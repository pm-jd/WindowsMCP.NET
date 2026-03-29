# WindowsMCP.NET Parity Testing — Design Specification

**Date:** 2026-03-29
**Status:** Approved
**Depends on:** WindowsMCP.NET implementation, Python Windows-MCP (`uvx windows-mcp`)

## Overview

A test suite that verifies WindowsMCP.NET behaves identically to the Python-based Windows-MCP reference implementation. Tests run sequentially: first against the Python server to capture a baseline, then against the C# server to compare. Two phases: schema compatibility (API surface) and functional parity (actual behavior).

## Goals

- Verify 1:1 API compatibility (tool names, parameter signatures, annotations)
- Verify functional parity (tools produce the same effects on the desktop)
- Generate a machine-readable schema diff report to guide C# implementation fixes
- Produce reusable regression tests for the C# server

## Non-Goals

- Pixel-accurate screenshot comparison (too fragile)
- Performance benchmarking (separate concern)
- Notification tool testing (toast disappears, hard to verify)
- MultiSelect/MultiEdit testing (requires complex test app)
- Scrape testing against live URLs (unstable)
- Headless CI execution for functional tests (requires desktop)

---

## Architecture

```
tests/WindowsMCP.NET.ParityTests/
├── WindowsMCP.NET.ParityTests.csproj
├── Infrastructure/
│   ├── McpServerFixture.cs         — Start/stop MCP server (Python or C#) via stdio
│   ├── McpTestClient.cs            — Thin wrapper around McpClient for test readability
│   └── SchemaComparer.cs           — Compare tool schemas from two servers
├── Phase1_SchemaTests/
│   └── ToolSchemaParityTests.cs    — Automated schema diff for all 18 tools
├── Phase2_FunctionalTests/
│   ├── InputToolsParityTests.cs    — Click, Type, Scroll, Move, Shortcut, Wait
│   ├── SnapshotToolsParityTests.cs — Snapshot, Screenshot
│   ├── AppToolsParityTests.cs      — App launch/switch/resize
│   ├── SystemToolsParityTests.cs   — PowerShell, Process, Registry
│   ├── ClipboardToolsParityTests.cs — Clipboard get/set
│   └── FileSystemToolsParityTests.cs — FileSystem 8 modes
└── TestData/
    └── baseline/                    — Stored Python server baseline results (JSON)
```

### Dependencies

- `ModelContextProtocol` — MCP client SDK (`McpClient`, `StdioClientTransport`)
- `xunit` — Test framework
- `FlaUI.UIA3` — UI Automation for verifying desktop state in functional tests
- `System.Text.Json` — Schema comparison and baseline serialization

---

## Infrastructure

### McpServerFixture

Manages the lifecycle of an MCP server process and provides a connected `McpClient`.

**Configuration via environment variables:**

| Variable | Values | Default |
|----------|--------|---------|
| `PARITY_SERVER` | `python`, `dotnet` | `dotnet` |
| `PYTHON_MCP_CMD` | Command to start Python server | `uvx windows-mcp` |
| `DOTNET_MCP_PATH` | Path to WindowsMCP.NET.exe | `src/WindowsMCP.NET/bin/Debug/.../WindowsMCP.NET.exe` |

**Lifecycle:**
1. Start server as child process with stdio transport
2. Connect `McpClient` via `StdioClientTransport`
3. Cache `tools/list` response
4. Tests run against connected client
5. Teardown: disconnect client, terminate process

**Transport:** stdio for both servers. Avoids certificate/API key setup. Both servers support stdio natively. The MCP C# client has first-class stdio support.

### McpTestClient

Thin wrapper for readable test code:

- `CallToolAsync(string toolName, Dictionary<string, object?> args) → JsonElement`
- `ListToolsAsync() → List<McpClientTool>`
- `GetToolSchemaAsync(string toolName) → JsonElement`

### Test Execution (Sequential)

```bash
# Phase 1: Schema diff
PARITY_SERVER=python dotnet test --filter "Category=Schema"
# → Captures Python schema to TestData/baseline/python_schema.json

PARITY_SERVER=dotnet dotnet test --filter "Category=Schema"
# → Compares C# schema against Python baseline, generates diff report

# Phase 2: Functional tests
PARITY_SERVER=python dotnet test --filter "Category=Functional"
# → Captures baseline results to TestData/baseline/

PARITY_SERVER=dotnet dotnet test --filter "Category=Functional"
# → Compares against stored baseline
```

---

## Phase 1: Schema Parity Tests

### ToolSchemaParityTests

Connects to the configured server, retrieves `tools/list`, and verifies against baseline.

**Per tool (all 18), checks:**

1. **Tool exists** — Name present in `tools/list`
2. **Parameter names** — Identical parameter set
3. **Parameter types** — Same JSON Schema types (string, integer, boolean, array, object)
4. **Default values** — Same defaults where defined
5. **Required/Optional** — Same required fields
6. **Annotations** — `readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint` match
7. **Description** — Present (content may differ, only check existence)

### SchemaComparer Output

Generates `TestData/schema-diff.json`:

```json
{
  "timestamp": "2026-03-29T12:00:00Z",
  "pythonServer": "windows-mcp 0.7.0",
  "dotnetServer": "WindowsMCP.NET 0.1.0",
  "tools": [
    {
      "name": "Click",
      "status": "mismatch",
      "differences": [
        { "field": "parameter:loc", "python": "array of int", "dotnet": "missing (has x, y instead)" },
        { "field": "parameter:clicks", "python": "integer, default=1", "dotnet": "missing (has double bool)" }
      ]
    },
    {
      "name": "Wait",
      "status": "match",
      "differences": []
    }
  ],
  "summary": { "total": 18, "match": 12, "mismatch": 6 }
}
```

