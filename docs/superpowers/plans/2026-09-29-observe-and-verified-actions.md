# Observe & Verified Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a compact `Observe` tool with stable element ids and let Click/Type/Perform/MultiSelect/MultiEdit act on those ids with pattern-first execution, mouse fallback and effect verification.

**Architecture:** A UIA collector (`ObservationService`, FlaUI `CacheRequest` per window) produces raw `ObservedNode`s; a pure `ObservationBuilder` turns them into an `Observation` (elements, texts, ids, signature); `ObservationStore` maps ids to `ElementLocator`s and resolves them live. An `ActionExecutor` behind an `IActionTarget` seam runs pattern-first actions and compares signatures before/after. Existing `Snapshot`/`Context`/labels stay untouched.

**Tech Stack:** .NET 10 (`net10.0-windows`), FlaUI.UIA3 5.0.0, ModelContextProtocol 2.2.0, xunit v3 on Microsoft.Testing.Platform, System.Drawing for JPEG.

**Spec:** `docs/superpowers/specs/2026-09-29-observe-and-verified-actions-design.md`

## Global Constraints

- Branch: `feat/observe-verified-actions` (already created, spec committed). Never push; never commit to `master`.
- Build: `dotnet build src/WindowsMCP.NET -c Release`; unit tests: `dotnet test tests/WindowsMCP.NET.Tests -c Release -v q`.
- Every tool body is wrapped in try/catch returning `[ERROR] {ExceptionType}: {message}` (or `ToolHelpers.ErrorResult(ex)` for `CallToolResult` tools); `ErrorFlagFilter` sets `IsError`.
- New tool enums live in `Tools/ToolEnums.cs` with `[JsonConverter(typeof(SnakeCaseEnumConverter<T>))]` (an existing test enforces the attribute).
- `Observe`: `ReadOnly = true`, `Idempotent = true`; defaults `scope=foreground`, `format=markdown`, `screenshot=false`, `max_elements=150` (clamp 10…500).
- Walk time budget 5 s → partial result with `truncated=true`, never an exception.
- Element ids: `"e"` + first 4 chars of base32(SHA-256(locator)); colliding ids within one observation extended to 6 chars.
- `ObservationStore` keeps the last 8 observations (LRU).
- `settle_ms` default 300, clamp 0…2000. `verify` default `true` when `element` is used, otherwise `false`.
- Stall: 3 consecutive element steps with effect `unchanged` → Perform stops with `Stopped after step N: no visible change for 3 steps (stall).`
- Screenshot: JPEG quality 80, longest edge ≤ 1568 px, region = union of observed window rects clipped to the screen.
- Precedence on action tools: `element` > `label` > `loc`. Existing label/loc behaviour and result strings stay unchanged.
- OCR and any decision model are out of scope.
- Code comments/descriptions in English; match surrounding style (static tool classes, singletons injected as tool parameters).

## Review Focus

1. **Stale or foreign id** (element from a closed dialog, id from 9+ observations ago, typo) → `[ERROR] ElementNotFoundException: element eXXXX no longer present — call Observe`, never a click at old coordinates. Pinned in Task 4.
2. **Identical siblings** (two toolbar buttons both named "Open", repeated table rows) → distinct, stable ids; resolving one never hits the other. Pinned in Task 1 and Task 2.
3. **Non-ASCII and private-use names** (`Schließen`, `Bildlauf nach oben`, `\ue8c8` icon glyphs, `µm`) → markdown and JSON render them without exceptions; ids deterministic. Pinned in Task 3.
4. **Very large UIs** (≥ 600 actionable nodes, or walk over budget) → `truncated=true`, markdown footer says `truncated`, topmost-window elements kept first. Pinned in Task 2.
5. **Unknown process / no foreground app** (`scope=process` with a name that has no window; foreground is the taskbar) → `[ERROR]` naming the process or `no foreground application window`, before any UIA walk. Pinned in Task 5.

---

### Task 0: Baseline

