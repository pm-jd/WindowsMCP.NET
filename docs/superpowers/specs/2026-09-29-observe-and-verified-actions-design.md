# Observe & Verified Actions — Design Specification

**Date:** 2026-09-29
**Status:** Approved (design), spec under review
**Sub-project:** 1 of the "cheaper, faster computer use" plan (0 spike · **1 observation & verification** · 2 decision service · 3 local step loop · 4 MCS test runner)
**Depends on:** FlaUI.UIA3 5.0.0, existing tool conventions (`ToolHelpers`, `ErrorFlagFilter`, `ToolEnums`)

## Overview

Today an agent sees the desktop through `Snapshot`/`Context`: a depth-5 UIA walk over the **whole desktop** plus a full-resolution PNG, and it acts through numbered labels that point at **cached rectangles**. Nothing checks whether an action had any effect.

This sub-project adds a new read tool, `Observe`, that returns a compact, structured state of the foreground application with **stable element ids**, and extends the action tools so they accept those ids, resolve them against the **live** UI at execution time, prefer UIA patterns over mouse input and **verify** the outcome. `Snapshot`, `Context` and the numbered labels stay unchanged.

The result is useful on its own (fewer tokens and fewer failed steps for Claude) and is the prerequisite for sub-projects 2–4, where a local decision model or a test runner consumes the same state.

## Evidence (spike, 2026-09-28, MCS 1.13.1186 on SWENTW3, simulation config)

| Finding | Consequence for this design |
|---|---|
| Depth-5 desktop walk returns ~26 KB of text, but cuts off MCS panel content (Nosepiece, Focus Axis, View Settings, browser tree) while including Firefox, desktop icons and minimized windows | Walk only the foreground process, no depth limit, filter offscreen/zero-size |
| A single FlaUI `CacheRequest` (Subtree) over the MCS window: ~500 nodes in ~1 s; compact state ≈ 5 KB (~1,300 tokens) | One cached round-trip per window; budget < 1.5 s |
| UIA3 exposes the Nosepiece objectives as `RadioButton '1x'…'20x'`; UIA2 does not | Stay on FlaUI UIA3 |
| `FromPoint` hit-test removes content of hidden docking tabs in the main window, but fails for every element in popups (ToolStrip drop-downs), modal dialogs and docking `TabItem`s | Hit-test only the bottom-most main window; trust windows stacked above it; keep `TabItem`s whose tab container is visible |
| `SelectionItem.Select()` on a docking tab returns success and does nothing | Every pattern action is verified; mouse click is the fallback |
| Unnamed `Edit`s are identified only by their value ("90,000") | Derive a `label` from the preceding `Text` sibling |
| OCR text lines offered as click targets caused ~5 of 12 errors of the best local model; Windows OCR is weak on 8 pt UI text | OCR is out of scope; static texts go into a separate, non-actionable block |
| Many menu items are named by command id (`SaveAs`, `StitchHereWxH`) | Reported as-is; readable names are an MCS-side fix (see Non-Goals) |
| windows-mcp 2026.06.1 captured 4800×2700 and clicked ×2.5 off after an RDP reconnect | Current build is PerMonitorV2 via `app.manifest`; re-verify after updating SWENTW3, re-query screen metrics per call |

## Goals

- `Observe` returns the foreground application's windows, focus, visible actionable elements and static texts in < 1.5 s and < 8 KB for MCS main screens.
- Element ids are stable across `Observe` calls as long as the UI structure is unchanged.
- `Click`, `Type`, `Perform`, `MultiSelect`, `MultiEdit` accept element ids, resolve them live and report an `effect`.
- `Perform` stops on stalls instead of reporting "OK" for steps that changed nothing.
- No behaviour change for existing parameters; Phase-1 parity schema tests stay green apart from the additive changes.

## Non-Goals

- OCR (deferred until an application needs it; would require TFM `net10.0-windows10.0.19041.0`)
- Any decision model / Ollaya integration (sub-project 2)
- Changing `Snapshot`, `Context` or numbered labels
- MCS-side accessibility fixes (`AccessibleName` for custom-drawn controls, readable menu names) — tracked separately in the MCS repository
- Multi-monitor-specific behaviour beyond what `ScreenCaptureService` already does

