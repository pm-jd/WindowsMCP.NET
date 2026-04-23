# App `ensure` / `status` Modes — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `ensure` and `status` modes to the `App` MCP tool so clients can focus-or-launch apps in a single call, driven by process state rather than screenshots.

**Architecture:** Extend `AppTools` with two new modes. Matching logic lives in a pure static helper (`ProcessWindowMatcher`) for testability. `DesktopService` grows `FindMatches`, `BringToForeground`, and a `LaunchApp` overload. Foreground-bringing uses the `AttachThreadInput` Win32 workaround to bypass foreground-lock restrictions. All existing modes (`launch`, `switch`, `resize`) remain unchanged.

**Tech Stack:** .NET 9 (Windows), ModelContextProtocol SDK, xUnit, FuzzySharp, System.Diagnostics.Process, User32 P/Invoke.

**Design doc:** `docs/plans/2026-04-23-app-ensure-mode-design.md` (commit `e402b5b`).

---

## Ground Rules

- **TDD:** Write the failing test first for every unit-testable piece. Parity tests may be added after the unit work.
- **Commit after every green task.** Small, reviewable commits.
- **Do NOT push** until explicitly asked by the user (see memory `feedback_no_auto_push.md`).
- **Build with `-c Release`** — the Debug exe may be locked by a running MCP instance.
- **Run tests with `-v q`** for quiet output unless debugging.

---

## Task 1: Add new Win32 P/Invoke declarations

**Files:**
- Modify: `src/WindowsMCP.NET/Native/User32.cs`

**Step 1: Add three new P/Invoke declarations**

Add inside `internal static partial class User32` (after existing declarations, before constants):

```csharp
[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo,
    [MarshalAs(UnmanagedType.Bool)] bool fAttach);

[LibraryImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool BringWindowToTop(nint hWnd);

[LibraryImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool IsIconic(nint hWnd);
```

`IsIconic` tests if a window is minimized — needed so we only call `ShowWindow(SW_RESTORE)` when actually minimized (restoring a maximized window would shrink it).

`Kernel32.GetCurrentThreadId` already exists in `src/WindowsMCP.NET/Native/Kernel32.cs` — no change needed there.

**Step 2: Verify build**

```bash
dotnet build src/WindowsMCP.NET -c Release
```
Expected: build succeeds with no errors.

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Native/User32.cs
git commit -m "feat: add AttachThreadInput, BringWindowToTop, IsIconic P/Invokes"
```

---

## Task 2: Add `ProcessSnapshot` model

**Files:**
- Create: `src/WindowsMCP.NET/Models/ProcessSnapshot.cs`

**Step 1: Write the file**

```csharp
namespace WindowsMcpNet.Models;

/// <summary>
/// Minimal projection of a running process, used by ProcessWindowMatcher
/// so matching logic can be unit-tested without touching real Process handles.
/// </summary>
public sealed record ProcessSnapshot(uint Pid, string Name);
```

**Step 2: Verify build**

```bash
dotnet build src/WindowsMCP.NET -c Release
```
Expected: build succeeds.

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Models/ProcessSnapshot.cs
git commit -m "feat: add ProcessSnapshot record for testable process matching"
```

---

## Task 3: `ProcessWindowMatcher` — write failing tests first (TDD)

**Files:**
- Create: `tests/WindowsMCP.NET.Tests/Services/ProcessWindowMatcherTests.cs`

**Step 1: Write the failing tests**