- [ ] **Step 1:** `dotnet build src/WindowsMCP.NET -c Release` → `Build succeeded`, 0 errors.
- [ ] **Step 2:** `dotnet test tests/WindowsMCP.NET.Tests -c Release -v q` → all pass (≈223). Record the count.
- [ ] **Step 3:** `dotnet build src/WindowsMCP.NET -c Debug -r win-x64` then `dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q --filter-trait "Category=Schema"` → record pass/fail of `GenerateSchemaDiffReport` and the difference count in `TestData/schema_diff_report.txt` (pre-existing diffs are expected; later tasks may add entries but must not add failures to other schema tests).

### Task 1: Enums, models, element locator

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/ToolEnums.cs`
- Create: `src/WindowsMCP.NET/Models/Observation.cs`, `src/WindowsMCP.NET/Models/ElementLocator.cs`
- Test: `tests/WindowsMCP.NET.Tests/Services/ElementLocatorTests.cs`

**Interfaces — Produces:**
- `public enum ObserveScope { Foreground, Process, Desktop }`, `public enum ActionMethod { Auto, Pattern, Mouse }` (both with the snake-case converter attribute).
- `Models/Observation.cs` (namespace `WindowsMcpNet.Models`):
  - `public sealed record ObservedWindow(nint Handle, string Title, string ClassName, string Process, int Pid, bool Foreground, bool Modal, Rectangle Rect);`
  - `public sealed record ObservedNode(int Index, int? Parent, int Depth, int Window, string ControlType, string Name, string AutomationId, Rectangle Rect, bool Enabled, bool Focused, string? Value, string? Toggle, bool Selected, string? Expand, bool? HitVisible);` — `Window` indexes `ObservedWindow` list; `HitVisible` null = not hit-tested.
  - `public sealed record ObservedElement(string Id, string Type, string Name, string? Label, string? Panel, string? Window, string? Value, string? Toggle, bool Selected, string? Expand, bool Enabled, Rectangle Rect, ElementLocator Locator);`
  - `public sealed record ObservationTimings(long WalkMs, long HitMs, long TotalMs);`
  - `public sealed record Observation(IReadOnlyList<ObservedWindow> Windows, string? FocusId, IReadOnlyList<ObservedElement> Elements, IReadOnlyList<string> Texts, string Signature, bool Truncated, ObservationTimings Timings);`
- `Models/ElementLocator.cs`:
  - `public sealed record LocatorStep(string ControlType, string Key, int Index);` — `Key` = AutomationId if non-empty else Name; `Index` = position among earlier siblings with the same `(ControlType, Key)`.
  - `public sealed record ElementLocator(string Process, string WindowClass, IReadOnlyList<LocatorStep> Path)` with `public string Id(int length)` → `"e" + base32(SHA256(canonical string)).Substring(0, length)`; canonical string = `Process|WindowClass|` + steps joined as `ControlType:Key:Index` with `/`. Base32 alphabet `abcdefghijklmnopqrstuvwxyz234567` (lower-case ids). Override equality by value of `Path` (sequence equality) so records compare correctly.

- [ ] **Step 1: Write failing tests** in `ElementLocatorTests`:
  - `SameLocator_SameId`: two locators built from equal values → `Id(4)` equal, starts with `"e"`, length 5.
  - `DifferentIndex_DifferentId`: `…Button:Open:0` vs `…Button:Open:1` → ids differ (Review Focus 2).
  - `NonAsciiKey_IsDeterministic`: key `"Schließen"` and `"\ue8c8"` → same id on repeated calls, only chars from the base32 alphabet.
  - `Equality_UsesPathSequence`: two locators with separately allocated but equal step lists are `Equals` and have equal `GetHashCode`.
- [ ] **Step 2:** `dotnet test tests/WindowsMCP.NET.Tests -c Release --filter-class "*ElementLocatorTests"` → FAIL (types missing).
- [ ] **Step 3:** Implement enums, records and `ElementLocator.Id`.
- [ ] **Step 4:** Re-run → PASS; run the whole unit suite (enum-attribute test must pass).
- [ ] **Step 5:** Commit `feat: observation models, element locator ids, ObserveScope/ActionMethod enums`.

### Task 2: ObservationBuilder (pure)

**Files:**
- Create: `src/WindowsMCP.NET/Services/ObservationBuilder.cs`
- Test: `tests/WindowsMCP.NET.Tests/Services/ObservationBuilderTests.cs` (helper that builds synthetic `ObservedWindow`/`ObservedNode` lists)

**Interfaces:**
- Consumes: Task 1 records.
- Produces: `public static class ObservationBuilder` with
  - `public static Observation Build(IReadOnlyList<ObservedWindow> windows, IReadOnlyList<ObservedNode> nodes, int maxElements, ObservationTimings timings, bool budgetExceeded)`
  - `public static string ComputeSignature(IReadOnlyList<ObservedWindow> windows, IReadOnlyList<ObservedElement> elements)` — first 8 hex chars of SHA-256 over window `Handle`+`Title` and per element `Id|Name|Value|Toggle|Selected|Expand|Enabled` (no rects).

Rules (spec §1, §2):
- Windows are ordered topmost first; elements ordered by window order, then `Rect.Y`, then `Rect.X`.
- A node is dropped with its subtree when `HitVisible == false`, except `TabItem` whose parent is a kept `Tab`.
- Actionable types: Button, SplitButton, CheckBox, RadioButton, ComboBox, Edit, Spinner, Slider, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, DataItem, HeaderItem. Elements with empty `Name` and empty `Value` are skipped.
- `Label`: for Edit/ComboBox/Spinner/Slider whose `Name` is empty or equals `Value`: `Name` of the nearest preceding sibling of type `Text` with a non-empty name; else null.
- `Panel`: nearest ancestor (excluding the window root) of type Pane/Group/ToolBar/Tab/MenuBar/Menu/StatusBar/Tree/List/Table/DataGrid with a non-empty name; unnamed `ToolBar` → `"Toolbar"`, unnamed `MenuBar` → `"Menu bar"`; null if none.
- `Window`: title of the element's window when it is not the last (bottom-most) window, else null.
- Texts: kept `Text`/`Group`/`Header` nodes with non-empty names, distinct, in element order.
- Ids: locator from process, window class, and the path of `LocatorStep`s from the window root; `Id(4)`, any collision within this observation → both use `Id(6)`.
- `FocusId`: id of the element whose node has `Focused`, else null.
- `Truncated` = `budgetExceeded || actionable count > maxElements`; keep the first `maxElements`.

- [ ] **Step 1: Write failing tests** (one `[Fact]` each):
  - `DropsHitInvisibleSubtree` (hidden docking panel content removed), `KeepsTabItemOfVisibleTab` (TabItem with `HitVisible=false`, parent Tab kept → element present).
  - `NodesWithoutHitTest_AreKept` (popup window nodes with `HitVisible=null`).
  - `PopupElementsComeFirst` (drop-down window before main window; its elements carry `Window = "FileDropDown"`, main elements `Window = null`).
  - `UnnamedEdit_GetsPrecedingTextLabel` (`Text 'Focus Axis'` then `Edit` with Value `90,000` → `Label == "Focus Axis"`).
  - `Panel_UsesNamedAncestor_AndToolbarFallback` (button in unnamed ToolBar → `"Toolbar"`; radio in Group `Nosepiece` → `"Nosepiece"`).
  - `DuplicateSiblings_GetDistinctIds` (two `Button 'Open'` under same parent → different ids; rebuilding gives the same two ids) — Review Focus 2.
  - `Truncates_AtMaxElements` (600 buttons, `maxElements: 150` → 150 elements, `Truncated == true`; popup elements retained) — Review Focus 4.
  - `BudgetExceeded_SetsTruncated`.
  - `Signature_ChangesOnValueToggleSelectedEnabled_NotOnRect` (five builds compared).
  - `Texts_AreDistinctAndNotActionable`.
- [ ] **Step 2:** Run `--filter-class "*ObservationBuilderTests"` → FAIL.
- [ ] **Step 3:** Implement `ObservationBuilder`.
- [ ] **Step 4:** Re-run → PASS.
- [ ] **Step 5:** Commit `feat: ObservationBuilder turns UIA nodes into compact observations`.

### Task 3: Formatting (markdown + JSON)

**Files:**
- Create: `src/WindowsMCP.NET/Services/ObservationFormatter.cs`
- Test: `tests/WindowsMCP.NET.Tests/Services/ObservationFormatterTests.cs`

**Interfaces — Produces:**
- `public static string ToMarkdown(Observation o)`:
  - per window block, topmost first: `## {Title}  ({Process}, pid {Pid})` + ` · foreground` / ` · modal` when true; a `focus: {FocusId}` line after the first block when set;
  - element line: `{Id,-7}{Type} '{Name}'` then, each prefixed by two spaces when present: `label='{Label}'`, `@{Panel}`, `value={Value}`, `toggle={Toggle}`, `selected`, `expanded` (when `Expand == "Expanded"`), `disabled`;
  - `texts: ` + texts joined by ` · ` (omitted when empty);
  - footer `signature {Signature} · {n} elements · {TotalMs} ms` + ` · truncated` when truncated.
