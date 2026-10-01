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

- **windows**: `title`, `process`, `pid`, `foreground`, `modal` (owned window with disabled owner), `unreadable`, `rect`
  - **An observation is never silently empty.** A window whose UIA root or subtree cannot be read (exception or timeout — typically an earlier `Invoke` whose handler opened a modal dialog is still pending and the application's UI thread answers nobody) is **not** skipped: it is reported with `unreadable = true`, its title, class and rectangle taken from Win32 (`GetWindowText`, `GetWindowRect`) and no elements. After the first unreadable window of a process, the remaining windows of the same process are marked unreadable without another UIA attempt in that call (one timeout per process, not per window). A window that did not answer but is no longer visible (a menu or tooltip that closed between the enumeration and the read) is simply left out and says nothing about its process. The failure is logged at Warning with the exception type.
- **focus**: the element with keyboard focus (as element reference, if it is in the collected set)
- **elements** (actionable, visible, capped by `max_elements`, topmost window first, then top-to-bottom/left-to-right):
  - `id`, `type`, `name`, `label` (derived, only when `name` is empty or equals `value`), `panel` (nearest named container ancestor), `window` (only when not the main window)
  - state: `value`, `toggle`, `selected`, `expanded`, `enabled` (only emitted when `false`)
  - `rect` (JSON only)
  - actionable types: Button, SplitButton, CheckBox, RadioButton, ComboBox, Edit, Document, Spinner, Slider, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, DataItem, HeaderItem
  - an element with neither a name nor a value is skipped — **except input types** (Edit, ComboBox, Spinner, Slider, Document), which are always emitted: an empty unnamed field is exactly what gets typed into (`label` is derived as usual; markdown renders the empty name as `''`)
  - **password fields** (UIA `IsPassword`): the `value` is never collected, so it is neither emitted nor part of the signature
- **texts**: visible named `Text` elements (and `Group`/`Header` captions), deduplicated, not actionable — context only; at most 80 entries
- **signature**: hash over (window handles + titles + the unreadable flag, and for every emitted element: id, name, value, toggle, selected, expanded, enabled) — always over the **full** value, independent of the display limits below. Unreadable windows are part of it: readable → unreadable is a change, and an observation whose windows do not answer never has the signature of an empty one
- **truncated**: `true` when `max_elements`, the 80-texts cap or the time budget cut the result
- **omitted**: the number of actionable elements that were found but not listed because of `max_elements` (0 when the cut had another reason). The markdown footer then reads `… · truncated: 132 more elements (max_elements=150)` instead of the bare `… · truncated`, so the caller knows how much is missing and which parameter to raise (a real MCS program screen has 282 actionable elements)
- **timings**: `walk_ms`, `hit_ms`, `total_ms`

**Display limits** (both formats): `value` at most 200 characters; `name`, `label`, `panel` and each text at most 120. A longer string keeps its first N characters followed by `…(+n chars)` (n = characters left out). In **markdown**, carriage return, line feed and tab inside names, labels, panels, values, texts and window titles are rendered as the two-character escapes `\r`, `\n`, `\t` (any other control or line-separator character as `\uXXXX`) — one line per element is an invariant of the format. **JSON** keeps the real characters (JSON escaping handles them) and applies the same truncation.

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

When a drop-down or dialog is open, its window block comes first with its own heading. A window without any emitted element keeps its heading — except when it is untitled, not the foreground window and not unreadable (the untitled helper windows an open menu brings along are noise; JSON still lists every window). The `focus:` line follows the first heading that is rendered.

A window that did not answer UI Automation says so in its heading, and one line before the footer says what that means and what still works:

```
## Öffnen  (MCS, pid 10976) · foreground · modal · unreadable

## MCS - service  (MCS, pid 10976) · unreadable

2 window(s) did not answer UI Automation — an earlier action may still be running (for example a modal dialog opened by Invoke). Use Screenshot and keyboard/coordinates until it is closed.
signature 3c1f07aa · 0 elements · 2040 ms
```

### JSON format

`ToolHelpers.JsonResult` envelope with `structuredContent`; shape documented inline in the `format` parameter description (project convention):
`{windows:[{title,process,pid,foreground,modal,unreadable?,rect:[x,y,w,h]}], focus:"e9K1", elements:[{id,type,name,label?,panel?,window?,value?,toggle?,selected?,expanded?,enabled?,rect:[x,y,w,h]}], texts:[…], signature, truncated, omitted, timings:{walk_ms,hit_ms,total_ms}}` (`unreadable` is only emitted when `true`; `omitted` is always emitted, `0` when nothing was cut by `max_elements`)

### Screenshot

`screenshot=true` appends one image block: the union of the collected windows' rects (clipped to the screen), JPEG quality 80, longest edge ≤ 1568 px. No annotation (ids are in the text). The region depends on the windows' rectangles only — unreadable windows carry their Win32 rectangle — so the image is also returned when no window answered UI Automation, which is exactly when the picture is all the caller can get.

### Collection (`ObservationService`)

1. Resolve the target windows (EnumWindows z-order, visible, owned by the target pid(s)).
2. Per window: one `CacheRequest` with `TreeScope.Subtree`, tree filter `IsControlElement = true`, properties ControlType, Name, AutomationId, IsEnabled, HasKeyboardFocus, IsOffscreen, BoundingRectangle, IsPassword, RuntimeId, Invoke availability, Value, ToggleState, IsSelected, ExpandCollapseState. Walk `CachedChildren`; drop offscreen and ≤ 1 px nodes (and their subtrees). A window for which this request fails is recorded as unreadable (see Content) instead of being skipped.
3. Visibility: hit-test **only** for nodes of the bottom-most main window; nodes of windows above it are visible; a `TabItem` is visible when its `Tab` parent is. The hit-test has two stages:
   - **Win32 pre-check**: the top-level window at the node's centre (`GetAncestor(WindowFromPoint(centre), GA_ROOT)` — the same helper the click guard of §3 uses, `WindowHit`) must be the node's own top-level window; otherwise the node is not visible, and UIA is not asked. This removes the elements that are **drawn in another top-level window but listed under the main window too**: WinForms exposes the items of an open drop-down as children of their menu item, and the controls of an owned modal dialog under the main window, so each appeared twice — once in the popup's/dialog's own window block and once (with a different id) under the main window. An action on the main-window copy found its click point "covered" and fell back to `Invoke`, the call that blocks when its handler opens a dialog. Only the copy in the popup's own window block stays (those windows are not hit-tested).
   - **UIA**: `FromPoint` at the element centre, match by RuntimeId against the node, its descendants and ancestors.
4. Build the state (pure function over the node list — see §6): actionable elements, labels, panels, texts, ids, signature.
5. Budget: `CancellationToken` plus a hard 5 s walk budget; on overrun return what was collected with `truncated=true`.

Collections are serialized inside the service (one at a time, like `UiTreeService`'s lock); the threading model of the existing `UiAutomationService` is reused unchanged — verify during implementation whether a dedicated STA thread is needed for the cached walk.

## 2. Stable element ids

- **Locator** per element: process name, top-level window class name, and the ancestor path from the window root, one step per level: `(ControlType, AutomationId if non-empty else Name, index among siblings with the same ControlType and key)`.
- **id** = `"e"` + first 4 characters of a base32 SHA-256 over the locator (collision of different locators within one observation → extend to 6 characters for the colliding ids). Elements whose locators are **identical** within one observation — the same path in two windows of the same process name and class (two Notepad windows, two MCS main windows) — cannot be separated by any locator hash; their ids are hashed over `locator + "|hwnd:" + window handle` (6 characters) so they differ.
- **Window affinity**: every id remembers the top-level window (handle) it was observed in and whether that window was **transient** — not the bottom-most (main) window of its process in that observation, i.e. a popup, menu or dialog. (A process with several top-level document windows, such as Windows 11 Notepad, has one bottom-most window; the others count as transient too, which only costs them the restart fallback below.) The handle is not part of the locator and (except for the identical-locator case above) not of the id.
- **Store**: `ObservationStore` (singleton) keeps id → (locator, window handle, transient) for the last 8 observations (LRU). Ids from older observations fail with the stale-id error below.
- **Resolution** at action time (fresh UIA calls, no cache):
  1. Candidate windows: when the remembered window is still a visible top-level window of the same process name and class, resolve **only** in that window. When it is gone, a transient id is stale (a remembered dialog button never resolves in a later dialog of the same class); a main-window id falls back to every visible window of that process name and class in z-order (so ids survive an application restart).
  2. Walk the locator path from the live window (FindFirstChild per step with type + key + index).
  3. Fallback: within the candidate window(s), a unique descendant with the same ControlType and key.
  4. None or ambiguous → `[ERROR] ElementNotFound: element e7Q2 no longer present — call Observe`.
- Resolution returns the element together with the window it was found in; that window's process id scopes verification (§4) and its enabled state is checked before acting (§3).

RuntimeIds are not used for identity: WinForms proxies regenerate them. Known limitation: with several same-class windows of one process name and the remembered window gone, a main-window id resolves in the topmost match — agents should act on their latest `Observe`.

## 3. Actions with element ids

New optional parameter `element: string?` on `Click`, `Type` and on the **click and type steps** of `Perform` (a `scroll`, `move`, `shortcut` or `wait` step that carries `element` fails with `ArgumentException: 'element' is only supported on click and type steps` instead of silently running without it; an `element` that is not a string fails as well instead of falling back to `label`/`loc`); `MultiSelect` and `MultiEdit` accept element ids wherever they accept labels today (exact parameter shape follows their current signatures). Precedence: `element` > `label` > `loc`. New parameter `method: ActionMethod = auto` (`auto | pattern | mouse`).

### Guards — input never reaches anything but the resolved target

Two rules hold on every element path: **input is never sent to anything but the resolved target, and no action is performed twice.** When in doubt the action is refused with an actionable `[ERROR]` and nothing is done. Checked before anything is sent:

- **Modal dialog**: when the top-level window the element was resolved in (§2) is disabled — it owns an open modal dialog — resolution fails with `InvalidOperationException: element e7Q2 is in a window blocked by a modal dialog — call Observe and handle the dialog first`. UIA patterns (`Invoke`, `SetValue`) would otherwise act behind the dialog.
- **Disabled target**: `Click` and `Type` refuse an element that reports `IsEnabled = false` with `InvalidOperationException: Button is disabled — nothing was done` (an element that does not report the property counts as enabled). The same check guards the mouse-only element paths (non-left/multi clicks, `MultiSelect`).
- **Safe click point**: before **any** mouse input on the element path — mouse-first clicks, mouse method and fallback, click-to-focus, non-left/multi clicks, `MultiSelect` — one check (`ActionGuards.ClickPoint`) decides whether and where the mouse may click:
  1. **Interactive desktop**: the process must be able to inject input, i.e. there is a foreground window (`GetForegroundWindow() != 0`). A disconnected or non-rendered session has none; only UIA patterns work there. Otherwise `InvalidOperationException: no interactive desktop (session disconnected or not rendered) — nothing was clicked`.
  2. **Visible area**: the element's current rectangle has an area; otherwise `the element has no visible area — nothing was clicked`.
  3. **Own window** (`IActionTarget.OwnsPoint`; the window at the point comes from `Native/WindowHit`, the same helper Observe's hit-test uses): the window at the click point (centre of that rectangle) is the element's; otherwise `InvalidOperationException: the element is covered by another window — nothing was clicked`. Two conditions, both plain Win32 hit-tests (UIA's `FromPoint` is deliberately not used: it is unreliable in popups, menus and dialogs — see Evidence —, costs a cross-process call and cannot answer while the application is busy):
     - **the top-level window**: `GetAncestor(WindowFromPoint(point), GA_ROOT)` is the window the element was resolved in (§2). This refuses a point covered by another application **and** one covered by a popup, menu or dialog of the same application.
     - **the control**: the window `WindowFromPoint` returns — the deepest child window at the point — is the element's own *native window* or a child window of it (`IsChild`). The native window is the nearest element with a non-zero `NativeWindowHandle` walking up from the target: the target itself for a control with its own HWND (dialog button, edit field), the ToolStrip, menu popup, tab strip or top-level window for windowless elements (toolbar buttons, menu items, docking tabs, title-bar buttons). This refuses a control whose centre is overlapped by a sibling control or clipped by a scroll container — since the mouse is the default for Button/SplitButton/Hyperlink/plain MenuItem, the click would otherwise land on the other control. For an element whose native window is the top-level window itself the condition adds nothing (every window in it is a child of it). The native window is determined once per target, lazily, and guarded: when it cannot be determined — the walk fails, **or the window it finds does not contain the click point at all** — only the top-level condition applies. The second case was measured on the Windows 11 task bar (XAML island): its buttons hang under a `Windows.UI.Input.InputSite` window lying elsewhere, while the window at their centre is the task list; without that exception 52 of 64 visible task-bar buttons were refused.

  `MultiSelect` checks **all** targets before it presses Ctrl, and each one again right before its click (a failure then stops the run, releases Ctrl and lists what was already clicked).
- **Keyboard focus**: keys are only sent once the target — or one of its descendants; a ComboBox's inner Edit counts — is confirmed to hold the keyboard focus (see Type).
- **Live reads**: a read that fails *before* the action (the element vanished) ends in `ElementNotFound` with nothing done. No read *after* an action was sent (read-back, rectangle, focus, hit-test, signature) can turn the executed action into an error.

### Click (`method=auto`)

| Element type | Tried first | Otherwise |
|---|---|---|
| Button, SplitButton, Hyperlink, MenuItem without children | **mouse click**, when the click point is safe (Guards) | Invoke |
| MenuItem with children, ComboBox | ExpandCollapse (Expand; Collapse when already expanded) | mouse click when the pattern is not supported |
| CheckBox | Toggle | mouse click when the pattern is not supported |
| RadioButton, TabItem, ListItem, TreeItem, DataItem | SelectionItem.Select | mouse click when the pattern is not supported or the select did not take (see below) |
| any other type | mouse click | — |

Then verify (§4).

**Why Invoke-type controls are clicked with the mouse first** (acceptance on MCS, 2026-10-01): an `Invoke` whose handler opens a modal dialog (WinForms `ShowDialog()`) does not return until the dialog is closed. While that call is pending, **every** UI Automation request to that application times out — for this server and for any other UIA client — so the dialog that was just opened can neither be observed nor operated by element id (`Observe` came back empty after 4 s, a `FindAll` took 66 s). Waiting longer does not help; the call only returns when the dialog is gone. A mouse click has no such after-effect, so for the control types whose click pattern is `Invoke` the mouse is used whenever it can click safely (interactive desktop, visible area, own window — see Guards), and `Invoke` only when it cannot: in a disconnected or non-rendered session, for an element without visible area, or when the click point is covered. A mouse click that changed nothing is reported as `via mouse — effect: unchanged` and is **not** followed by an `Invoke` (the button could be pressed twice). When there is no safe click point and the element does not support `Invoke` either, the call fails with the reason the mouse cannot be used. Toggle, SelectionItem and ExpandCollapse stay pattern-first.

For the pattern-first types, the mouse click (the centre of the element's **current** `BoundingRectangle` via the existing `InputFactory` mouse path, then verify again) is used when the element does **not support** the pattern, and — because `Select` is idempotent — when a `SelectionItem` select did not take (the docking-tab case from the spike: `Select()` returns and switches nothing).

**A selection is verified by the target itself.** Whether a select took cannot be read from the signature: in the re-test a docking tab's `Select` did nothing, something else changed meanwhile, and the result was `via SelectionItem — effect: changed` with the tab never switched. So after a dispatched `Select` that has **returned**: settle, then read the target's own SelectionItem `IsSelected` (`IActionTarget.IsSelected`; `null` when the element does not report it or the read fails — the read can never turn the sent Select into an error), then decide:

| `IsSelected` | What happens | Effect |
|---|---|---|
| `true` | no mouse click | from the signature (`changed`/`unchanged`) |
| `false` | the select did not take: one mouse click when there is a safe click point (Guards) — idempotent for tab, radio and list items | from the signature after the click, `via mouse`; without a safe click point the select stands: `via SelectionItem — effect: unchanged`, whatever else the signature picked up |
| `null` | the previous rule: mouse click when the signature is unchanged (and a safe click point exists) | from the signature |

A select that has **not returned** (`StillRunning`) is never followed by a click and its `IsSelected` is not read (the application is busy). `method=pattern` never clicks; a target that says `false` is reported as `unchanged` there too. The click on `false` does not depend on `verify` (with `verify=false` the effect is `not_verified`).

A pattern call that **throws or does not return within 2 s is treated as attempted**: the action may have run (WinForms `Invoke` on a button that opens a modal dialog blocks until the dialog is closed), so it is verified like any other call and never repeated with the mouse. After an attempted `Invoke`, `Toggle` or `ExpandCollapse` with an unchanged signature the result is `effect: unchanged` and **no** second action is performed (a mouse click could invoke a button twice or toggle a checkbox back); the caller can retry with `method=mouse`.

A call that **has not returned** when the 2 s limit runs out is told apart from one that returned or threw (`PatternCall.Attempt` → `NotSupported` / `Returned` / `StillRunning`, carried in `ActionOutcome.CallPending`) and is visible in the result of `Click`, `Type`, `Perform` and `MultiEdit` (`MultiSelect` only uses the mouse): the effect is followed by ` — the call has not returned after 2 s: the application may be showing a modal dialog and cannot be observed until it is closed (Screenshot and keyboard still work)`. There is no retry and no mouse click afterwards — not even the `SelectionItem` retry, because the select is still executing. `method=pattern` never uses the mouse (error only when the element has no usable pattern) — the way to force `Invoke` on a button; `method=mouse` skips the pattern. `button`/`clicks` other than a single left click always use the mouse path.

### Type

1. If `ValuePattern` is supported and not read-only: `SetValue(clear ? text : current + text)`. As with Click, a `SetValue` that throws or does not return within 2 s counts as attempted — the read-back decides, the keyboard path is not used on top. `press_enter` sends Enter afterwards via keyboard: the target is focused first (`SetFocus`; one mouse click when that does not bring the focus), and when the focus cannot be confirmed the call fails with `value was set, but the element could not be focused — Enter was not sent`.
2. Otherwise: focus the element (`SetFocus`; one mouse click when that does not bring the focus — subject to the covered-click-point guard), **confirm that it holds the keyboard focus**, then the existing keyboard path (`clear` → Ctrl+A, Delete). Without confirmed focus nothing is typed: `could not give keyboard focus to the element — nothing was typed`.
3. Read back `ValuePattern.Value` (when available): equal after trimming → `value_verified`, else `value_mismatch` (reported, not thrown). `\n`/`\t` handling stays as today for the keyboard path.

**`clear`** — with `element`, `clear=false` (the default) **appends** the text to the field's current value; `clear=true` sets the field to exactly the text. The default is unchanged; the parameter descriptions of `Type` and of `Perform` type steps say so plainly, and the result tells what the field shows (below).

### Result text

Existing result strings are kept and extended: `Clicked e7Q2 (RadioButton '20x') via SelectionItem — effect: changed`. A pattern call that has not returned adds its note after the effect (see Click): `Clicked eA3F (Button 'Open') via Invoke — effect: changed — the call has not returned after 2 s: …`.

`Type` tells what the field shows whenever that is not simply the text that was passed (acceptance: `0,0000` appended to `0,0000` gave `0,00000,0000` and a result that said neither):

- `clear=false` on a field that already had a value starts with `Appended` instead of `Typed` and names the new content: `Appended 6 chars to e9K1 (Edit 'Focus Axis') — now '0,00000,0000' via ValuePattern — effect: value_verified` (`— now '…'` only when the value could be read back).
- `value_mismatch` is followed by the read-back and the expectation: `… — effect: value_mismatch (field shows '0,0000', expected '12,5')`. `MultiEdit` appends the same to the field's entry.
- Both values are shortened to 60 characters and escaped like the Observe markdown (`…(+n chars)`, `\r`/`\n`/`\t`).
- Never for a password field: its value is not read, so the result is the plain `Typed n chars into … — effect: not_verified`.

## 4. Verification and stall detection

- **Signature**: after resolving the element and before acting, compute the signature of **the process that owns the window the element was resolved in** (its pid — all visible top-level windows of exactly that process instance; same collection as `Observe` without hit-testing and without formatting; it is *not* the scope of the last `Observe`: pattern actions do not bring the application to the foreground, and with two instances of one application only the instance acted on may be watched). After acting wait `settle_ms` (default 300, max 2000) and compute it again. A failing signature walk after the action never fails the action (it counts as a constant).
- `Type` judges by the value read-back and needs the signature only as a fallback: the before-signature is computed only when the value is not readable up front (no ValuePattern) or `press_enter` follows (Enter may take the element away). A field that was readable before, is not readable afterwards and has no baseline reports `not_verified`.
- `Type` into a **password field** (UIA `IsPassword`, `IActionTarget.IsPassword`, false when it cannot be read) always reports `not_verified`: its value is never read back and is deliberately not part of the signature, so the signature would say `unchanged` after a successful entry — never `changed`/`unchanged` from the signature there, and no signature walk at all.
- **effect**: `changed` (signatures differ), `unchanged`, `value_verified`, `value_mismatch`, `not_verified` (verification off, a password field, or nothing to compare against).
- **verify** parameter: default `true` when `element` is used, `false` otherwise (label/loc behaviour unchanged).
- **Perform**: each click and type step re-resolves its `element`; new options `verify` (default `true` for element steps) and `stop_on_stall` (default `true`): three consecutive element steps with `effect=unchanged` stop the run with `Stopped after step N: no visible change for 3 steps (stall).`. A `not_verified` step says nothing either way — it neither counts towards the stall nor resets it — so entries into password fields never stall a chain. Step result lines gain the effect. Progress notifications unchanged.
- A step whose pattern "succeeded" but whose mouse fallback also produced `unchanged` is reported as `OK — effect: unchanged` (not an error); only stall detection turns it into a stop.

## 5. Errors, limits, security

- All tools keep the `[ERROR] Type: message` convention; `ErrorFlagFilter` sets `IsError`.
- New error cases: `ElementNotFound` (stale/removed id), `ProcessNotFound` (scope=process), `ObservationTimeout` is not an error — it yields `truncated=true`.
- `max_elements` clamp 10…500; `settle_ms` clamp 0…2000.
- `Observe` is read-only, but it exposes more than `Snapshot` does: **control values** (the text of edit fields and documents, selected items) in addition to names and rectangles. That is a new read surface for whoever holds the API key. Password fields (UIA `IsPassword`) are excluded: the collector drops their value, so it reaches neither the observation nor the signature, and `Type` on such a field does not read the value back (it reports `not_verified`, see §4). Values of ordinary fields are shown truncated (§1) but are otherwise not filtered — an application that shows a secret in a plain text field shows it to `Observe` as well.

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
    ObservationStore.cs        — id → (locator, window handle, transient) LRU
    ActionExecutor.cs          — pattern and mouse/keyboard actions, ValuePattern read-back, effect;
                                 ActionGuards (enabled check, verified click point) shared with the tools
    FlaUiActionTarget.cs       — live element behind the IActionTarget seam (guarded reads)
    PatternCall.cs             — one UIA pattern call under the 2 s limit (not supported / returned / still running)
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