```csharp
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

public class ProcessWindowMatcherTests
{
    // Z-order is preserved from input order (first = topmost).
    private static WindowInfo Win(nint handle, string title, uint pid) =>
        new(handle, title, pid, 0, 0, 100, 100, true);

    [Fact]
    public void Match_ProcessNameExact_Found()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "chrome"),  Win(1, "Google - Chrome",  100)),
            (new ProcessSnapshot(200, "notepad"), Win(2, "Untitled - Notepad", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Single(result);
        Assert.Equal(2u, result[0].Window.Handle);
        Assert.Equal("notepad", result[0].ProcessName);
    }

    [Fact]
    public void Match_ProcessNameCaseInsensitive_Found()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "Notepad"), Win(1, "Untitled - Notepad", 100)),
        };

        var result = ProcessWindowMatcher.Match(input, "NOTEPAD");

        Assert.Single(result);
    }

    [Fact]
    public void Match_DotExeSuffix_NormalizedBothDirections()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"),     Win(1, "A - Notepad", 100)),
            (new ProcessSnapshot(200, "notepad.exe"), Win(2, "B - Notepad", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad.exe");
        Assert.Equal(2, result.Count);

        var result2 = ProcessWindowMatcher.Match(input, "notepad");
        Assert.Equal(2, result2.Count);
    }

    [Fact]
    public void Match_NoProcessMatch_FallsBackToFuzzyTitle()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "Code"), Win(1, "main.cs - Visual Studio Code",  100)),
            (new ProcessSnapshot(200, "foo"),  Win(2, "unrelated",                      200)),
        };

        var result = ProcessWindowMatcher.Match(input, "visual studio code");

        Assert.Single(result);
        Assert.Equal(1u, result[0].Window.Handle);
    }

    [Fact]
    public void Match_MultipleExact_PreservesInputZOrder()
    {
        // EnumWindows returns top-to-bottom Z-order.
        // Matcher must preserve that order for the tiebreaker.
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"), Win(1, "A - Notepad", 100)),
            (new ProcessSnapshot(200, "notepad"), Win(2, "B - Notepad", 200)),
            (new ProcessSnapshot(300, "notepad"), Win(3, "C - Notepad", 300)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Equal(3, result.Count);
        Assert.Equal(1u, result[0].Window.Handle);
        Assert.Equal(2u, result[1].Window.Handle);
        Assert.Equal(3u, result[2].Window.Handle);
    }

    [Fact]
    public void Match_ProcessNameBeatsFuzzyTitle()
    {
        // "notepad" as process name exists — fuzzy title match on another window must NOT appear.
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"), Win(1, "irrelevant",                  100)),
            (new ProcessSnapshot(200, "foo"),     Win(2, "notepad tutorial — browser", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Single(result);
        Assert.Equal(1u, result[0].Window.Handle);
    }

    [Fact]
    public void Match_EmptyInput_ReturnsEmpty()
    {
        var result = ProcessWindowMatcher.Match(
            Array.Empty<(ProcessSnapshot, WindowInfo)>(), "notepad");
        Assert.Empty(result);
    }

    [Fact]
    public void Match_FuzzyScoreBelowThreshold_Excluded()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "foo"), Win(1, "xyz", 100)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Empty(result);
    }
}
```

**Step 2: Run the tests — they should fail (type does not exist)**

```bash
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q
```
Expected: compilation error — `ProcessWindowMatcher` does not exist.

---

## Task 4: `ProcessWindowMatcher` — implement

**Files:**
- Create: `src/WindowsMCP.NET/Services/ProcessWindowMatcher.cs`

**Step 1: Write minimal implementation**

```csharp
using FuzzySharp;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public static class ProcessWindowMatcher
{
    private const int FuzzyThreshold = 60;

    /// <summary>
    /// Find matches for a given name against (process, window) candidates.
    /// Strategy:
    ///   1. Exact process-name match (case-insensitive, .exe suffix normalized)
    ///   2. Fuzzy window-title fallback (PartialRatio >= 60) if no process match
    /// Input order is preserved (callers pass Z-ordered lists from EnumWindows).
    /// </summary>
    public static List<(WindowInfo Window, string ProcessName)> Match(
        IEnumerable<(ProcessSnapshot Process, WindowInfo Window)> candidates,
        string name)
    {
        var needle = StripExe(name).ToLowerInvariant();

        var exactMatches = candidates
            .Where(c => StripExe(c.Process.Name).Equals(needle, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.Window, c.Process.Name))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        // Fuzzy-title fallback.
        return candidates
            .Select(c => (c.Window, c.Process.Name,
                Score: Fuzz.PartialRatio(name.ToLowerInvariant(), c.Window.Title.ToLowerInvariant())))
            .Where(x => x.Score >= FuzzyThreshold)
            .Select(x => (x.Window, x.Name))
            .ToList();
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
}
```

**Step 2: Run the tests — they should pass**

```bash
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q --filter "FullyQualifiedName~ProcessWindowMatcher"
```
Expected: 8 passed, 0 failed.

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/ProcessWindowMatcher.cs \
        tests/WindowsMCP.NET.Tests/Services/ProcessWindowMatcherTests.cs