- `public static object ToJsonEnvelope(Observation o)` → anonymous object `{ windows[{title,process,pid,foreground,modal,rect}], focus, elements[{id,type,name,label?,panel?,window?,value?,toggle?,selected?,expanded?,enabled?,rect:[x,y,w,h]}], texts, signature, truncated, timings{walk_ms,hit_ms,total_ms} }`; optional fields omitted when null/false/true-default (`enabled` only when false, `selected`/`expanded` only when true). Serialized by `ToolHelpers.JsonResult` (its options already ignore nulls — verify; if not, build with `Dictionary<string, object?>` and skip nulls).

- [ ] **Step 1: Write failing tests:**
  - `Markdown_ElementLine_Format` → exact line `e7q2k  RadioButton '20x'  @Nosepiece` for a sample element with id `e7q2k` (id padded to 7; ids are lower-case, the spec's `e7Q2` examples are illustrative).
  - `Markdown_DisabledAndValue` → contains `value=90,000` and `disabled`.
  - `Markdown_NonAsciiNames` (`Schließen`, `\ue8c8`, `Scale (µm/px)`) → no exception, names appear verbatim — Review Focus 3.
  - `Markdown_Footer_Truncated` → ends with `· truncated`.
  - `Json_OmitsDefaults` → serialized JSON has no `"enabled"` for enabled elements and has `"enabled":false` for disabled ones; `rect` is a 4-int array.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: markdown and JSON formatting for observations`.

### Task 4: ObservationService, ObservationStore, DI

**Files:**
- Create: `src/WindowsMCP.NET/Services/ObservationService.cs`, `src/WindowsMCP.NET/Services/ObservationStore.cs`, `src/WindowsMCP.NET/Services/ElementNotFoundException.cs`
- Modify: `src/WindowsMCP.NET/Server/McpServerSetup.cs:54-57` (register both singletons)
- Test: `tests/WindowsMCP.NET.Tests/Services/ObservationStoreTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - `public sealed class ElementNotFoundException(string id) : Exception($"element {id} no longer present — call Observe")`.
  - `ObservationService` (ctor: `ILogger<ObservationService>`; owns its own `UIA3Automation`):
    - `public Observation Observe(ObserveScope scope, string? process, int maxElements, CancellationToken ct)` — resolves windows (EnumWindows z-order, visible, top-level; foreground → pid of `GetForegroundWindow`; `process` → first process by name with a visible window), one `CacheRequest` (TreeScope Subtree, filter `IsControlElement=true`, properties per spec §1) per window, walks `CachedChildren` dropping offscreen and ≤1 px nodes, hit-tests (FromPoint at centre, RuntimeId match against node/descendants/ancestors) only the last window of each process, sets `budgetExceeded` after 5 s, calls `ObservationBuilder.Build`.
    - `public string Signature(ObserveScope scope, string? process, CancellationToken ct)` — same collection without hit-testing → `Build(...).Signature`.
    - `public AutomationElement? FindLive(ElementLocator locator)` — spec §2 resolution (path walk, then unique type+key match in the same window class of the same process); null when none/ambiguous.
    - Throws `InvalidOperationException("no foreground application window")` when the foreground window is missing, belongs to `explorer`'s taskbar (`Shell_TrayWnd`) or to this process; `InvalidOperationException($"process '{process}' has no visible window")` for `scope=process` — both before any UIA walk; `ArgumentException` when `scope=process` and `process` is empty.
    - Collections serialized with a `Lock`.
  - `ObservationStore`:
    - `public void Remember(Observation o)`; keeps id → locator for the last 8 observations (evict oldest observation's ids unless re-seen in a newer one).
    - `public ElementLocator Get(string id)` → throws `ElementNotFoundException(id)` when unknown/evicted.

- [ ] **Step 1: Write failing tests** (`ObservationStoreTests`, synthetic `Observation`s):
  - `Get_KnownId_ReturnsLocator`.
  - `Get_UnknownId_Throws_WithCallObserveMessage` → message `element e0000 no longer present — call Observe` — Review Focus 1.
  - `Evicts_AfterEightObservations` (id only in observation #1, then 8 more → throws).
  - `ReSeenId_SurvivesEviction` (id in #1 and #9 → still resolvable).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement store, exception, service; register `services.AddSingleton<ObservationService>(); services.AddSingleton<ObservationStore>();`. **Step 4:** Run store tests → PASS; build Release → 0 warnings as errors (analyzers are on).
- [ ] **Step 5:** Commit `feat: ObservationService (cached UIA walk, hit-test, live resolution) and ObservationStore`.

### Task 5: `Observe` tool and JPEG capture

**Files:**
- Create: `src/WindowsMCP.NET/Tools/ObserveTools.cs`
- Modify: `src/WindowsMCP.NET/Services/ScreenCaptureService.cs` (add region JPEG), `src/WindowsMCP.NET/Server/McpServerSetup.cs:38-46` (Instructions line), `tests/WindowsMCP.NET.Tests/Server/McpServerSetupTests.cs:29-35` (21 tools, contains `Observe`)
- Test: `tests/WindowsMCP.NET.Tests/Tools/ObserveToolsTests.cs`, `tests/WindowsMCP.NET.Tests/Services/ScreenCaptureScaleTests.cs`

**Interfaces:**
- Consumes: Tasks 3–4.
- Produces:
  - `public static Size ScreenCaptureService.ScaleToFit(Size source, int maxEdge)` (pure; never upscales) and `public byte[] CaptureRegionJpeg(Rectangle region, int maxEdge = 1568, long quality = 80)`.
  - `[McpServerTool(Name = "Observe", ReadOnly = true, Idempotent = true)] public static CallToolResult Observe(ObservationService observation, ObservationStore store, ScreenCaptureService capture, ObserveScope scope = ObserveScope.Foreground, string? process = null, OutputFormat format = OutputFormat.Markdown, bool screenshot = false, int max_elements = 150, CancellationToken ct = default)` — clamps `max_elements` 10…500, calls `Observe`, `store.Remember`, returns `ToolHelpers.TextResult(ToMarkdown)` or `ToolHelpers.JsonResult(ToJsonEnvelope)`; when `screenshot`, appends `ImageContentBlock.FromBytes(jpeg, "image/jpeg")` for the union of window rects clipped to the virtual screen. Description explains scope values, element ids (“pass them as `element` to Click/Type/Perform”) and documents the JSON shape inline (project convention).
  - Instructions gain: `- For precise UI work call Observe (compact state of the foreground app with stable element ids) and pass ids as "element" to Click/Type/Perform; results report effect=changed/unchanged.`

- [ ] **Step 1: Write failing tests:**
  - `ScaleToFit_LimitsLongestEdge` (3840×2160, 1568 → 1568×882), `ScaleToFit_NeverUpscales` (800×600 → 800×600).
  - `Observe_ProcessScopeWithoutName_ReturnsError` → text starts `[ERROR] ArgumentException`.
  - `Observe_UnknownProcess_ReturnsErrorNamingProcess` (`process: "no_such_process_xyz"`) → contains `no_such_process_xyz` — Review Focus 5.
  - `Observe_Description_DocumentsElementIdsAndJsonShape` (contains `element`, `signature`, `truncated`).
  - `AddWindowsMcpServer_RegistersAllTools` (renamed from `…AllTwentyTools`): count 21, contains `Observe`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run unit suite → PASS.
- [ ] **Step 5:** Commit `feat: Observe tool with optional downscaled JPEG screenshot`.

### Task 6: ActionExecutor and live adapter

**Files:**
- Create: `src/WindowsMCP.NET/Services/ActionExecutor.cs` (includes `IActionTarget`, `IInputDriver`, `ActionOutcome`, `ActionEffect`), `src/WindowsMCP.NET/Services/FlaUiActionTarget.cs`
- Test: `tests/WindowsMCP.NET.Tests/Services/ActionExecutorTests.cs` (fakes for target, input, signature)

**Interfaces — Produces:**
- `public enum ActionEffect { Changed, Unchanged, ValueVerified, ValueMismatch, NotVerified }` with extension `ToWire()` → `changed|unchanged|value_verified|value_mismatch|not_verified`.
- `public sealed record ActionOutcome(string Via, ActionEffect Effect);` — `Via` ∈ `Invoke`, `ExpandCollapse`, `Toggle`, `SelectionItem`, `ValuePattern`, `mouse`, `keyboard`.
- `public interface IActionTarget { string ControlType { get; } bool HasChildren { get; } Rectangle CurrentRect { get; } bool TryInvoke(); bool TryExpandCollapse(); bool TryToggle(); bool TrySelect(); bool CanSetValue { get; } bool TrySetValue(string value); string? ReadValue(); bool TryFocus(); }`
- `public interface IInputDriver { void LeftClick(Point p); void TypeText(string text, bool clear, bool pressEnter); void PressEnter(); }` — production `InputDriver` wraps `User32.SetCursorPos` + `InputFactory` exactly as `InputTools.Click`/`Type` do today (extract, do not duplicate).
- `public sealed class ActionExecutor(IInputDriver input)` with
  - `public ActionOutcome Click(IActionTarget t, ActionMethod method, Func<string>? signature, int settleMs)` — `signature == null` → no verification (`NotVerified`). Pattern table from spec §3 (MenuItem with `HasChildren` and ComboBox → ExpandCollapse). Auto: pattern → settle → compare; unchanged or pattern unsupported → `LeftClick(centre of CurrentRect)` → settle → compare. `Pattern`: no fallback. `Mouse`: skip pattern.
  - `public ActionOutcome Type(IActionTarget t, string text, bool clear, bool pressEnter, Func<string>? signature, int settleMs)` — spec §3 Type steps; read-back when `ReadValue()` non-null: trimmed equality with expected (`clear ? text : previous + text`) → `ValueVerified` else `ValueMismatch`; without read-back fall back to signature comparison.
- `FlaUiActionTarget(AutomationElement el)` implements `IActionTarget` with FlaUI patterns (`Patterns.X.PatternOrDefault`); each `Try*` returns false on unsupported/exception.

- [ ] **Step 1: Write failing tests:**
  - `Click_Button_UsesInvoke_Changed`, `Click_RadioButton_UsesSelectionItem`, `Click_CheckBox_UsesToggle`, `Click_MenuItemWithChildren_UsesExpandCollapse`.
  - `Click_PatternUnchanged_FallsBackToMouseAtCurrentRectCentre` (signature fake returns A, A, B → `Via == "mouse"`, `Changed`, click point = centre of `CurrentRect`).
  - `Click_MethodPattern_NoFallback` (→ `Unchanged`, no mouse call).
  - `Click_MethodMouse_SkipsPattern`.
  - `Click_NoSignature_NotVerified`.
  - `Type_ValuePattern_Verified`, `Type_ReadBackMismatch_ReportsMismatch`, `Type_NoValuePattern_UsesKeyboard` (`Via == "keyboard"`, `TryFocus` called first).
  - `ToWire_Values` (`ValueVerified` → `value_verified`).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (settle via `Thread.Sleep(settleMs)`; tests pass 0). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: pattern-first ActionExecutor with mouse fallback and effect verification`.

### Task 7: Element ids on Click, Type, MultiSelect, MultiEdit

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/InputTools.cs` (Click `:50-90`, Type `:92-132`), `src/WindowsMCP.NET/Tools/MultiTools.cs`, `src/WindowsMCP.NET/Server/McpServerSetup.cs` (register `ActionExecutor`, `IInputDriver`)
- Test: `tests/WindowsMCP.NET.Tests/Tools/ElementActionToolsTests.cs`

**Interfaces:**
- Consumes: Tasks 4 and 6.
- Produces (additive parameters; existing ones keep names, order and defaults; new DI parameters are services and do not appear in the schema):
  - `Click(..., string? element = null, ActionMethod method = ActionMethod.Auto, bool? verify = null, int settle_ms = 300)`
  - `Type(..., string? element = null, bool? verify = null, int settle_ms = 300)`
  - `MultiSelect(..., string[]? elements = null)` — ids resolved live to the centre of their current rect and clicked with the existing mouse path (Ctrl handling unchanged); no verification.
  - `MultiEdit(..., JsonElement? elements = null)` — `[[id, text], …]`, each via `ActionExecutor.Type` with `clear: true`, verification on.
  - Shared helper `internal static (IActionTarget Target, string Describe) ElementTargets.Resolve(string id, ObservationStore store, ObservationService svc)` → `store.Get(id)` then `svc.FindLive(locator) ?? throw new ElementNotFoundException(id)`; `Describe` = `"{id} ({Type} '{Name}')"`.
  - Result strings for element paths: `Clicked {describe} via {Via} — effect: {effect}` and `Typed {n} chars into {describe} via {Via} — effect: {effect}`; label/loc paths unchanged.
  - `verify ?? (element is not null)`; `settle_ms` clamped 0…2000; signature func = `() => svc.Signature(ObserveScope.Foreground, null, ct)`.

- [ ] **Step 1: Write failing tests:**
  - `Click_UnknownElement_ReturnsElementNotFound` → `[ERROR] ElementNotFoundException: element e9zz9 no longer present — call Observe` (no UIA needed: store is empty) — Review Focus 1.
  - `Type_UnknownElement_ReturnsElementNotFound`.
  - `MultiSelect_UnknownElement_ReturnsElementNotFound`, `MultiEdit_ElementsShape_Validated` (`[["e1"]]` → `[ERROR] ArgumentException`).
  - `Click_Schema_HasElementMethodVerifySettle` (reflection over parameters: names `element`, `method`, `verify`, `settle_ms`; `method` default `Auto`).
  - Existing `InputToolsParity`/`PerformToolsTests` compile and pass after call-site updates.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement; update internal callers (`PerformTools` calls to `InputTools.Click/Type`) to pass the new services. **Step 4:** Run unit suite → PASS.
- [ ] **Step 5:** Commit `feat: element ids with verified pattern-first actions on Click/Type/MultiSelect/MultiEdit`.

### Task 8: Perform with element steps and stall detection

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/PerformTools.cs` (parameters `:20-30`, step dispatch `:80-135`, result formatting `:160-180`)
- Test: `tests/WindowsMCP.NET.Tests/Tools/PerformStallTests.cs`

**Interfaces:**
- Consumes: Tasks 6–7 (`ElementTargets.Resolve`, `ActionExecutor`).
- Produces:
  - New Perform parameters: `bool verify = true`, `bool stop_on_stall = true`, `int settle_ms = 300` (verify applies to element steps only).
  - Steps accept `"element"` (and `"method"` for click) for `click`/`type`; resolved per step. `if_exists` with `element`: skip when the id no longer resolves.
  - Step line suffix ` — effect: {effect}` for element steps.
  - Stall logic isolated as `internal sealed class StallTracker(int limit = 3) { public bool Record(ActionEffect e); }` — `Unchanged` increments, any other verified effect resets, `NotVerified` neither; `Record` returns true when the limit is reached.
  - Stop message: `Stopped after step {n}: no visible change for 3 steps (stall).`

- [ ] **Step 1: Write failing tests:**
  - `StallTracker_ThreeUnchanged_Stops`, `StallTracker_ChangedResets`, `StallTracker_NotVerifiedIgnored`.
  - `Perform_ElementStep_UnknownId_FailsStep` (`stop_on_error: true` → `Step 1: FAIL — ElementNotFoundException: element e0000 no longer present — call Observe`).
  - `Perform_ElementStep_IfExists_SkipsUnknownId` → `SKIP`.
  - `Perform_Schema_HasVerifyStallSettle`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run unit suite → PASS (existing `PerformToolsTests`/`PerformProgressTests` unchanged).
- [ ] **Step 5:** Commit `feat: Perform resolves element ids per step, reports effects, stops on stalls`.

### Task 9: Parity tests, docs, acceptance

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ObserveParityTests.cs`
- Modify: `CLAUDE.md` (tool count 21, tool table row for Observe, Key Patterns bullet "Observe → act by element id", Architecture note on verification)

- [ ] **Step 1: Write the desktop parity tests** (`[Trait("Category","Functional")]`, `[Trait("Category","Desktop")]`, dotnet mode only):
  - `Observe_Notepad_ListsDocumentEdit`: `App(mode=ensure, name=notepad)` → `Observe(format=json)` → `structuredContent.elements` contains an element with `type` `Document` or `Edit`; `signature` non-empty.
  - `Type_ByElementId_ValueVerified`: type `observe_parity` into that id with `clear=true` → result contains `effect: value_verified`.
  - `Click_MenuByElementId_Changed`: click the `File` MenuItem id → contains `effect: changed`; then `Shortcut escape`.
  - `StaleId_AfterClose_ElementNotFound`: close Notepad without saving (`Process kill` of the launched notepad or `alt+f4` + "Don't save" via Observe/Click), then `Click(element=<old id>)` → `[ERROR] ElementNotFoundException`.
- [ ] **Step 2:** `dotnet build src/WindowsMCP.NET -c Debug -r win-x64` then `dotnet test tests/WindowsMCP.NET.ParityTests -c Release -v q` → new tests PASS; schema tests: `Observe` listed under "New tools", no new failures versus Task 0.
- [ ] **Step 3:** Update `CLAUDE.md`; run unit suite once more → all PASS (≈223 + new).
- [ ] **Step 4:** Commit `test: Observe/element-id parity tests; docs: CLAUDE.md for Observe`.
- [ ] **Step 5: Acceptance on SWENTW3 (manual, report results; no code):** publish Release, update SWENTW3 (`C:\Promicron\Tools\WindowsMCP.NET.exe`, restart via its autostart) — ask the user before replacing the running server; with MCS running on `E:\ollaya\mcs-sim-config`: (a) `Screenshot` after RDP reconnect is 1920×1080 and a `Click` on the MCS "File" menu by label lands; (b) `Observe` on MCS main screen contains `RadioButton '20x'`, `Edit` labelled `Focus Axis`, the View Settings buttons, markdown < 8 KB, total < 1.5 s; (c) File menu open → drop-down items listed first; (d) `Click(element=<Camera TabItem id>)` → `effect: changed` via `mouse`. Record numbers in the PR/summary.

---

## Self-Review Notes

- Spec coverage: §1 → Tasks 2, 3, 4, 5; §2 → Tasks 1, 2, 4; §3 → Tasks 6, 7; §4 → Tasks 6, 8; §5 → Tasks 4, 5, 7; §6 → file layout across tasks; §7 → Tasks 1–9; §8 → Task 9 (release via normal pipeline is outside this branch).
- Error type naming: the spec's `ElementNotFound` surfaces as `ElementNotFoundException` because the project prints exception type names; message text matches the spec.
