# Perform & Context Tools Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add two new MCP tools — `Perform` (batched UI action chains) and `Context` (on-demand system state) — to reduce roundtrips by ~60% for UI automation workflows.

**Architecture:** Both tools are new static classes in `Tools/`, reusing existing services via DI. `Perform` dispatches steps to the same logic as InputTools. `Context` composes existing services (DesktopService, ScreenCaptureService, UiTreeService). Both return `IList<ContentBlock>` (text + optional image). Error handling uses try-catch with `[ERROR]` prefix pattern.

**Tech Stack:** C# / .NET 9, ModelContextProtocol SDK v1.2.0, FlaUI, System.Text.Json, xUnit

---

### Task 1: Context Tool — Tests

**Files:**
- Create: `tests/WindowsMCP.NET.Tests/Tools/ContextToolsTests.cs`

**Step 1: Write the tests**

These tests verify the tool method signature, parameter handling, and error behavior. Since `DesktopService`, `ScreenCaptureService`, and `UiTreeService` require a Windows desktop (P/Invoke), the unit tests focus on parameter validation and the composition logic. Integration testing happens manually via MCP.

```csharp
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ContextToolsTests
{
    [Fact]
    public void DefaultInclude_ReturnsWindowAndScreen()
    {
        // Default include is ["window", "screen"] — verify the method accepts null
        // Since we can't call the real method without services, we test the static helper
        var modules = ContextTools.ParseInclude(null);
        Assert.Contains("window", modules);
        Assert.Contains("screen", modules);
        Assert.Equal(2, modules.Count);
    }

    [Fact]
    public void ParseInclude_WithExplicitModules()
    {
        var json = JsonDocument.Parse("""["window", "clipboard", "processes"]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Equal(3, modules.Count);
        Assert.Contains("clipboard", modules);
        Assert.DoesNotContain("screen", modules);
    }

    [Fact]
    public void ParseInclude_UnknownModule_IsIgnored()
    {
        var json = JsonDocument.Parse("""["window", "bogus"]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Single(modules);
        Assert.Contains("window", modules);
    }

    [Fact]
    public void ParseInclude_EmptyArray_ReturnsDefault()
    {
        var json = JsonDocument.Parse("""[]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Contains("window", modules);
        Assert.Contains("screen", modules);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "ContextToolsTests" -c Release -v q`
Expected: FAIL (ContextTools class does not exist yet)

---

### Task 2: Context Tool — Implementation

**Files:**
- Create: `src/WindowsMCP.NET/Tools/ContextTools.cs`