git commit -m "feat: add ProcessWindowMatcher with unit tests"
```

---

## Task 5: Extend `DesktopService` — `FindMatches`

**Files:**
- Modify: `src/WindowsMCP.NET/Services/DesktopService.cs`

**Step 1: Add `FindMatches` method**

Append inside the `DesktopService` class (after `GetForegroundWindow`):

```csharp
/// <summary>
/// Enumerates all visible top-level windows with their owning process names,
/// then delegates matching to ProcessWindowMatcher.
/// Preserves Z-order (EnumWindows returns top-to-bottom).
/// </summary>
public List<(WindowInfo Window, string ProcessName)> FindMatches(string name)
{
    var windows = ListWindows();  // Z-ordered via EnumWindows
    var pidToName = new Dictionary<uint, string>();

    foreach (var proc in System.Diagnostics.Process.GetProcesses())
    {
        try
        {
            pidToName[(uint)proc.Id] = proc.ProcessName;
        }
        catch { /* access denied — skip */ }
        finally { proc.Dispose(); }
    }

    var candidates = windows
        .Where(w => pidToName.ContainsKey(w.ProcessId))
        .Select(w => (Process: new Models.ProcessSnapshot(w.ProcessId, pidToName[w.ProcessId]),
                      Window: w));

    return ProcessWindowMatcher.Match(candidates, name);
}
```

Note: `using WindowsMcpNet.Models;` may need to be added at the top of the file (verify — `WindowInfo` is already imported, `ProcessSnapshot` sits in the same namespace).

**Step 2: Verify build**

```bash
dotnet build src/WindowsMCP.NET -c Release
```
Expected: build succeeds.

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/DesktopService.cs
git commit -m "feat: add DesktopService.FindMatches for process+window lookup"
```

---

## Task 6: Extend `DesktopService` — `BringToForeground`

**Files:**
- Modify: `src/WindowsMCP.NET/Services/DesktopService.cs`

**Step 1: Add `BringToForeground` method**

Append after `FindMatches`:

```csharp
/// <summary>
/// Brings a window to the foreground using the AttachThreadInput Win32 workaround
/// to bypass foreground-lock restrictions common in remote/automation scenarios.
/// Returns true if SetForegroundWindow succeeded; false indicates a soft failure
/// (window may still have flashed in taskbar).
/// </summary>
public bool BringToForeground(nint hWnd)
{
    if (User32.IsIconic(hWnd))
        User32.ShowWindow(hWnd, User32.SW_RESTORE);

    uint currentThread = Kernel32.GetCurrentThreadId();
    uint targetThread  = User32.GetWindowThreadProcessId(hWnd, out _);

    if (currentThread == targetThread)
    {
        return User32.SetForegroundWindow(hWnd);
    }

    bool attached = User32.AttachThreadInput(currentThread, targetThread, true);
    try
    {
        bool ok = User32.SetForegroundWindow(hWnd);
        User32.BringWindowToTop(hWnd);
        return ok;
    }
    finally
    {
        if (attached)
            User32.AttachThreadInput(currentThread, targetThread, false);
    }
}
```

**Step 2: Verify build**

```bash
dotnet build src/WindowsMCP.NET -c Release
```
Expected: build succeeds.

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/DesktopService.cs
git commit -m "feat: add DesktopService.BringToForeground with AttachThreadInput trick"
```

---

## Task 7: Add `LaunchApp` overload with explicit launch command

**Files:**
- Modify: `src/WindowsMCP.NET/Services/DesktopService.cs`

**Step 1: Refactor `LaunchApp` into an overload**

Replace the existing `LaunchApp(string name)` method with:

```csharp
public Task<WindowInfo?> LaunchApp(string name) => LaunchApp(name, null);

public async Task<WindowInfo?> LaunchApp(string name, string? launchCommand)
{
    var target = string.IsNullOrWhiteSpace(launchCommand) ? name : launchCommand;
    _logger.LogInformation("Launching app: {Name} (command={Command})", name, target);

    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = target,
        UseShellExecute = true,
    });

    if (process is null) return null;

    process.WaitForInputIdle(3000);
    await Task.Delay(500);

    var windows = ListWindows();
    return windows.FirstOrDefault(w => w.ProcessId == (uint)process.Id);
}
```

**Step 2: Verify build and existing tests still pass**

```bash
dotnet build src/WindowsMCP.NET -c Release
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q
```
Expected: all existing tests still pass (the 1-arg overload preserves prior signature).

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/DesktopService.cs
git commit -m "feat: DesktopService.LaunchApp accepts optional launchCommand override"
```

---

## Task 8: `AppTools.ensure` / `status` — write failing unit tests

**Files:**
- Create: `tests/WindowsMCP.NET.Tests/Tools/AppToolsTests.cs`

