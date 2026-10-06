# Expect — Deterministic Checks on the Observed State — Design Specification

**Date:** 2026-10-04
**Status:** Approved (design), spec under review
**Sub-project:** 2 of the "cheaper, faster computer use" plan, redefined: the decision service is dropped, deterministic checks take its place (0 spike · 1 observation & verification · **2 deterministic checks** · 3 local step loop · 4 MCS test runner)
**Depends on:** `Observe` (release 2026.10.1), existing tool conventions (`ToolHelpers`, `ErrorFlagFilter`, `ToolEnums`)

## Overview

An agent or a test runner often needs a yes/no answer about the screen: is the Login dialog open, is `StartProgram` enabled, does the field show `90,000`. Today that takes an `Observe` call and reading its output; waiting for a state takes a `Wait`/`Observe` loop over the network.

This sub-project adds one read tool, `Expect`. It takes a list of conditions, observes the UI on the server, evaluates the conditions with fixed rules and returns `pass`, `fail` or `unknown` per condition. With a timeout it repeats until all conditions hold. No model is involved and nothing is sent to the UI.

## Evidence (measurement 2026-10-04, sim MCS on SWENTW3, 25 states, 113 yes/no questions)

| Finding | Consequence for this design |
|---|---|
| A local model (winnow:e4b) answered 98/113 questions correctly; the agreed gate (≥ 95 % of decided answers) failed, and wrong answers came with confidence up to 0.92 | No model. Fixed rules only |
| The ground truth for all 113 questions was computed from the `Observe` JSON with one-line predicates | The same predicates are the product: window exists, element enabled/selected/value |
| "Which docking tab is visible" was at chance for the model; MCS docking `TabItem`s never report `selected` | A state the UI does not report is answered `unknown`, never guessed |
| A normal MCS screen has ~280 actionable elements; `Observe` cuts at `max_elements` | `Expect` observes with the maximum (500); when the list is still cut, "absent" cannot be proven → `unknown` |

## Goals

- One call answers one or more conditions about the current UI state.
- An answer is `pass` or `fail` only when the observation proves it; otherwise `unknown` with the reason.
- With `timeout_ms > 0` one call replaces a wait-and-observe loop.
- The 113 measured questions, expressed as conditions, yield no wrong answer (each is correct or `unknown`).

## Non-Goals

- Image comparison, OCR, pixel checks
- Any model or Ollaya integration
- An `expect` step inside `Perform` (follow-up once the tool has proven itself)
- OR/NOT combinations of conditions (each state has its explicit opposite; a caller needing OR makes two calls)
- Telling which MCS docking tab is in front (not reported by the UI; check an element that only exists in that panel instead)

---

## 1. Tool `Expect`

```
Expect(
  conditions: array,                  // 1–20 condition objects, all must hold
  timeout_ms: int = 0,                // 0 = check once; clamped 0–30000
  scope: ObserveScope = foreground,   // as Observe
  process: string? = null,            // required for scope=process
  format: OutputFormat = markdown)
```

`ReadOnly = true`, `Idempotent = true`. `conditions` arrives as raw JSON (`JsonElement`), like `Perform` steps, and is parsed by `ExpectationParser`.

### 1.1 Conditions

Each condition is an object of exactly one kind, decided by its key:

**Window** — key `window`

```json
{"window": "Login", "state": "open"}
{"window": "Options", "state": "closed"}
{"window": "Login", "state": "open", "modal": true}
```

- `state`: `open` (default) | `closed`
- `modal` (optional, only with `open`): the matched window must (not) be modal
- matched against `ObservedWindow.Title`

**Element** — key `element` (an id from `Observe`) or a selector with `type`, `name` and/or `panel`

```json
{"element": "ejsbw", "state": "enabled"}
{"type": "Button", "name": "StartProgram", "state": "enabled"}
{"type": "RadioButton", "name": "20x", "state": "selected"}
{"type": "Edit", "name": "Focus", "panel": "Microscope", "value": "90,000"}
{"type": "Button", "name": "Logout", "state": "absent"}
```