**Step 1: Implement ContextTools.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ContextTools
{
    private static readonly HashSet<string> ValidModules = ["window", "screen", "ui_tree", "clipboard", "processes"];
    private static readonly List<string> DefaultModules = ["window", "screen"];

    [McpServerTool(Name = "Context", ReadOnly = true, Idempotent = true)]
    [Description("Get current system state. include: array of modules to return — " +
                 "window (active window info), screen (screenshot), ui_tree (UI element labels), " +
                 "clipboard (text content), processes (top processes). Default: [window, screen].")]
    public static IList<ContentBlock> Context(
        DesktopService desktopService,
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Modules to include: window, screen, ui_tree, clipboard, processes")] JsonElement? include = null)
    {
        var modules = ParseInclude(include);
        var sb = new StringBuilder();
        byte[]? screenshotPng = null;

        foreach (var module in modules)
        {
            try
            {
                switch (module)
                {
                    case "window":
                        AppendWindowInfo(sb, desktopService);
                        break;
                    case "screen":
                        screenshotPng = captureService.CaptureScreen(null);
                        break;
                    case "ui_tree":
                        AppendUiTree(sb, uiTreeService, captureService, ref screenshotPng);
                        break;
                    case "clipboard":
                        AppendClipboard(sb);
                        break;
                    case "processes":
                        AppendProcesses(sb);
                        break;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"-- {module} --");
                sb.AppendLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                sb.AppendLine();
            }
        }

        var result = new List<ContentBlock>();
        if (screenshotPng is not null)
            result.Add(ImageContentBlock.FromBytes(screenshotPng, "image/png"));
        if (sb.Length > 0)
            result.Add(new TextContentBlock { Text = sb.ToString().TrimEnd() });
        return result;
    }

    public static List<string> ParseInclude(JsonElement? include)
    {
        if (!include.HasValue || include.Value.ValueKind != JsonValueKind.Array)
            return new List<string>(DefaultModules);

        var arr = include.Value;
        if (arr.GetArrayLength() == 0)
            return new List<string>(DefaultModules);

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var val = item.GetString()?.ToLowerInvariant();
            if (val is not null && ValidModules.Contains(val))
                result.Add(val);
        }
        return result.Count > 0 ? result : new List<string>(DefaultModules);
    }

    private static void AppendWindowInfo(StringBuilder sb, DesktopService desktopService)
    {
        sb.AppendLine("-- Active Window --");
        var win = desktopService.GetForegroundWindow();
        if (win is null)
        {
            sb.AppendLine("(no foreground window)");
        }
        else
        {
            string processName = "(unknown)";
            try { processName = Process.GetProcessById((int)win.ProcessId).ProcessName; } catch { }
            sb.AppendLine($"Title:    {win.Title}");
            sb.AppendLine($"Process:  {processName} (PID {win.ProcessId})");
            sb.AppendLine($"Bounds:   [{win.X}, {win.Y}, {win.Width}, {win.Height}]");
        }
        sb.AppendLine();
    }

    private static void AppendUiTree(StringBuilder sb, UiTreeService uiTreeService,
        ScreenCaptureService captureService, ref byte[]? screenshotPng)
    {
        uiTreeService.InvalidateCache();
        var tree = uiTreeService.BuildAnnotatedTree();
        sb.AppendLine($"-- UI Tree ({tree.LabelMap.Count} interactive elements) --");
        sb.AppendLine(tree.ToText());

        // If we also have a screenshot, annotate it with labels
        if (screenshotPng is not null)
        {
            var points = uiTreeService.GetAnnotationPoints()
                .Select(p => (p.X, p.Y, p.Label))
                .ToList<(int X, int Y, string Label)>();
            screenshotPng = captureService.AnnotateScreenshot(screenshotPng, points);
        }
    }

    private static void AppendClipboard(StringBuilder sb)
    {
        sb.AppendLine("-- Clipboard --");
        var text = ClipboardTools.Clipboard("get");
        if (text.Length > 1000)
            text = text[..1000] + $"... ({text.Length} chars total)";
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static void AppendProcesses(StringBuilder sb)
    {
        sb.AppendLine("-- Top Processes --");
        sb.AppendLine($"{"PID",7}  {"Memory",9}  Window Title");
        var processes = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != nint.Zero; } catch { return false; } })
            .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
            .Take(10)
            .ToList();

        foreach (var p in processes)
        {
            try
            {
                var mem = p.WorkingSet64 / (1024 * 1024);
                var title = p.MainWindowTitle;
                if (string.IsNullOrEmpty(title)) title = p.ProcessName;
                sb.AppendLine($"{p.Id,7}  {mem,6} MB  {title}");
            }
            catch { }
        }
        sb.AppendLine();
    }
}
```

**Step 2: Run tests**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "ContextToolsTests" -c Release -v q`
Expected: ALL 4 tests PASS

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Tools/ContextTools.cs tests/WindowsMCP.NET.Tests/Tools/ContextToolsTests.cs
git commit -m "feat: add Context tool for on-demand system state"
```

---

### Task 3: Perform Tool — Tests

**Files:**
- Create: `tests/WindowsMCP.NET.Tests/Tools/PerformToolsTests.cs`

**Step 1: Write the tests**

```csharp
using System.Text.Json;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class PerformToolsTests
{
    [Fact]
    public void ParseSteps_ValidActions()
    {
        var json = JsonDocument.Parse("""
            [
                {"action": "click", "label": "3"},
                {"action": "type", "text": "hello"},
                {"action": "shortcut", "shortcut": "ctrl+s"},
                {"action": "wait", "duration": 1},
                {"action": "scroll", "direction": "down"},
                {"action": "move", "loc": [100, 200]}
            ]
        """).RootElement;

        var steps = PerformTools.ParseSteps(json);
        Assert.Equal(6, steps.Count);
        Assert.Equal("click", steps[0].Action);
        Assert.Equal("type", steps[1].Action);
        Assert.Equal("shortcut", steps[2].Action);
        Assert.Equal("wait", steps[3].Action);
        Assert.Equal("scroll", steps[4].Action);
        Assert.Equal("move", steps[5].Action);
    }

    [Fact]
    public void ParseSteps_UnknownAction_Rejected()
    {
        var json = JsonDocument.Parse("""[{"action": "explode"}]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Single(steps);
        Assert.Equal("explode", steps[0].Action);
        Assert.True(steps[0].IsUnknown);
    }

    [Fact]
    public void ParseSteps_EmptyArray()
    {
        var json = JsonDocument.Parse("""[]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Empty(steps);
    }

    [Fact]
    public void ParseSteps_MissingAction_Rejected()
    {
        var json = JsonDocument.Parse("""[{"text": "no action field"}]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Single(steps);
        Assert.True(steps[0].IsUnknown);
    }

    [Fact]
    public void FormatResults_AllSuccess()
    {
        var results = new List<PerformTools.StepResult>
        {
            new(1, true, "Clicked left at (100,200)"),
            new(2, true, "Typed 5 character(s)"),
        };
        var text = PerformTools.FormatResults(results, false);
        Assert.Contains("Step 1: OK", text);
        Assert.Contains("Step 2: OK", text);
        Assert.Contains("2/2 succeeded", text);
    }

    [Fact]
    public void FormatResults_WithFailure_StopOnError()
    {
        var results = new List<PerformTools.StepResult>
        {
            new(1, true, "Clicked"),
            new(2, false, "Label '99' not found"),
        };
        var text = PerformTools.FormatResults(results, true);
        Assert.Contains("Step 2: FAIL", text);
        Assert.Contains("1/2 succeeded", text);
        Assert.Contains("stop_on_error", text);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "PerformToolsTests" -c Release -v q`
Expected: FAIL (PerformTools class does not exist yet)

---

### Task 4: Perform Tool — Implementation

**Files:**
- Create: `src/WindowsMCP.NET/Tools/PerformTools.cs`

**Step 1: Implement PerformTools.cs**

```csharp
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class PerformTools
{
    private static readonly HashSet<string> ValidActions = ["click", "type", "shortcut", "scroll", "move", "wait"];

    [McpServerTool(Name = "Perform", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Execute a sequence of UI actions in one call. " +
                 "steps: array of {action, ...params}. " +
                 "Supported actions: click, type, shortcut, scroll, move, wait. " +
                 "Returns step-by-step results with optional screenshot.")]
    public static async Task<IList<ContentBlock>> Perform(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Array of action steps: [{action, ...params}]")] JsonElement steps,
        [Description("Stop executing on first error")] bool stop_on_error = true,
        [Description("Capture screenshot after execution")] bool snapshot_after = true,
        [Description("Milliseconds to wait between steps")] int delay_between_ms = 100)
    {
        var parsed = ParseSteps(steps);
        if (parsed.Count == 0)
            return [new TextContentBlock { Text = "[ERROR] No steps provided." }];

        var results = new List<StepResult>();
        bool stopped = false;

        for (int i = 0; i < parsed.Count; i++)
        {
            var step = parsed[i];
            var stepNum = i + 1;

            if (step.IsUnknown)
            {
                results.Add(new StepResult(stepNum, false, $"Unknown action '{step.Action}'"));
                if (stop_on_error) { stopped = true; break; }
                continue;
            }

            try
            {
                var msg = await ExecuteStep(step, uiTreeService);
                results.Add(new StepResult(stepNum, true, msg));
            }
            catch (Exception ex)
            {
                results.Add(new StepResult(stepNum, false, $"{ex.GetType().Name}: {ex.Message}"));
                if (stop_on_error) { stopped = true; break; }
            }

            if (i < parsed.Count - 1 && delay_between_ms > 0)
                await Task.Delay(delay_between_ms);
        }

        var text = FormatResults(results, stopped);
        var content = new List<ContentBlock> { new TextContentBlock { Text = text } };

        if (snapshot_after)
        {
            try
            {
                var pngBytes = captureService.CaptureScreen(null);
                var points = uiTreeService.GetAnnotationPoints()
                    .Select(p => (p.X, p.Y, p.Label))
                    .ToList<(int X, int Y, string Label)>();
                pngBytes = captureService.AnnotateScreenshot(pngBytes, points);
                content.Insert(0, ImageContentBlock.FromBytes(pngBytes, "image/png"));
            }
            catch { /* screenshot is best-effort */ }
        }

        return content;
    }

    private static async Task<string> ExecuteStep(ParsedStep step, UiTreeService uiTreeService)
    {
        return step.Action switch
        {
            "click" => InputTools.Click(uiTreeService,
                loc: step.Get("loc"),
                label: step.GetString("label"),
                button: step.GetString("button") ?? "left",
                clicks: step.GetInt("clicks") ?? 1),

            "type" => InputTools.Type(uiTreeService,
                text: step.GetString("text") ?? throw new ArgumentException("'text' required for type action"),
                label: step.GetString("label"),
                loc: step.Get("loc"),
                clear: step.GetBool("clear"),
                press_enter: step.GetBool("press_enter")),

            "shortcut" => InputTools.Shortcut(
                shortcut: step.GetString("shortcut") ?? throw new ArgumentException("'shortcut' required")),

            "scroll" => InputTools.Scroll(uiTreeService,
                direction: step.GetString("direction") ?? "down",
                wheel_times: step.GetInt("wheel_times") ?? 3,
                loc: step.Get("loc"),
                label: step.GetString("label")),

            "move" => InputTools.Move(uiTreeService,
                loc: step.Get("loc"),
                label: step.GetString("label"),
                drag: step.GetBool("drag")),

            "wait" => await InputTools.Wait(
                duration: step.GetInt("duration") ?? 1),

            _ => throw new ArgumentException($"Unknown action: {step.Action}")
        };
    }

    // --- Public helpers for testing ---

    public static List<ParsedStep> ParseSteps(JsonElement stepsElement)
    {
        var result = new List<ParsedStep>();
        if (stepsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in stepsElement.EnumerateArray())
        {
            var action = item.TryGetProperty("action", out var actionProp)
                ? actionProp.GetString()?.ToLowerInvariant()
                : null;
            var isUnknown = action is null || !ValidActions.Contains(action);
            result.Add(new ParsedStep(action ?? "(missing)", item, isUnknown));
        }
        return result;
    }

    public static string FormatResults(List<StepResult> results, bool stoppedEarly)
    {
        var sb = new StringBuilder();
        int succeeded = results.Count(r => r.Success);
        int total = results.Count;

        foreach (var r in results)
        {
            sb.AppendLine($"Step {r.StepNumber}: {(r.Success ? "OK" : "FAIL")} — {r.Message}");
        }

        sb.AppendLine();
        if (stoppedEarly)
            sb.AppendLine($"Stopped after step {results[^1].StepNumber} (stop_on_error=true). {succeeded}/{total} succeeded.");
        else
            sb.AppendLine($"Completed. {succeeded}/{total} succeeded.");

        return sb.ToString().TrimEnd();
    }

    // --- Types ---

    public sealed class ParsedStep
    {
        public string Action { get; }
        public bool IsUnknown { get; }
        private readonly JsonElement _raw;

        public ParsedStep(string action, JsonElement raw, bool isUnknown)
        {
            Action = action;
            _raw = raw;
            IsUnknown = isUnknown;
        }

        public string? GetString(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public int? GetInt(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        public bool GetBool(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

        public JsonElement? Get(string prop) =>
            _raw.TryGetProperty(prop, out var v) ? v : null;
    }

    public sealed record StepResult(int StepNumber, bool Success, string Message);
}
```

**Step 2: Run tests**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "PerformToolsTests" -c Release -v q`
Expected: ALL 6 tests PASS

**Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Tools/PerformTools.cs tests/WindowsMCP.NET.Tests/Tools/PerformToolsTests.cs
git commit -m "feat: add Perform tool for batched UI action chains"
```

---

### Task 5: Full Test Suite + Build Validation

**Step 1: Run all unit tests**

Run: `dotnet test tests/WindowsMCP.NET.Tests -c Release -v q`
Expected: ALL tests pass (28 existing + 10 new = 38)

**Step 2: Build release**

Run: `dotnet build src/WindowsMCP.NET -c Release`
Expected: Build succeeds without errors

**Step 3: Commit any fixups if needed, then push**

```bash
git push
```

---

## Summary

| Task | What | Files | Tests |
|------|------|-------|-------|
| 1 | Context tool tests | Tests/ContextToolsTests.cs | 4 |
| 2 | Context tool implementation | Tools/ContextTools.cs | 4 pass |
| 3 | Perform tool tests | Tests/PerformToolsTests.cs | 6 |
| 4 | Perform tool implementation | Tools/PerformTools.cs | 6 pass |
| 5 | Full validation + push | — | 38 pass |