**Step 1: Write the failing tests**

```csharp
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class AppToolsTests
{
    private static Microsoft.Extensions.Logging.ILogger<WindowsMcpNet.Services.DesktopService> NullLog =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsMcpNet.Services.DesktopService>.Instance;

    [Fact]
    public async Task Ensure_EmptyName_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "ensure", name: "");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'name' is required", result);
    }

    [Fact]
    public async Task Status_EmptyName_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "status", name: "");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'name' is required", result);
    }

    [Fact]
    public async Task Ensure_UnknownAmbiguous_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "ensure", name: "notepad", ambiguous: "bogus");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'ambiguous'", result);
    }

    [Fact]
    public async Task App_UnknownMode_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "quark", name: "notepad");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("Unknown mode", result);
    }

    [Fact]
    public void App_Description_ListsAllFiveModes()
    {
        var method = typeof(AppTools).GetMethod(nameof(AppTools.App))!;
        var attr = (System.ComponentModel.DescriptionAttribute)
            method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)[0];
        Assert.Contains("launch",  attr.Description);
        Assert.Contains("ensure",  attr.Description);
        Assert.Contains("status",  attr.Description);
        Assert.Contains("switch",  attr.Description);
        Assert.Contains("resize",  attr.Description);
    }
}
```

**Step 2: Run the tests — they should fail**

```bash
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q --filter "FullyQualifiedName~AppToolsTests"
```
Expected: 5 tests fail (unknown mode `ensure`, `status`, description missing modes, etc).

---

## Task 9: Implement `ensure` and `status` in `AppTools`

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/AppTools.cs`

**Step 1: Update tool description + add new parameters**

Replace the method signature and `[Description]` attribute:

```csharp
[McpServerTool(Name = "App", Destructive = true, OpenWorld = true, ReadOnly = false)]
[Description("Launch, focus, check, or resize a window. " +
             "mode: launch (start app), ensure (focus if running, else launch), " +
             "status (check if running), switch (focus by window title), resize.")]
