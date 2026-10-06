# WindowsMCP.NET

Windows desktop automation MCP server for Claude Code. Provides 22 tools for UI automation, file system operations, and system management on remote Windows machines.

## Build & Test

```bash
# Build (use Release — Debug exe may be locked by running instance)
dotnet build src/WindowsMCP.NET -c Release

# Unit tests (841 tests). Test projects run on Microsoft.Testing.Platform (xunit v3, see global.json);
# filter with --filter-trait / --filter-not-trait "Category=..." instead of the old --filter syntax.
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q

# Parity tests (51 tests: 21 schema + 30 functional — requires desktop session)
# NOTE: Parity tests launch the Debug exe from bin/Debug/.../win-x64/. Build that first
# with `dotnet build src/WindowsMCP.NET -c Debug -r win-x64` or the apphost will fail
# with "No frameworks were found".
dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q

# Publish single-file exe
dotnet publish src/WindowsMCP.NET -c Release -r win-x64 -o publish/

# Publish with GitHub PAT for auto-update
dotnet publish src/WindowsMCP.NET -c Release -r win-x64 -p:GitHubPat=<token> -o publish/
```

## Architecture

- **Runtime**: net10.0-windows (LTS), ModelContextProtocol SDK 2.2.0 (MCP spec 2026-07-28; Streamable HTTP in stateless mode)
- **Transport**: HTTP (remote, default) or stdio (local Claude Code)
- **Tools**: Static classes in `src/WindowsMCP.NET/Tools/` with `[McpServerTool]` attribute
- **Services**: Singletons injected as tool method parameters (`DesktopService`, `UiTreeService`, `ScreenCaptureService`)
- **Error handling**: All tools wrap their body in try-catch, returning `[ERROR] ExceptionType: message` instead of throwing (the SDK forwards only `McpException` messages; any other exception becomes a generic "An error occurred invoking" text, unchanged through SDK 2.x). A central `ErrorFlagFilter` (`Server/ErrorFlagFilter.cs`) detects this prefix and sets `CallToolResult.IsError=true` so clients can branch on the protocol flag instead of string-matching the body
- **Security**: API key auth (Bearer token, constant-time compare), optional IP allowlist (plain or CIDR, IPv4/IPv6), optional HTTPS. `/health` is anonymous but returns liveness only; details (version, host, PID, tool count) require the key. Auto-update verifies the release's `.sha256` before swapping the exe
- **Verified element actions**: element-id actions (`ActionExecutor`) act through a UIA pattern or the mouse/keyboard; success is judged by the signature of the process owning the element's window (visible top-level windows, hash of actionable elements) before vs. after the action — for `Type` by the value read-back — giving `effect`. Two rules hold on every element path: **input never reaches anything but the resolved target, and no action is performed twice** — when in doubt the action is refused with an `[ERROR]` and nothing is done:
  - click strategy (`method=auto`): control types clicked through `Invoke` (Button, SplitButton, Hyperlink, MenuItem without children) get a **mouse click first** when the click point is safe and `Invoke` only when it is not — a WinForms `Invoke` whose handler opens a modal dialog does not return until the dialog is closed, and meanwhile every UI Automation request to that application times out. Toggle, SelectionItem and ExpandCollapse types stay pattern-first (mouse where the element has no pattern); `method=pattern`/`mouse` force one way
  - a selection is verified by the target itself: after a `Select` that has returned, `IActionTarget.IsSelected` (SelectionItem `IsSelected`, null when not reported) decides — `true` → no click, effect from the signature; `false` → the select did not take (docking tabs): one mouse click when the click point is safe, else `via SelectionItem — unchanged` whatever else the signature picked up; `null` → the old rule (signature unchanged → mouse click). A select that has not returned is never followed by a click; `method=pattern` never clicks. When the click that was due after a select could not be made, the result says why: `via SelectionItem — effect: unchanged — the element could not be clicked instead: the element is covered by another window` (`ActionOutcome.NotClicked`; measured on MCS: a docking tab under an open auto-hide fly-out — the tab is still listed by `Observe`, close the fly-out first)
  - refused before anything is sent: element in a window disabled by a modal dialog (`ElementTargets.Resolve`), disabled target, no safe click point where the mouse is the only way (`ActionGuards.ClickPoint` — the one place a mouse coordinate for an element comes from: no interactive desktop, i.e. no foreground window in a disconnected/non-rendered session; no visible area; or the window at the point is not the element's — `IActionTarget.OwnsPoint`, Win32 hit-test via `Native/WindowHit`, no UIA hit-test: the top-level window at the point must be the window the element was resolved in (a popup/menu/dialog of the same application covers it too), and — checked by window, not by pixel — the deepest window at the point must be the element's native window (the nearest ancestor-or-self with a `NativeWindowHandle`) or a child window of it, which refuses another control's window lying over the element and a native window that is clipped away by a scroll container, hidden or disabled at that point; when the native window does not contain the point (XAML islands; elements scrolled or overflowed out of their host) the point is safe unless the target reports `IsOffscreen`; when it cannot be determined only the top-level check applies. **Not protected** (the click can still land on something else): (a) a windowless element overlapped by another windowless element of the same native window; (b) an element overlapped by a child window of its own native window; (c) an element whose native window is the top-level window itself (title-bar buttons, popup menu items) — every window in it is a child of it; (d) a windowless element clipped inside its host while its centre stays within the host's rectangle — `IsOffscreen` is not asked there, so whatever it reports; (e) an element scrolled or overflowed out of its host that does not report `IsOffscreen` (unsupported, failed read, or `false`); (f) an element whose native window cannot be determined — only the top-level window is checked), keyboard focus not confirmed on the target (no key is sent without it), no interactive desktop on the keyboard path (checked before anything is sent, also before SetFocus: `… — nothing was typed`; for Enter after a `SetValue` that already happened: `value was set, but there is no interactive desktop — Enter was not sent`; when that `SetValue` has not returned: `value was sent, but the call has not returned, and … — Enter was not sent`). A refusal while focusing for typing is worded `— nothing was typed`, never "nothing was clicked"
  - a UIA pattern call that throws or does not return within 2 s (`PatternCall`) counts as attempted: it is verified and never repeated with the mouse; `IActionTarget.Try*` returns `PatternCallResult` (`NotSupported` only when the pattern is unsupported, else `Returned` or `StillRunning`). A call still running after the limit is carried in `ActionOutcome.CallPending` and the result text says ` — the call has not returned after 2 s: if it opened a modal dialog, the application cannot be observed until that is closed …` (`ActionOutcome.EffectText`; a condition, because in a disconnected session every pattern call is slower than the limit). A mouse click is never followed by a pattern call either
  - `Type` into a password field (`IActionTarget.IsPassword`) is always `not_verified` — its value is neither read back nor part of the signature — and `StallTracker` ignores `not_verified`, so password entries never count towards `stop_on_stall`
  - reads before the action fail as `ElementNotFoundException` with nothing done; no read after an action (read-back, rect, focus, hit-test, signature) may turn the executed action into an error
