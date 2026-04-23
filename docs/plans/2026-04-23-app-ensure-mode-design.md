# App `ensure` / `status` Modes — Design

**Date:** 2026-04-23
**Status:** Approved, ready for implementation plan

## Problem

The current `App` tool has `launch`, `switch`, and `resize` modes. `switch` matches on **window titles** via fuzzy matching, which is fragile — users must guess titles like `"Unbenannt - Editor"` rather than app names like `"notepad"`. There is no single-call operation to **check if an app is running, focus it if yes, launch it if no**. Clients currently need two round-trips (`switch` → parse result → `launch` on failure) plus screenshot inspection to verify the result.

## Goal

Enable Claude-style automation workflows to bring applications to the foreground **without screenshots or taskbar inspection**. Instead, decide directly from the process list whether the app is running, focus it, or launch it.

## Approved Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Single-call API | One `ensure` mode (Option A from brainstorming Q1) |
| 2 | Matching | Process-name first, fuzzy-title fallback (Option B from Q2) |
| 3 | Launch behavior | Optional `launch_command`; fall back to `name` if omitted (Option A from Q3) |
| 4 | Multiple matches | Default = first visible window; `ambiguous="error"` returns structured list (A+C from Q4) |
| 5 | Architecture | Extend existing `App` tool (Option 1 from approach comparison) |

## API

```csharp
[McpServerTool(Name = "App", ...)]
[Description("Launch, focus, check, or resize a window. " +
             "mode: launch (start app), ensure (focus if running, else launch), " +
             "status (check if running), switch (focus by window title), resize.")]
public static async Task<string> App(
    DesktopService desktopService,
    string mode = "launch",
    [Description("App name. For ensure/status: process name (e.g. 'notepad'); " +
                 "falls back to fuzzy window title match. For launch: executable/URI.")]
    string name = "",
    [Description("Optional launch command for mode=ensure when process not found " +
                 "(e.g. full path or URI like 'ms-teams:'). Defaults to `name`.")]
    string? launch_command = null,
    [Description("Behavior on multiple matches: 'first' (default) or 'error' (list all).")]
    string ambiguous = "first",
    JsonElement? window_loc = null,
    JsonElement? window_size = null);
```

### Return strings

- `ensure` → `"Focused '<title>' (PID=X)"` or `"Launched '<name>' — window: '<title>' PID=X"` or `"Launched '<name>' (window not yet visible)"`
- `ensure` with `ambiguous="error"` and multiple matches → `"Multiple matches: [PID 1234 'Foo.exe' — 'Title A'], [PID 5678 'Foo.exe' — 'Title B']. Specify a more specific name."`
- `status` → `"Running: PID=X, window='<title>'"` or `"Not running"` or ambiguous list

All existing modes (`launch`, `switch`, `resize`) are unchanged. Parameters are additive — no breaking changes.

## Architecture

### New logic in `DesktopService`

```csharp
public List<(WindowInfo Window, string ProcessName)> FindMatches(string name);
public WindowInfo BringToForeground(WindowInfo window);
public async Task<WindowInfo?> LaunchApp(string name, string? launchCommand);  // overload
```

### Pure helper: `ProcessWindowMatcher`

Extracted to its own static class so unit tests can exercise matching logic without `Process.GetProcesses()`:

```csharp
public sealed record ProcessSnapshot(uint Pid, string Name);

public static class ProcessWindowMatcher
{
    public static List<(WindowInfo Window, string ProcessName)> Match(
        IEnumerable<(ProcessSnapshot Process, WindowInfo Window)> candidates,
        string name);
}
```

### Matching algorithm

1. Enumerate processes, filter `MainWindowHandle != 0`
2. Exact process-name match (case-insensitive, `.exe` suffix normalized)
3. If zero exact matches → fuzzy-title fallback via `Fuzz.PartialRatio` on `MainWindowTitle` (threshold 60)
4. Sort: **Z-order** (preserved from `EnumWindows` output — top-to-bottom order is documented behavior) → secondary: fuzzy score when applicable
5. Return list with `ProcessName` for ambiguous-output formatting

### `ensure` flow

```
matches = desktopService.FindMatches(name)

if matches.Count == 0:
    → LaunchApp(name, launch_command ?? name)
    → return "Launched ..."

if matches.Count == 1 OR ambiguous == "first":
    → BringToForeground(matches[0].Window)
    → return "Focused ..."

if matches.Count > 1 AND ambiguous == "error":
    → return "Multiple matches: ..."
```

### `BringToForeground` — robust focus

Windows restricts `SetForegroundWindow`; plain call often only flashes the taskbar icon on remote automation. Use the standard Win32 workaround:

```
1. If minimized → ShowWindow(SW_RESTORE)
2. GetWindowThreadProcessId(hWnd) → targetThread
3. AttachThreadInput(currentThread, targetThread, true)
4. SetForegroundWindow(hWnd)
5. BringWindowToTop(hWnd)
6. AttachThreadInput(currentThread, targetThread, false)
```

New P/Invoke declarations (~10 lines):
- `User32.AttachThreadInput`, `User32.BringWindowToTop`
- `Kernel32.GetCurrentThreadId` (verify if already present)

## Error Handling

Follows existing pattern: all exceptions → `[ERROR] Type: message` strings, never propagated.

| Scenario | Behavior |
|---|---|
| `name` empty (ensure/status) | `[ERROR] ArgumentException: 'name' is required for mode=ensure/status.` |
| Process runs but all windows invisible | `MainWindowHandle == 0` counted as "not focusable" → launch fallback on ensure |
| Background service (no window) | Ensure launches. Status returns `"Running: PID=X, (no window)"` |
| `SetForegroundWindow` fails | AttachThreadInput trick is attempted; if still false → soft warning `"Attempted focus on '<title>' — window may not have come to front"` (no ERROR) |
| `Process.Start` fails | `Win32Exception` caught → `[ERROR] Win32Exception: ...` |
| `launch_command` given but process found | Ignored (launch path not taken) |
| Unknown `ambiguous` value | `[ERROR] ArgumentException: 'ambiguous' must be 'first' or 'error'.` |
| Race: window closes between find and focus | `BringWindowToTop` returns false → soft warning |

## Testing

### Unit tests (`WindowsMCP.NET.Tests`)

Deterministic, no real windows needed:

- `Ensure_EmptyName_ReturnsError`
- `Ensure_UnknownAmbiguous_ReturnsError`
- `Status_EmptyName_ReturnsError`
- `App_Description_ListsAllFiveModes` (guards against mode deletion)
- `ProcessWindowMatcher_ProcessNameExact_PrioritizedOverFuzzy`
- `ProcessWindowMatcher_DotExeSuffix_Normalized` (`notepad` ≡ `notepad.exe`)
- `ProcessWindowMatcher_MultipleMatches_OrderedByZOrder`

### Parity tests (`WindowsMCP.NET.ParityTests`)

Real desktop interaction, session host required, `notepad.exe` as target:

- `Ensure_NotRunning_LaunchesApp`
- `Ensure_AlreadyRunning_Focuses` (verifies via `GetForegroundWindow().ProcessId`)
- `Status_NotRunning_ReturnsNotRunning`
- `Status_Running_ReturnsPidAndTitle`
- `Ensure_MultipleMatches_First_FocusesFirst`
- `Ensure_MultipleMatches_Error_ReturnsList`
- `Ensure_LaunchCommandOverride_UsesOverride`

Teardown discipline: `try/finally` with `Process.Kill` to prevent accumulating Notepad windows.

## Documentation

### CLAUDE.md tool table

```markdown
| **App** | Launch, focus, check, or resize windows. Modes: launch, ensure, status, switch, resize |
```

### CLAUDE.md "Key Patterns" addition

```markdown
- **Foreground without screenshots**: Use `App(mode="ensure", name="notepad")`
  to focus an app if running, launch if not — one call instead of screenshot+parse workflow.
```

## YAGNI — Out of Scope

| Feature | Reason deferred |
|---|---|
| Configurable app-alias registry | Wait until recurring apps emerge; `launch_command` suffices for now |
| Z-order via explicit `GetWindow(GW_HWNDPREV)` | `EnumWindows` already returns Z-ordered output |
| `SendInput(0)` pre-focus workaround | AttachThreadInput is sufficient; revisit only if bugs emerge |
| Timeout parameter on ensure | Inherits existing `LaunchApp` wait (3.5s); adjust only on demand |
| Wait-for-window-visible mode | Client can compose `Wait` + `Context` if needed |
| Cycle-through-multiple-windows mode | Feature creep; one call = one focus |

## Code-size estimate

| File | Approx. changed lines |
|---|---|
| `src/WindowsMCP.NET/Tools/AppTools.cs` | +80 |
| `src/WindowsMCP.NET/Services/DesktopService.cs` | +50 |
| `src/WindowsMCP.NET/Services/ProcessWindowMatcher.cs` (new) | +60 |
| `src/WindowsMCP.NET/Models/ProcessSnapshot.cs` (new) | +5 |
| `src/WindowsMCP.NET/Native/User32.cs` | +10 |
| `src/WindowsMCP.NET/Native/Kernel32.cs` | +5 |
| `tests/WindowsMCP.NET.Tests/...` | +100 |
| `tests/WindowsMCP.NET.ParityTests/...` | +150 |
| `CLAUDE.md` | +3 |
| **Total** | **~470 lines (≈250 tests)** |