public static async Task<string> App(
    DesktopService desktopService,
    [Description("Mode: launch, ensure, status, switch, or resize")] string mode = "launch",
    [Description("App name. For ensure/status: process name (e.g. 'notepad'); " +
                 "falls back to fuzzy window title match. For launch: executable/URI.")]
    string name = "",
    [Description("Window position as [x, y] (for resize)")] JsonElement? window_loc = null,
    [Description("Window size as [width, height] (for resize)")] JsonElement? window_size = null,
    [Description("Optional launch command for mode=ensure when process not found " +
                 "(e.g. full path or URI like 'ms-teams:'). Defaults to `name`.")]
    string? launch_command = null,
    [Description("Behavior on multiple matches (ensure/status): 'first' (default) or 'error'.")]
    string ambiguous = "first")
{
    try
    {
        return mode.ToLowerInvariant() switch
        {
            "launch" => await LaunchApp(desktopService, name),
            "ensure" => await EnsureApp(desktopService, name, launch_command, ambiguous),
            "status" => StatusApp(desktopService, name, ambiguous),
            "switch" => SwitchToApp(desktopService, name),
            "resize" => ResizeApp(desktopService, name, window_loc, window_size),
            _ => throw new ArgumentException(
                $"Unknown mode '{mode}'. Use: launch, ensure, status, switch, or resize.")
        };
    }
    catch (Exception ex)
    {
        return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
    }
}
```

**Step 2: Add `EnsureApp` and `StatusApp` private helpers**

Append inside the class:

```csharp
private static async Task<string> EnsureApp(DesktopService desktopService,
    string name, string? launchCommand, string ambiguous)
{
    if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("'name' is required for mode=ensure.");
    ValidateAmbiguous(ambiguous);

    var matches = desktopService.FindMatches(name);

    if (matches.Count == 0)
    {
        var window = await desktopService.LaunchApp(name, launchCommand);
        if (window is null)
            return $"Launched '{name}' (window not yet visible)";
        return $"Launched '{name}' — window: \"{window.Title}\" PID={window.ProcessId}";
    }

    if (matches.Count > 1 && ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
        return FormatAmbiguous(matches);

    var (target, _) = matches[0];
    bool focused = desktopService.BringToForeground(target.Handle);
    return focused
        ? $"Focused \"{target.Title}\" (PID={target.ProcessId})"
        : $"Attempted focus on \"{target.Title}\" (PID={target.ProcessId}) — window may not have come to front";
}

private static string StatusApp(DesktopService desktopService, string name, string ambiguous)
{
    if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("'name' is required for mode=status.");
    ValidateAmbiguous(ambiguous);

    var matches = desktopService.FindMatches(name);

    if (matches.Count == 0)
        return "Not running";

    if (matches.Count > 1 && ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
        return FormatAmbiguous(matches);

    var (target, _) = matches[0];
    return $"Running: PID={target.ProcessId}, window=\"{target.Title}\"";
}

private static void ValidateAmbiguous(string ambiguous)
{
    if (!ambiguous.Equals("first", StringComparison.OrdinalIgnoreCase) &&
        !ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("'ambiguous' must be 'first' or 'error'.");
}

private static string FormatAmbiguous(List<(WindowInfo Window, string ProcessName)> matches)
{
    var parts = matches.Select(m =>
        $"[PID {m.Window.ProcessId} '{m.ProcessName}' — \"{m.Window.Title}\"]");
    return $"Multiple matches: {string.Join(", ", parts)}. Specify a more specific name.";
}
```

You also need a `using WindowsMcpNet.Models;` at the top of the file (for `WindowInfo` in the helper signature).

**Step 3: Run unit tests — they should pass**

```bash
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q
```
Expected: all unit tests pass (including the 5 new AppToolsTests + 8 ProcessWindowMatcherTests + existing tests).

**Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Tools/AppTools.cs \
        tests/WindowsMCP.NET.Tests/Tools/AppToolsTests.cs
git commit -m "feat: add App ensure/status modes for process-based focus+launch"
```

---

## Task 10: Add parity tests for real desktop interaction

**Files:**
- Modify: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/AppToolsParityTests.cs`

**Step 1: Add new tests at the end of the class**

Insert before the closing brace of `AppToolsParityTests`:

```csharp
[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Ensure_NotRunning_LaunchesApp()
{
    // Ensure no notepads are running at the start
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
        try { p.Kill(entireProcessTree: true); } catch { }
    await Task.Delay(500);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "ensure",
        ["name"] = "notepad"
    });

    _output.WriteLine($"ensure (not running) result: {result}");
    await Task.Delay(1000);

    Assert.Contains("Launched", result);
    Assert.True(System.Diagnostics.Process.GetProcessesByName("notepad").Length > 0);
}

[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Ensure_AlreadyRunning_Focuses()
{
    // Pre-launch
    await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "launch",
        ["name"] = "notepad.exe"
    });
    await Task.Delay(1500);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "ensure",
        ["name"] = "notepad"
    });

    _output.WriteLine($"ensure (running) result: {result}");
    Assert.Contains("Focused", result);
}

[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Status_NotRunning_ReturnsNotRunning()
{
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
        try { p.Kill(entireProcessTree: true); } catch { }
    await Task.Delay(500);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "status",
        ["name"] = "notepad"
    });

    _output.WriteLine($"status (not running) result: {result}");
    Assert.Equal("Not running", result.Trim());
}

[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Status_Running_ReturnsPidAndTitle()
{
    await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "launch",
        ["name"] = "notepad.exe"
    });
    await Task.Delay(1500);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "status",
        ["name"] = "notepad"
    });

    _output.WriteLine($"status (running) result: {result}");
    Assert.Contains("Running:", result);
    Assert.Contains("PID=", result);
}

[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Ensure_MultipleMatches_Error_ReturnsList()
{
    // Launch two notepad instances
    await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "launch", ["name"] = "notepad.exe"
    });
    await Task.Delay(800);
    await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"] = "launch", ["name"] = "notepad.exe"
    });
    await Task.Delay(1200);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"]      = "ensure",
        ["name"]      = "notepad",
        ["ambiguous"] = "error"
    });

    _output.WriteLine($"ensure ambiguous=error result: {result}");
    Assert.Contains("Multiple matches:", result);
}

[Fact]
[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public async Task Ensure_LaunchCommandOverride_UsesOverride()
{
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
        try { p.Kill(entireProcessTree: true); } catch { }
    await Task.Delay(500);

    var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
    {
        ["mode"]           = "ensure",
        ["name"]           = "definitely-not-a-real-app",
        ["launch_command"] = "notepad.exe"
    });

    _output.WriteLine($"ensure with launch_command result: {result}");
    await Task.Delay(1000);

    // launch_command fires only when no match → notepad should start
    Assert.True(System.Diagnostics.Process.GetProcessesByName("notepad").Length > 0);
}
```

**Step 2: Build and run parity tests**

```bash
dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q
```
Expected: all parity tests pass. Requires an active desktop session — skip this step if running under a service account.

Note: `Ensure_MultipleMatches_Error_ReturnsList` may be flaky on slow machines because the second notepad window takes longer to appear. If it fails intermittently, increase the `Task.Delay(1200)` to `Task.Delay(2000)`.

**Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/AppToolsParityTests.cs
git commit -m "test: parity tests for App ensure/status modes"
```