- **Cancellation & progress**: async tools take a `CancellationToken` (bound to the client's request; PowerShell/Notification kill their child process on cancel); `Perform` reports one progress notification per step via `IProgress<ProgressNotificationValue>`

## Tools (22)

| Tool | Purpose |
|------|---------|
| **Context** | Get system state (active window, screenshot, UI tree, clipboard, processes) |
| **Observe** | Compact state of the foreground app (windows, focus, actionable elements with stable ids, optional JPEG) for precise interaction. Values ≤ 200 chars, names/texts ≤ 120 (`…(+n chars)`), ≤ 80 texts; password values are never collected; a failed screenshot is reported in a text block, not as an error. Never silently empty: a window that does not answer UI Automation is listed as `unreadable` (Win32 title/rect, no elements) with a one-line explanation, and the screenshot is still returned. When `max_elements` (default 300, clamped 10–500) cuts the list, the footer says `truncated: N more elements (max_elements=300)`; JSON `"omitted": N` |
| **Expect** | Check conditions on the observed state with fixed rules, no model: window open/closed (modal), element exists/absent/enabled/disabled/selected/not_selected/checked/unchecked/expanded/collapsed and `value`/`value_contains`, static text. Answers `pass`/`fail`/`unknown` per condition; `timeout_ms` (≤ 30 s) waits until all pass. `fail`/`unknown` are results, not errors |
| **Perform** | Execute batched UI action chains (click, type, shortcut, scroll, move, wait); click/type steps accept `element` ids (rejected on other steps), report `effect`, stop after 3 element steps without visible change |
| **Snapshot** | Capture screenshot + build UI element tree with numbered labels |
| **Screenshot** | Fast screenshot without rebuilding UI tree |
| **Click** | Click at coordinates, a labeled UI element or an `Observe` element id (`element`; `method=auto`: mouse first for buttons/links/plain menu items, UIA pattern first for the rest; reports `effect`) |
| **Type** | Type text with optional target click or `Observe` element id (`element`; value-verified, a password field reports `not_verified`); `\n` is sent as Enter, `\t` as Tab. With `element`, `clear=false` (default) appends to the field's value — the result then says `Appended … — now '…'`; a `value_mismatch` says `(field shows '…', expected '…')` |
| **Shortcut** | Send keyboard shortcuts (ctrl+c, alt+f4, win+printscreen, ctrl++ for the plus key; see `InputTools.ParseShortcut`) |
| **Scroll** | Scroll mouse wheel |
| **Move** | Move cursor, optional drag |
| **Wait** | Pause execution (max 10s) |
| **MultiSelect** | Click multiple UI elements (labels or element ids) sequentially |
| **MultiEdit** | Fill multiple form fields (labels or element ids) |
| **FileSystem** | File ops: read, write, copy, move, delete, list, search, info, read_base64, write_base64. `list/search/info` support `format=json` and `offset/limit` pagination (default limit 200) |
| **PowerShell** | Execute PowerShell commands |
| **Process** | List or kill processes. `list` supports `format=json`, `offset/limit/has_more` pagination. `kill` refuses PID 0/4, the server itself and critical system processes (`ProcessGuard`) |
| **Registry** | Read/write/delete/list Windows registry. `get/list` support `format=json`; `list` paginates per section (subkeys+values, default limit 200) |
| **App** | Launch, focus, check, or resize windows. Modes: launch, ensure, status, switch, resize. `status` supports `format=json` returning all matches |
| **Clipboard** | Get or set clipboard text |
| **Notification** | Show Windows toast notifications (title/message passed via env vars — injection-safe) |
| **Scrape** | Fetch URL and convert HTML to Markdown |

## Key Patterns

- **Observe → act by element id**: `Observe` returns stable ids; pass them as `element` to Click/Type/Perform (click and type steps)/MultiSelect/MultiEdit. Actions use a UIA pattern or the mouse/keyboard (buttons, links and plain menu items are clicked with the mouse when that is safe — see "Verified element actions") and report `effect` (changed/unchanged/value_verified/value_mismatch/not_verified). `Perform` stops after 3 element steps without visible change (`stop_on_stall`).
- **Expect answers only what the observation proves** (`ExpectationParser` → `ExpectationEvaluator` → `ExpectationFormatter`, all pure; `ExpectTools.RunAsync` is the wait loop with injected clock and pause): a condition is `pass` or `fail` only when one observation (taken with `max_elements=500`) proves it, otherwise `unknown` with the reason — the collection ran out of its time budget (`Observation.BudgetExceeded`: listed nodes may be hidden, windows may be missing — all element/text conditions unknown, windows only answered when one matches); nothing found while the element list is cut (`Omitted`/`Truncated`) or a window is `unreadable` or minimised (`Observation.MinimizedWindows` — a minimised window has no rectangle and is not in the window list; a window condition still finds it: open → pass, closed → fail), and then also a selector's single match (a second one cannot be ruled out; ids are not affected); several elements fit the selector (except `exists` → pass, `absent` → fail); `selected=false` from a `TabItem` (docking tabs never report their selection) or from a control that did not answer `IsSelected` (`ObservedElement.SelectionReported`); no toggle/expand state reported; a value check on a password field (`Password`) or on a control that did not answer the Value property (`ValueReported` — an empty field that did answer counts as `""`); an element id the store does not know or whose application is outside the scope. Element ids are resolved through `ObservationStore.Find` and matched by locator + window, never as strings. The `type` of a selector must be one of the types an observation lists (`ObservationBuilder.ActionableTypes`) — any other could never match and is rejected. `ScopeGuard` keeps a call on what it was asked about: `scope=foreground` pins the first application observed (another one in front → `Observation.Unobserved`, everything unknown, the wait goes on); `ObservationService.CheckScope` is asked before every observation, so nothing in scope is a state and not an error — no foreground window → unknown, `scope=process` without a visible window → a complete empty observation (`closed`/`absent` pass: wait for an application to start or exit). Only the last observation of a call is remembered in the store. Names, panels and window titles compare case-insensitively (`match`: exact|contains), values case-sensitively. Overall: all pass → pass, any fail → fail, else unknown. Waiting ends only on overall pass, the time limit or cancellation — a fail may still become a pass; invalid conditions are an `[ERROR]` naming the 1-based condition index
- **Element ids have window affinity**: an id resolves only in the window it was observed in while that window exists; ids of transient windows (popup, menu, dialog) die with their window, main-window ids fall back to same-class windows of the process name. **Ids are stable while the application runs**; whether they survive a restart depends on the application: the locator keys on AutomationId (else Name), and WinForms reports a control's window handle as its AutomationId, which changes with every start (measured on MCS: 94 of 97 main-window ids differ after a restart; its dialogs expose real AutomationIds/names and keep theirs). Kept on purpose — the handle identifies the control instance exactly, positional indexes would shift whenever a sibling pane appears. Identical locators in one observation (two same-class windows) get ids derived from locator + window handle. `ObservationStore` keeps `StoredElement(Locator, WindowHandle, Transient)`; `ObservationService.FindLive` returns the element with the hwnd it was found in
- **Observation content rules** (`ObservationBuilder`/`ObservationFormatter`, pure and unit-tested): input types (Edit, ComboBox, Spinner, Slider, Document) are emitted even when unnamed and empty; markdown is strictly one line per element (`\r`/`\n`/`\t` escaped); display truncation never affects the signature (full value); a window without elements keeps its markdown heading unless it is untitled, not foreground and readable (menu helper windows)
- **One copy per element**: WinForms lists the items of an open drop-down (and the controls of an owned dialog) under the main window too. A main-window node whose centre lies in another top-level window is not hit-visible (`ObservationService.ClassifyHit`: Win32 pre-check via `Native/WindowHit` — `GetAncestor(WindowFromPoint, GA_ROOT)` — before UIA's `FromPoint`), so only the copy in the popup's/dialog's own window block is listed. The node carries the reason (`ObservedNode.InOtherWindow`): the builder's exemption that keeps a `TabItem` under a visible `Tab` although UIA's hit-test missed it (docking tabs) does not apply to a tab item drawn in another window. `WindowHit` is the single definition of "the window at a point", shared with `FlaUiActionTarget.OwnsPoint`
- **An observation is never silently empty**: a window whose UIA root/subtree cannot be read (typically while an `Invoke` that opened a modal dialog is still pending) becomes an `ObservedWindow` with `Unreadable = true` — title/class/rect from Win32, no nodes, part of the signature, heading `· unreadable`, JSON `"unreadable": true`, plus one line before the footer. `ObservationService.ReadWindow` (pure rule): one UIA attempt per process and collection — after the first window that does not answer, the other windows of that pid are marked unreadable unasked (also after the 5 s budget is spent: recording them costs only Win32 calls); a window that vanished meanwhile is dropped. A window classified as unreadable is logged at Warning, one that was merely gone at Debug
- **Efficient UI workflow**: Use `Context` to get state, then `Perform` to batch actions (2 calls instead of 5+)
- **Label-based interaction**: `Snapshot` assigns numbered labels to UI elements; `Click`/`Type` reference labels
- **Binary file transfer**: Use `read_base64`/`write_base64` for cross-machine binary file operations (1MB limit)
- **Foreground without screenshots**: Use `App(mode="ensure", name="notepad")` to focus an app if running, launch if not — one call instead of screenshot+parse workflow
- **Structured output**: Pass `format="json"` to `App.status`, `FileSystem.list/search/info`, `Process.list`, `Registry.get/list` to get parseable JSON envelopes instead of human-readable text — useful when chaining tool output into agent logic. The exact JSON shape per mode is documented inline in each `format` parameter description so agents can compose calls without round-tripping. These tools return `CallToolResult` built via `ToolHelpers.JsonResult` (text block + `structuredContent`, MCP 2025-06-18+), `TextResult` (markdown) or `ErrorResult` (`[ERROR]` text with `IsError=true`)
- **Pagination**: List tools (`FileSystem.list/search`, `Process.list`, `Registry.list`) accept `offset`/`limit` and report `has_more`/`next_offset` so agents can page through large result sets
- **Helpers**: `ToolHelpers.cs` centralizes pagination (`Paginate`), JSON serialization options, coordinate parsing (`ToPoint`) and output capping — reuse instead of duplicating per-tool
- **Enumerated parameters**: `mode`, `format`, `button`, `direction`, `sort_by`, … are C# enums in `Tools/ToolEnums.cs`, so the input schema lists the allowed values. Every tool enum carries `[JsonConverter(typeof(SnakeCaseEnumConverter<T>))]` (snake_case on the wire, case-insensitive member names accepted); a test enforces the attribute. Tool arguments are bound with `McpServerSetup.ToolSerializerOptions`, a copy of the SDK defaults without the global enum converter — options-level converters would otherwise override the attributes
- **Coordinates** are `int[]` (`[x, y]`), not `JsonElement`, so the schema is `array of integer`; `Perform` steps still arrive as raw JSON and are converted via `ParsedStep.GetIntArray/GetEnum`

## Project Structure

```
src/WindowsMCP.NET/
  Tools/           # MCP tool implementations (static classes)
  Services/        # Singletons: DesktopService, ScreenCaptureService, UiTreeService, UiAutomationService,
                   # ObservationService (UIA collection, live resolution), ObservationBuilder (pure filter/ids/signature), ObservationStore (id lookup),
                   # ExpectationParser/Evaluator/Formatter (Expect: pure rules over an Observation),
                   # ActionExecutor + ActionGuards (verified pattern/mouse actions, input guards), FlaUiActionTarget/PatternCall (live element, time-limited pattern calls)
  Native/          # P/Invoke: User32, Kernel32; InputFactory (shared SendInput builders, text→keystrokes); WindowHit (the window at a screen point)
  Models/          # WindowInfo, UiElementNode, AnnotatedTree, Observation, ObservedNode, ElementLocator, Expectation
  Config/          # AppConfig, CliParser, ConfigManager
  Setup/           # TrayIcon, UpdateChecker, CertificateGenerator, SetupWizard
  Security/        # ApiKeyMiddleware, IpAllowlist (CIDR/IPv6), IpAllowlistMiddleware
tests/
  WindowsMCP.NET.Tests/         # Unit tests
  WindowsMCP.NET.ParityTests/   # Integration/schema parity tests
```

## CI/CD

- GitHub Actions on push to master: build, test, publish, create GitHub release
- CalVer versioning: `YYYY.MM.patch` (auto-incremented)
- PAT injection via MSBuild: `-p:GitHubPat=<token>` replaces `%%GITHUB_PAT%%` in UpdateChecker.cs
