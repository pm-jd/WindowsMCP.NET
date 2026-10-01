# WindowsMCP.NET

Windows desktop automation MCP server for Claude Code. Provides 21 tools for UI automation, file system operations, and system management on remote Windows machines.

## Build & Test

```bash
# Build (use Release — Debug exe may be locked by running instance)
dotnet build src/WindowsMCP.NET -c Release

# Unit tests (352 tests). Test projects run on Microsoft.Testing.Platform (xunit v3, see global.json);
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
- **Verified element actions**: element-id actions (`ActionExecutor`) try UIA patterns first and fall back to the mouse where safe; success is judged by the signature of the element's own process (visible top-level windows, hash of actionable elements) before vs. after the action, giving `effect`
- **Cancellation & progress**: async tools take a `CancellationToken` (bound to the client's request; PowerShell/Notification kill their child process on cancel); `Perform` reports one progress notification per step via `IProgress<ProgressNotificationValue>`

## Tools (21)

| Tool | Purpose |
|------|---------|
| **Context** | Get system state (active window, screenshot, UI tree, clipboard, processes) |
| **Observe** | Compact state of the foreground app (windows, focus, actionable elements with stable ids, optional JPEG) for precise interaction |
| **Perform** | Execute batched UI action chains (click, type, shortcut, scroll, move, wait); steps accept `element` ids, report `effect`, stop after 3 element steps without visible change |
| **Snapshot** | Capture screenshot + build UI element tree with numbered labels |
| **Screenshot** | Fast screenshot without rebuilding UI tree |
| **Click** | Click at coordinates, a labeled UI element or an `Observe` element id (`element`; pattern-first, reports `effect`) |
| **Type** | Type text with optional target click or `Observe` element id (`element`; value-verified); `\n` is sent as Enter, `\t` as Tab |
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

- **Observe → act by element id**: `Observe` returns stable ids; pass them as `element` to Click/Type/Perform/MultiSelect/MultiEdit. Actions try UIA patterns first, fall back to the mouse where safe, and report `effect` (changed/unchanged/value_verified/value_mismatch/not_verified). `Perform` stops after 3 element steps without visible change (`stop_on_stall`).
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
                   # ObservationService (UIA collection), ObservationBuilder (pure filter/ids/signature), ObservationStore (id lookup), ActionExecutor (pattern-first actions)
  Native/          # P/Invoke: User32, Kernel32; InputFactory (shared SendInput builders, text→keystrokes)
  Models/          # WindowInfo, UiElementNode, AnnotatedTree, Observation, ObservedNode, ElementLocator
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