---

## Task 11: Schema parity baseline refresh

**Context:** `ToolSchemaParityTests` compares the emitted JSON schema for each tool against baseline files. Adding two new parameters (`launch_command`, `ambiguous`) will change the `App` schema, so the baseline needs updating.

**Files:**
- Check: `tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/ToolSchemaParityTests.cs`
- Likely touch: baseline JSON files under `tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/Baselines/` (exact path depends on repo layout — run the schema test and follow the failure message)

**Step 1: Run the schema parity test**

```bash
dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v n --filter "FullyQualifiedName~ToolSchemaParityTests"
```
Expected: at least one failure mentioning `App`. The failure message typically includes a diff and a hint about how to regenerate the baseline.

**Step 2: Regenerate the baseline**

Read the test infrastructure (`BaselineManager.cs`, `SchemaComparer.cs`) to find the regeneration mechanism — often an environment variable like `UPDATE_BASELINES=true` or a `--update` filter. Follow the documented mechanism.

If no such mechanism exists, update the baseline JSON file manually: it should reflect the new `App` schema including `launch_command` and `ambiguous` parameters.

**Step 3: Re-run to confirm the baseline matches**

```bash
dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q --filter "FullyQualifiedName~ToolSchemaParityTests"
```
Expected: all schema tests pass.

**Step 4: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/
git commit -m "test: refresh App schema baseline for ensure/status modes"
```

---

## Task 12: Update CLAUDE.md documentation

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update the tool table row for `App`**

Find the line:
```markdown
| **App** | Launch, switch to, or resize windows |
```
Replace with:
```markdown
| **App** | Launch, focus, check, or resize windows. Modes: launch, ensure, status, switch, resize |
```

**Step 2: Add a bullet to the "Key Patterns" section**

Find the existing `## Key Patterns` section. Append a bullet:

```markdown
- **Foreground without screenshots**: Use `App(mode="ensure", name="notepad")` to focus an app if running, launch if not — one call instead of screenshot+parse workflow.
```

**Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document App ensure/status modes in CLAUDE.md"
```

---

## Task 13: Full regression run

**Step 1: Clean build and run all unit tests**

```bash
dotnet build src/WindowsMCP.NET -c Release
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q
```
Expected: everything green.

**Step 2: Run all parity tests (desktop session required)**

```bash
dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q
```
Expected: all 38+ tests green (original 38 + 6 new parity tests from Task 10).

**Step 3: Smoke-test the MCP tool from Claude Code**

Start the server locally (`dotnet run --project src/WindowsMCP.NET -c Release -- --transport stdio` or equivalent HTTP start) and issue these calls through Claude:

1. `App(mode="status", name="notepad")` — expect `"Not running"` initially
2. `App(mode="ensure", name="notepad")` — expect `"Launched 'notepad' ..."` 
3. `App(mode="ensure", name="notepad")` again — expect `"Focused ..."` 
4. `App(mode="status", name="notepad")` — expect `"Running: PID=X, window=..."`

**Step 4: Final summary commit (if no code changes needed after smoke test)**

No commit required if smoke test passes.

---

## Out of scope (do NOT implement in this plan)

- App-alias registry (Q3 Option C from brainstorming)
- Explicit Z-order sort via `GetWindow(GW_HWNDPREV)` — `EnumWindows` already Z-orders
- `SendInput(0)` pre-focus workaround — AttachThreadInput is sufficient
- Timeout parameter for `ensure` — inherits `LaunchApp` 3.5s wait
- Wait-for-window-visible mode — client composes with `Wait`
- Cycle-through-multiple-windows mode

If any of these turn out to be needed in practice, open a follow-up design doc.

---

## Rollback plan

Each task is a single commit, so partial rollback is:

```bash
# Roll back to pre-feature state
git reset --hard e402b5b   # design-doc commit
```

Safe: all changes are additive. Existing modes (`launch`, `switch`, `resize`) are unchanged and still tested.
