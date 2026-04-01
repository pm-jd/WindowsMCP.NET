# Perform & Context Tools — Design

**Goal:** Reduce MCP roundtrips and give Claude Code better situational awareness for UI automation workflows.

**Architecture:** Two new tools — `Perform` for batched UI action chains with automatic feedback, `Context` for on-demand system state. Both reuse existing services (InputTools, DesktopService, ScreenCaptureService, UiTreeService).

**Tech Stack:** C# / .NET 9, ModelContextProtocol SDK v1.2.0, FlaUI, xUnit

---

## Tool 1: Perform — Batched UI Actions

### Problem

A typical UI workflow (Snapshot → Click → Type → Snapshot) requires 4+ MCP roundtrips. Each roundtrip adds latency, especially over HTTP to remote machines.

### Solution

A single `Perform` tool that accepts an array of steps and executes them sequentially, returning step-by-step results with an optional screenshot.

### Schema

```json
{
  "tool": "Perform",
  "steps": [
    { "action": "click", "label": "3" },
    { "action": "type", "text": "admin", "label": "5" },
    { "action": "type", "text": "pass123", "label": "7", "press_enter": true },
    { "action": "shortcut", "shortcut": "ctrl+s" },
    { "action": "wait", "duration": 2 },
    { "action": "scroll", "direction": "down", "wheel_times": 3 },
    { "action": "move", "label": "9", "drag": true }
  ],
  "stop_on_error": true,
  "snapshot_after": true,
  "delay_between_ms": 100
}
```

### Supported Actions

| Action     | Key Parameters                                  |
|------------|------------------------------------------------|
| `click`    | `label`, `loc`, `button`, `clicks`              |
| `type`     | `text`, `label`, `loc`, `clear`, `press_enter`  |
| `shortcut` | `shortcut`                                      |
| `scroll`   | `direction`, `wheel_times`, `label`, `loc`      |
| `move`     | `label`, `loc`, `drag`                          |
| `wait`     | `duration`                                      |

### Parameters

| Parameter          | Type     | Default | Description                                    |
|-------------------|----------|---------|------------------------------------------------|
| `steps`           | array    | required| Array of action objects                         |
| `stop_on_error`   | bool     | true    | Stop chain on first error                       |
| `snapshot_after`   | bool     | true    | Attach screenshot after execution               |
| `delay_between_ms` | int     | 100     | Milliseconds to wait between steps              |

### Return Format

Text content with step-by-step results, plus optional ImageContent (PNG screenshot):

```
Step 1: OK — Clicked label 3 ("Save" Button) at [450, 320]
Step 2: OK — Typed "admin" at label 5 ("Username")
Step 3: FAIL — Label 7 not found in UI tree
Stopped after step 3 (stop_on_error=true). 2/3 succeeded.
```

### Implementation Notes

- New class `PerformTools.cs` in `Tools/`
- Steps parsed as `JsonElement[]` — each step dispatched to existing Input tool logic
- Reuses `UiTreeService` for label resolution, `ScreenCaptureService` for snapshot
- Returns `IList<Content>` (TextContent + optional ImageContent)
- Error handling: each step wrapped in try-catch, failures reported inline

---

## Tool 2: Context — System State on Demand

### Problem

Claude Code operates blind — it doesn't know what's on screen, which window is active, or what's in the clipboard without making separate tool calls first. This leads to guesswork and wasted roundtrips.

### Solution

A single `Context` tool that returns a configurable snapshot of the current system state.

### Schema

```json
{
  "tool": "Context",
  "include": ["window", "screen", "ui_tree", "clipboard", "processes"]
}
```

### Include Modules

| Module      | What it returns                                        | Cost     |
|-------------|-------------------------------------------------------|----------|
| `window`    | Active window: title, process name, PID, bounds        | ~5ms     |
| `screen`    | Screenshot of active monitor                           | ~100ms   |
| `ui_tree`   | UI tree with numbered labels (like Snapshot)            | ~300ms   |
| `clipboard` | Current clipboard text (truncated to 1000 chars)       | ~5ms     |
| `processes` | Top 10 processes by memory, with window titles          | ~20ms    |

### Parameters

| Parameter | Type     | Default              | Description                          |
|-----------|----------|----------------------|--------------------------------------|
| `include` | string[] | ["window", "screen"] | Which modules to include in response |

### Return Format

Structured text content with sections, plus optional ImageContent:

```
-- Active Window --
Title:    MCS - Hauptmenue
Process:  MCS.exe (PID 4820)
Bounds:   [0, 0, 1920, 1080] (maximized)

-- Clipboard --
(empty)

-- Top Processes --
 PID   Memory    Window Title
 4820  312 MB    MCS - Hauptmenue
 9216  189 MB    Microsoft Outlook

-- UI Tree (14 interactive elements) --
[1] Button "Datei" (32, 8)
[2] Button "Bearbeiten" (98, 8)
[3] TreeItem "Auftraege" (45, 120)
...

[Screenshot attached]
```

### Implementation Notes

- New class `ContextTools.cs` in `Tools/`
- Composes existing services: `DesktopService`, `ScreenCaptureService`, `UiTreeService`
- Each module is independently try-catch wrapped — a failed module doesn't block others
- Returns `IList<Content>` (TextContent + optional ImageContent)

---

## Workflow Comparison

### Before (5 roundtrips)

```
1. Snapshot          → see UI tree
2. Click label 3     → click button
3. Type "test"       → enter text
4. Shortcut ctrl+s   → save
5. Snapshot          → verify result
```

### After (2 roundtrips)

```
1. Context(window, ui_tree)                                → see state
2. Perform(click 3, type "test", shortcut ctrl+s)          → do all + screenshot
```

**60% fewer roundtrips** for a typical UI automation workflow.

---

## Error Handling

Both tools follow the `[ERROR]` prefix pattern established in FileSystemTools:
- Each operation is wrapped in try-catch
- Failures return `[ERROR] ExceptionType: message` inline
- No exceptions propagate to the MCP SDK (avoiding generic error swallowing)

---

## Approved

Design approved 2026-04-01.