**Tests FAIL as long as mismatches exist.** This is intentional — the diff report drives fixes to the C# implementation. Once all 18 tools are schema-compatible, Phase 1 tests go green.

---

## Phase 2: Functional Parity Tests

### Baseline Mode

When `PARITY_SERVER=python`, functional tests run against the Python server and save results as JSON in `TestData/baseline/`. Baseline files are overwritten on each Python run (always reflects latest Python server behavior). When `PARITY_SERVER=dotnet`, tests run against the C# server and compare against the stored baseline. If no baseline exists, the test is skipped with a warning (not failed).

### Comparison Tolerances

- Timestamps, PIDs, handles: ignored
- Screenshot dimensions: must match; pixel content: ignored
- Text output structure: must match; dynamic values (process names, memory sizes): ignored
- File paths: normalized before comparison

### Test Scenarios

#### Input Tools (Notepad as controlled environment)

| Test | Setup | Action | Verification |
|------|-------|--------|-------------|
| Click | Open Notepad, determine text area position | `Click(loc: [x, y])` | FlaUI: text area has focus |
| Type | Open Notepad, empty document | `Type(text: "Hello World", loc: [x, y])` | FlaUI: Notepad text content == "Hello World" |
| Type with clear | Notepad with existing text | `Type(text: "New", loc: [x, y], clear: true)` | FlaUI: text == "New" (old text gone) |
| Shortcut | Notepad with text | `Shortcut(shortcut: "ctrl+a")` | FlaUI: text is selected |
| Scroll | Notepad with long text | `Scroll(loc: [x, y], direction: "down")` | Scroll position changed (via UIA scroll pattern) |
| Move | Empty desktop | `Move(loc: [500, 500])` | Win32 `GetCursorPos` == (500, 500) |
| Wait | — | `Wait(duration: 1.0)` | Elapsed time >= 1.0 seconds |

#### System Tools (String/Structure comparison)

| Test | Action | Verification |
|------|--------|-------------|
| PowerShell echo | `PowerShell(command: "Write-Output 'hello'")` | Output contains "hello" |
| PowerShell error | `PowerShell(command: "Get-Item nonexistent")` | Output contains error text |
| Process list | `Process(mode: "list")` | Contains running processes, table/structured format |
| Registry get | `Registry(mode: "get", path: "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", name: "ProductName")` | Contains "Windows" |
| FileSystem write+read | `FileSystem(mode: "write", ...)` then `FileSystem(mode: "read", ...)` | Read result == written content |
| FileSystem list | `FileSystem(mode: "list", path: tempDir)` | Lists known files created in setup |

#### Screen Tools (Image validation)

| Test | Action | Verification |
|------|--------|-------------|
| Screenshot | `Screenshot()` | Result contains valid PNG (magic bytes), dimensions == screen resolution |
| Snapshot | `Snapshot()` | PNG present + UI tree text is non-empty, contains numbered labels |
| Snapshot tree format | `Snapshot(useDom: true, useVision: false)` | Tree text contains ControlType entries and label numbers |

#### Clipboard & App Tools

| Test | Action | Verification |
|------|--------|-------------|
| Clipboard roundtrip | `Clipboard(mode: "set", text: "test123")` then `Clipboard(mode: "get")` | Get result == "test123" |
| App launch | `App(mode: "launch", name: "notepad")` | Process running, window visible (via FlaUI) |
| App switch | Open two windows, `App(mode: "switch", name: ...)` | Foreground window changed |
| App resize | Open Notepad, `App(mode: "resize", ...)` | Window rect matches requested dimensions |

---

## Phase 3: CI Integration

The functional tests (Phase 2) run against the C# server only (no baseline comparison) as regression tests in the existing GitHub Actions CI pipeline. These tests require a Windows desktop and are marked with `[Trait("Category", "Desktop")]` so they can be excluded in headless CI environments.

Schema tests (Phase 1) are only meaningful when comparing two servers and are excluded from regular CI. They run on-demand during development.

---

## Workflow: Fixing API Mismatches

The schema diff report from Phase 1 produces a concrete list of fixes needed in the C# implementation. The expected fixes based on the code review include:

1. **Click:** Change `x`/`y` back to `loc: int[]`, restore `clicks: int` parameter
2. **Type:** Add missing `clear: bool`, `pressEnter: bool` parameters
3. **Move:** Add missing `drag: bool` parameter
4. **Scroll:** Add missing `type: string` parameter for horizontal scroll, rename to match Python
5. **Screenshot:** Add missing `useAnnotation: bool` parameter
6. **Scrape:** Add missing `query: string?` parameter
7. **Process:** Add missing `sortBy`, `limit`, `force` parameters
8. **MultiSelect/MultiEdit:** Align parameter types with Python (arrays instead of comma-separated strings)
9. **Snapshot:** Add `useVision`/`useDom` parameters

These fixes are implemented iteratively: fix → run Phase 1 → check diff → repeat until green.

---

## Prerequisites

- Python Windows-MCP installed: `uvx windows-mcp` available on PATH
- WindowsMCP.NET built: `dotnet build` passing
- Windows desktop available (not headless)
- Notepad available (Windows standard)