---

## 1. Tool `Observe`

```
Observe(
  scope: ObserveScope = foreground,   // foreground | process | desktop
  process: string? = null,            // required for scope=process (process name, e.g. "MCS")
  format: OutputFormat = markdown,    // markdown | json
  screenshot: bool = false,
  max_elements: int = 150)
```

`ReadOnly = true`, `Idempotent = true`. `ObserveScope` lives in `ToolEnums.cs` with `[JsonConverter(typeof(SnakeCaseEnumConverter<ObserveScope>))]`.

### Scope

| Scope | Windows collected |
|---|---|
| `foreground` | All visible top-level windows of the foreground window's process, in z-order (menus, drop-downs, modal dialogs, main window) |
| `process` | Same, for the named process (first match; error if none) |
| `desktop` | All visible top-level windows of all processes, in z-order (escape hatch; large) |

### Content

- **windows**: `title`, `process`, `pid`, `foreground`, `modal` (owned window with disabled owner), `rect`
- **focus**: the element with keyboard focus (as element reference, if it is in the collected set)
- **elements** (actionable, visible, capped by `max_elements`, topmost window first, then top-to-bottom/left-to-right):
  - `id`, `type`, `name`, `label` (derived, only when `name` is empty or equals `value`), `panel` (nearest named container ancestor), `window` (only when not the main window)
  - state: `value`, `toggle`, `selected`, `expanded`, `enabled` (only emitted when `false`)
  - `rect` (JSON only)
  - actionable types: Button, SplitButton, CheckBox, RadioButton, ComboBox, Edit, Document, Spinner, Slider, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, DataItem, HeaderItem
- **texts**: visible named `Text` elements (and `Group`/`Header` captions), deduplicated, not actionable — context only
- **signature**: hash over (window handles + titles, and for every emitted element: id, name, value, toggle, selected, expanded, enabled)
- **truncated**: `true` when `max_elements` or the time budget cut the result
- **timings**: `walk_ms`, `hit_ms`, `total_ms`

### Markdown format (default)

```
## MCS - service  (MCS, pid 26648) · foreground
focus: e9K1

e7Q2  RadioButton '20x'            @Nosepiece
e9K1  Edit 'Focus Axis'            value=90,000
eA3F  Button 'SnapImage'           @Toolbar
eB0C  Button 'StartLiveVideo'      @Toolbar  disabled

texts: Nosepiece · Focus Axis · Illumination · View Settings
signature 5f1c9a0e · 118 elements · 1,21 s
```

When a drop-down or dialog is open, its window block comes first with its own heading.

### JSON format

`ToolHelpers.JsonResult` envelope with `structuredContent`; shape documented inline in the `format` parameter description (project convention):
`{windows:[…], focus:"e9K1", elements:[{id,type,name,label?,panel?,window?,value?,toggle?,selected?,expanded?,enabled?,rect:[x,y,w,h]}], texts:[…], signature, truncated, timings:{walk_ms,hit_ms,total_ms}}`

### Screenshot

`screenshot=true` appends one image block: the union of the collected windows' rects (clipped to the screen), JPEG quality 80, longest edge ≤ 1568 px. No annotation (ids are in the text).

### Collection (`ObservationService`)

1. Resolve the target windows (EnumWindows z-order, visible, owned by the target pid(s)).
2. Per window: one `CacheRequest` with `TreeScope.Subtree`, tree filter `IsControlElement = true`, properties ControlType, Name, AutomationId, IsEnabled, HasKeyboardFocus, IsOffscreen, BoundingRectangle, RuntimeId, Invoke availability, Value, ToggleState, IsSelected, ExpandCollapseState. Walk `CachedChildren`; drop offscreen and ≤ 1 px nodes (and their subtrees).
3. Visibility: hit-test (`FromPoint` at the element centre, match by RuntimeId against the node, its descendants and ancestors) **only** for nodes of the bottom-most main window; nodes of windows above it are visible; a `TabItem` is visible when its `Tab` parent is.
4. Build the state (pure function over the node list — see §6): actionable elements, labels, panels, texts, ids, signature.
5. Budget: `CancellationToken` plus a hard 5 s walk budget; on overrun return what was collected with `truncated=true`.