- selector fields: `type` (one of the control types an observation lists — Button, SplitButton, CheckBox, RadioButton, ComboBox, Edit, Document, Spinner, Slider, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, DataItem, HeaderItem; case-insensitive; any other type is invalid input, because it could never match), `name` (matches the element's `name` or its `label`), `panel`, `in_window` (window title); at least one of `type`/`name`/`panel` is required. `{"panel": "Errors"}` alone asks whether anything of that panel is there — the way to check which docking panel is shown (acceptance 2026-10-05: 22 of the 113 measured questions are of this kind)
- `state`: `exists` (default) | `absent` | `enabled` | `disabled` | `selected` | `not_selected` | `checked` | `unchecked` | `expanded` | `collapsed`
- `value` (equals) or `value_contains` (optional, not with `absent`): compared with the element's full value, ordinal, case-sensitive
- `state` and a value check in one condition must both hold

**Text** — key `text`

```json
{"text": "Measurement finished"}
```

- passes when a static text of the observation contains the string (case-insensitive)

**Matching:** names, panels and window titles are compared case-insensitively; `match: "exact"` (default) or `"contains"` per condition applies to `window`, `name`, `panel` and `in_window`. `text` is always a contains-match.

**Invalid input** (unknown key, state or element type, a key given twice, a blank string, no kind, two kinds, `modal` with `closed`, `value` with `absent`, more than 20 conditions, empty list) is an `[ERROR]` naming the condition index; nothing is observed.

### 1.2 Evaluation rules

A condition is evaluated against one `Observation` taken with `max_elements = 500`.

| Condition | `pass` | `fail` | `unknown` |
|---|---|---|---|
| window `open` | a window matches (and `modal` agrees) | no window matches; or `modal` disagrees | no window matches and the observation ran out of time; several windows match and only some agree with `modal` |
| window `closed` | no window matches | a window matches | no window matches and the observation ran out of time |
| element `exists` | at least one element matches | none matches and the list is complete | none matches and the list is cut |
| element `absent` | none matches and the list is complete | at least one matches | none matches and the list is cut |
| `enabled` / `disabled` | the one match has that state | it has the opposite | not found (list cut); several match |
| `selected` / `not_selected` | see below | see below | the element is a `TabItem` reporting `selected = false`; the control does not report a selection state; not found (list cut); several match |
| `checked` / `unchecked` | toggle is `on` / `off` | the opposite | the element reports no toggle state or `indeterminate`; not found (list cut); several match |
| `expanded` / `collapsed` | expand state agrees | the opposite | the element reports no expand state; not found (list cut); several match |
| `value` / `value_contains` | value agrees | value differs | password field (value never collected); the control does not report a value; not found (list cut); several match |
| text | a text contains the string | none does and the observation is not truncated | none does and the observation is truncated |

Rules behind the table:

- **Nothing was observed** (`Observation.Unobserved`, set by the tool — §1.3): every condition is `unknown` with that reason.
- **The observation ran out of time** (`Observation.BudgetExceeded`): windows reached after the budget are missing and nodes left without a hit-test are listed although they may be hidden. Every element and text condition is `unknown`; a window condition is answered only when a window matches.
- **Minimised windows** (`Observation.MinimizedWindows`, measured on Windows 11: a minimised window has no rectangle and is not in the window list): a window condition matches them — `open` passes ("open, minimised"; with `modal` it is `unknown`), `closed` fails. Their elements and texts are not observed, so a minimised window in scope makes "not found" unproven, like an unreadable one.
- **A selector's single match is only "the" element when the list is complete:** with a cut list or an unreadable or minimised window, a state or value check by selector is `unknown` (a second match cannot be ruled out); `exists`/`absent` and checks by id are not affected.

- **Not found, list complete** → `fail` for every state except `absent` (the expectation was about an element that is not there).
- **"List is cut"** means `Observation.Omitted > 0` or `Observation.Truncated`.
- **Several matches** → `unknown` with the ids of the first five; the caller narrows the selector (`panel`, `in_window`) or uses an id. Exceptions: `exists` passes and `absent` fails as soon as one matches.
- **Selection:** `Selected == true` proves `selected`. `Selected == false` proves `not_selected` only when the control answered `SelectionItem.IsSelected` (`ObservedElement.SelectionReported`) and is not a `TabItem`: docking tabs report `false` whatever is shown (measured on MCS), and a control without the pattern (a check box, a button) is `false` by default. Both `selected` and `not_selected` are `unknown` then.
- **Values:** `Observe` collects no value for an empty field and for a control without a readable value. `ObservedElement.ValueReported` (the control answered the Value property) tells them apart: an empty field counts as the empty string (`value: ""` passes), a control that reports no value is `unknown`. A password field is `unknown` (`ObservedElement.Password`). The three flags are set by the collector/`ObservationBuilder`, not emitted by `Observe` and not part of the signature.
- **An unreadable window** (`ObservedWindow.Unreadable`) still counts as a window for window conditions. An element or text condition is `unknown` when it found nothing and any window in scope is unreadable — the element may be inside it.
- **Element by id:** the id is looked up in `ObservationStore` and the element is found in the fresh observation by its locator and window (the window affinity of ids: the window it was observed in; an element of a main window is followed into another window with the same locator, an element of a transient window is not). Id strings are not compared — the id of one element can differ between observations (identical locators in two windows, prefix collisions). An id the store does not know is `unknown`; an id whose application has no window in the observed scope is `unknown`. A selector condition restricted by `in_window` matches `ObservedElement.Window`; elements of the main window (`Window == null`) match the title of the bottom-most window.

**Overall result:** `pass` when every condition passes; `fail` when at least one fails; otherwise `unknown`.

### 1.3 Waiting

- `timeout_ms = 0`: one observation, one evaluation.
- `timeout_ms > 0`: observe and evaluate; when the overall result is not `pass` and the limit is not reached, wait 200 ms (at most the time left) and repeat — the last observation is taken at the limit. The result returned is the last evaluation. An observation that has started is finished even when it runs past the deadline (it is bounded by Observe's own 5 s budget).
- Waiting stops early only on overall `pass` or cancellation — a `fail` may turn into `pass` (that is what waiting is for).
- The request's `CancellationToken` ends the wait; the cancellation is rethrown, as `App` and `Perform` do.
- **Scope during a wait** (`ScopeGuard`): `scope=foreground` is resolved anew for every observation. The first application observed is pinned; an observation of another application is `Unobserved` ("the foreground application changed during the wait") — every condition `unknown` — and the wait goes on, because the first one may come back. An observation without any window is `Unobserved` too.
- **Nothing in scope is a state, not an error** (`ObservationService.CheckScope`, asked before every observation): no foreground application window → `Unobserved`; `scope=process` and no process of that name has a visible window → an empty, complete observation, so `closed`/`absent` pass and `open`/`exists` fail. This is what makes "wait until the application has started / has closed" work.

### 1.4 Output

Markdown (default):

```
Expect: FAIL (2 of 3 passed) — MCS (pid 2116) — 1 observation, 1240 ms
pass     1 window 'Login' open — open, modal
pass     2 Button 'OK' enabled — enabled (id k3f9a)
fail     3 Edit 'User' value 'admin' — value is '' (id p01zq)
```

```
Expect: UNKNOWN (1 of 2 passed) — MCS (pid 2116) — 4 observations, 3050 ms (timeout)
pass     1 window 'MCS' open — open
unknown  2 TabItem 'Camera' selected — a TabItem does not report its selection reliably (id ejsbw)
```

JSON (`format=json`, via `ToolHelpers.JsonResult`):

```
{result: "pass"|"fail"|"unknown", passed: int, total: int, observed: [{process: string, pid: int}], observations: int, elapsed_ms: int, timed_out: bool,
 conditions: [{index: int, result: "pass"|"fail"|"unknown", condition: string, actual: string, elements?: [string]}]}
```

- `condition` is the condition rendered as one line, `actual` what was found or why it is unknown, `elements` the ids of the matched elements (at most five).
- Values in `actual` are clipped like in `Observe` (`ObservationFormatter` limits); the comparison uses the full value.
- A `fail` or `unknown` result is **not** a tool error (`IsError = false`): the tool did its job. `[ERROR]` is reserved for invalid input, a missing `process` and cancellation.
- The header (and `observed`) names the processes whose windows the last observation saw — what the answers are about; `nothing observed` when there was none.
- Only the last observation is remembered in `ObservationStore` (each remembered one pushes an older one out), so the ids in the result can be passed to `Click`/`Type` right away and a long wait does not cost the caller its earlier ids.

---

## 2. Components

| Unit | Kind | Responsibility |
|---|---|---|
| `Models/Expectation.cs` | records/enums | `Expectation` (window / element / text variants), `ExpectState`, `ExpectResult` (`Pass`, `Fail`, `Unknown`), `ConditionOutcome(Index, Result, Condition, Actual, ElementIds)`, `ExpectOutcome` |
| `Services/ExpectationParser.cs` | pure | `JsonElement` → `IReadOnlyList<Expectation>`; all input validation, errors name the condition index |
| `Services/ExpectationEvaluator.cs` | pure | `Evaluate(Observation, IReadOnlyList<Expectation>)` → outcomes; implements §1.2 and nothing else |
| `Services/ExpectationFormatter.cs` | pure | markdown and JSON envelope (§1.4) |
| `Tools/ExpectTools.cs` | tool | parameter clamping, the wait loop (§1.3), `store.Remember`, error wrapping |

- The wait loop is a static method taking `Func<Observation> observe`, `TimeProvider` and a delay function, so it is unit-tested without UIA and without real time.
- `ObservationService`, `ObservationStore` and the action path are not changed; `ObservationBuilder` only copies the password flag onto the element.
- The tool count becomes 22: `CLAUDE.md`, `README.md`, the server instructions (`McpServerSetup`) and the parity schema test are updated.

## 3. Error handling

- Tool body in try/catch → `ToolHelpers.ErrorResult` (existing convention).
- Parser errors: `[ERROR] ArgumentException: condition 2: unknown state 'visible' (allowed: exists, absent, …)`.
- `scope=process` without `process`: `[ERROR] ArgumentException`.
- Nothing in scope is not an error (§1.3). Any other exception of an observation ends the call with that error; it is not swallowed and retried.

## 4. Testing

**Unit (pure, synthetic observations):**
- parser: every kind, defaults, every invalid-input case
- evaluator: every cell of the table in §1.2, including list cut, several matches, `TabItem` selection, password value, unreadable window, `in_window` for main-window elements, `match=contains`, case-insensitive names, case-sensitive values
- formatter: markdown lines, JSON shape, clipping
- wait loop: passes on first observation; passes on the third; times out with the last evaluation; `fail` does not stop the wait; cancellation

**Parity (desktop session, Notepad):** schema entry for `Expect`; one functional test — window open, text field value after `Type`, wait for a window that opens.

**Acceptance (sim MCS on SWENTW3, `-c E:\ollaya\mcs-sim-config`):**
1. The 113 questions of the 2026-10-04 measurement are rewritten as conditions and evaluated against the 25 stored state files with the real evaluator (offline harness in the scratchpad; the stored JSON is mapped to `Observation`). Pass criterion: 0 wrong answers; the `unknown` count is reported with reasons.
2. Live: `Expect` with `timeout_ms` waits for the Login dialog opened by a click; `Expect` on a covered docking tab returns `unknown` for `selected`; a `value` check on the focus-axis field passes.

## 5. Open points for the plan

- Resolved by the branch review (2026-10-05): `SelectionReported`, `ValueReported` and `BudgetExceeded` were added; the `TabItem` rule stays on top of `SelectionReported`.
- Known limits, documented in the tool description: elements without a name and a value are not observed, so a type-and-panel selector can miss icon-only buttons; `enabled` is what UI Automation reports — controls of a window disabled by a modal dialog may still report enabled.