Collections are serialized inside the service (one at a time, like `UiTreeService`'s lock); the threading model of the existing `UiAutomationService` is reused unchanged — verify during implementation whether a dedicated STA thread is needed for the cached walk.

## 2. Stable element ids

- **Locator** per element: process name, top-level window class name, and the ancestor path from the window root, one step per level: `(ControlType, AutomationId if non-empty else Name, index among siblings with the same ControlType and key)`.
- **id** = `"e"` + first 4 characters of a base32 SHA-256 over the locator (collision within one observation → extend to 6 characters for the colliding ids).
- **Store**: `ObservationStore` (singleton) keeps id → locator for the last 8 observations (LRU). Ids from older observations fail with the stale-id error below.
- **Resolution** at action time (fresh UIA calls, no cache):
  1. Walk the locator path from the live window (FindFirstChild per step with type + key + index).
  2. Fallback: within the same top-level window, a unique descendant with the same ControlType and key.
  3. None or ambiguous → `[ERROR] ElementNotFound: element e7Q2 no longer present — call Observe`.

RuntimeIds are not used for identity: WinForms proxies regenerate them.

## 3. Actions with element ids

New optional parameter `element: string?` on `Click`, `Type` and on every `Perform` step; `MultiSelect` and `MultiEdit` accept element ids wherever they accept labels today (exact parameter shape follows their current signatures). Precedence: `element` > `label` > `loc`. New parameter `method: ActionMethod = auto` (`auto | pattern | mouse`).

### Click (`method=auto`)

| Element type | Pattern tried first |
|---|---|
| Button, SplitButton, Hyperlink, MenuItem without children | Invoke |
| MenuItem with children, ComboBox | ExpandCollapse (Expand; Collapse when already expanded) |
| CheckBox | Toggle |
| RadioButton, TabItem, ListItem, TreeItem, DataItem | SelectionItem.Select |

Then verify (§4). The mouse fallback (click the centre of the element's **current** `BoundingRectangle` via the existing `InputFactory` mouse path, then verify again) is used when the pattern is unsupported or its call fails, and — because `Select` is idempotent — when a `SelectionItem` call reports success but nothing changed (the docking-tab case from the spike). After a successful `Invoke`, `Toggle` or `ExpandCollapse` with an unchanged signature the result is `effect: unchanged` and **no** second action is performed (a mouse click could invoke a button twice or toggle a checkbox back); the caller can retry with `method=mouse`. `method=pattern` never falls back; `method=mouse` skips the pattern. `button`/`clicks` other than a single left click always use the mouse path.

### Type

1. If `ValuePattern` is supported and not read-only: `SetValue(clear ? text : current + text)`; `press_enter` sends Enter afterwards via keyboard. The target is focused first (`SetFocus`, mouse click on failure) so Enter reaches it.
2. Otherwise: focus the element (`SetFocus`, mouse click on failure), then the existing keyboard path (`clear` → Ctrl+A, Delete).
3. Read back `ValuePattern.Value` (when available): equal after trimming → `value_verified`, else `value_mismatch` (reported, not thrown). `\n`/`\t` handling stays as today for the keyboard path.

### Result text

Existing result strings are kept and extended: `Clicked e7Q2 (RadioButton '20x') via SelectionItem — effect: changed`.

## 4. Verification and stall detection

- **Signature**: after resolving the element and before acting, compute the scope signature (same collection as `Observe` without hit-testing and without formatting); after acting wait `settle_ms` (default 300, max 2000) and compute it again.
- **effect**: `changed` (signatures differ), `unchanged`, `value_verified`, `value_mismatch`.
- **verify** parameter: default `true` when `element` is used, `false` otherwise (label/loc behaviour unchanged).
- **Perform**: each step re-resolves its `element`; new options `verify` (default `true` for element steps) and `stop_on_stall` (default `true`): three consecutive element steps with `effect=unchanged` stop the run with `Stopped after step N: no visible change for 3 steps (stall).`. Step result lines gain the effect. Progress notifications unchanged.
- A step whose pattern "succeeded" but whose mouse fallback also produced `unchanged` is reported as `OK — effect: unchanged` (not an error); only stall detection turns it into a stop.

## 5. Errors, limits, security

- All tools keep the `[ERROR] Type: message` convention; `ErrorFlagFilter` sets `IsError`.
- New error cases: `ElementNotFound` (stale/removed id), `ProcessNotFound` (scope=process), `ObservationTimeout` is not an error — it yields `truncated=true`.
- `max_elements` clamp 10…500; `settle_ms` clamp 0…2000.
- `Observe` is read-only and exposes only what `Snapshot` already exposes; no new security surface.

## 6. Code structure

```
src/WindowsMCP.NET/
  Models/
    ObservedNode.cs            — raw cached node (type, name, aid, rect, states, parent, depth, window index)
    Observation.cs             — windows, elements, texts, focus, signature, truncated, timings
    ElementLocator.cs          — locator path + id hashing
  Services/
    ObservationService.cs      — UIA collection (CacheRequest, hit-test) → List<ObservedNode>; STA thread
    ObservationBuilder.cs      — pure: nodes → Observation (filter, visibility rules, label/panel, texts, ids, signature)
    ObservationStore.cs        — id → locator LRU; live resolution
    ActionExecutor.cs          — pattern-first actions, mouse fallback, ValuePattern read-back, effect
  Tools/
    ObserveTools.cs            — [McpServerTool] Observe
    InputTools.cs / PerformTools.cs / MultiTools — new `element`, `method`, `verify`, `settle_ms`, `stop_on_stall`
    ToolEnums.cs               — ObserveScope, ActionMethod
```

`ObservationBuilder` has no UIA dependency so it is unit-testable with synthetic node lists. `ActionExecutor` depends on an `IElementActions` seam (patterns, focus, bounding rect) so the fallback/verification logic is testable without a desktop.

## 7. Testing

**Unit tests** (`WindowsMCP.NET.Tests`, no desktop):
- `ObservationBuilder`: offscreen/zero-size filtering, visibility rules (main window hit-test result honoured, popups trusted, TabItem rule), actionable vs text split, label derivation, panel resolution, ordering, `max_elements` truncation, deterministic ids and collision extension, signature changes on value/toggle/selected/enabled and not on rect-only changes.
- `ElementLocator`: same structure → same id; sibling index changes → different id.
- `ActionExecutor` with a fake `IElementActions`: pattern success + changed; pattern "success" + unchanged → mouse fallback; `method=pattern` no fallback; ValuePattern read-back mismatch; stall counting in `Perform`.
- Enum converter test already covers new enums (attribute check).

**Parity tests** (`WindowsMCP.NET.ParityTests`):
- Phase 1 schema: `Observe` present with expected parameters; additive parameters on Click/Type/Perform/MultiSelect/MultiEdit (baseline updated deliberately).
- Phase 2 functional (desktop): Notepad — `Observe` finds the document `Edit`; `Type` via element id → `value_verified`; `Click` on the Format menu via id → `changed`; stale id after closing Notepad → `ElementNotFound`.

**Acceptance on MCS** (manual, SWENTW3, simulation config `E:\ollaya\mcs-sim-config`):
- Update SWENTW3 to the current windows-mcp build; confirm screenshots are 1920×1080 after an RDP reconnect and clicks land correctly.
- Re-capture the 30 spike states with `Observe`: Nosepiece/Focus Axis/View Settings/browser controls present; menu drop-down and dialog items listed first; main screen < 8 KB markdown; `Observe` < 1.5 s.
- `Click` on the Camera docking tab via id → `changed` (mouse fallback exercised).

## 8. Rollout

1. Implement behind no flag (additive tools/parameters).
2. Update `CLAUDE.md` (tool table: 21 tools; Key Patterns: "Observe → act by element id").
3. Release via the normal CalVer pipeline; update SWENTW3 and the in-house test machines through the auto-updater.
4. Hand the observation format to sub-project 2 (decision service) as its input contract.

## Open Questions

None blocking. To revisit after acceptance: whether `Context` should gain a `module=observe` that embeds the compact state, and whether OCR is needed for any non-MCS application.
