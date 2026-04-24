# WindowsMCP.NET

Windows desktop automation MCP server for Claude Code. Provides 20 tools for UI automation, file system operations, and system management on remote Windows machines.

## Build & Test

```bash
# Build (use Release — Debug exe may be locked by running instance)
dotnet build src/WindowsMCP.NET -c Release

# Unit tests (84 tests)
dotnet test tests/WindowsMCP.NET.Tests -c Release -v q

# Parity tests (44 tests: 21 schema + 23 functional — requires desktop session)
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

- **Transport**: HTTP (remote, default) or stdio (local Claude Code)
- **Tools**: Static classes in `src/WindowsMCP.NET/Tools/` with `[McpServerTool]` attribute
- **Services**: Singletons injected as tool method parameters (`DesktopService`, `UiTreeService`, `ScreenCaptureService`)
- **Error handling**: All tools wrap their body in try-catch, returning `[ERROR] ExceptionType: message` instead of throwing (MCP SDK swallows raw exceptions)
- **Security**: API key auth (Bearer token), optional IP allowlist, optional HTTPS

## Tools (20)

| Tool | Purpose |
|------|---------|
| **Context** | Get system state (active window, screenshot, UI tree, clipboard, processes) |
| **Perform** | Execute batched UI action chains (click, type, shortcut, scroll, move, wait) |
| **Snapshot** | Capture screenshot + build UI element tree with numbered labels |
| **Screenshot** | Fast screenshot without rebuilding UI tree |
| **Click** | Click at coordinates or labeled UI element |
| **Type** | Type text with optional target click |
| **Shortcut** | Send keyboard shortcuts (ctrl+c, alt+f4, etc.) |
| **Scroll** | Scroll mouse wheel |
| **Move** | Move cursor, optional drag |
| **Wait** | Pause execution (max 10s) |
| **MultiSelect** | Click multiple UI elements sequentially |
| **MultiEdit** | Fill multiple form fields |
| **FileSystem** | File ops: read, write, copy, move, delete, list, search, info, read_base64, write_base64. `list/search/info` support `format=json` and `offset/limit` pagination (default limit 200) |
| **PowerShell** | Execute PowerShell commands |
| **Process** | List or kill processes. `list` supports `format=json`, `offset/limit/has_more` pagination |
| **Registry** | Read/write/delete/list Windows registry. `get/list` support `format=json`; `list` paginates per section (subkeys+values, default limit 200) |
| **App** | Launch, focus, check, or resize windows. Modes: launch, ensure, status, switch, resize. `status` supports `format=json` returning all matches |
| **Clipboard** | Get or set clipboard text |
| **Notification** | Show Windows toast notifications (title/message passed via env vars — injection-safe) |
| **Scrape** | Fetch URL and convert HTML to Markdown |

## Key Patterns

- **Efficient UI workflow**: Use `Context` to get state, then `Perform` to batch actions (2 calls instead of 5+)
- **Label-based interaction**: `Snapshot` assigns numbered labels to UI elements; `Click`/`Type` reference labels
- **Binary file transfer**: Use `read_base64`/`write_base64` for cross-machine binary file operations (1MB limit)
- **Foreground without screenshots**: Use `App(mode="ensure", name="notepad")` to focus an app if running, launch if not — one call instead of screenshot+parse workflow
- **Structured output**: Pass `format="json"` to `App.status`, `FileSystem.list/search/info`, `Process.list`, `Registry.get/list` to get parseable JSON envelopes instead of human-readable text — useful when chaining tool output into agent logic
- **Pagination**: List tools (`FileSystem.list/search`, `Process.list`, `Registry.list`) accept `offset`/`limit` and report `has_more`/`next_offset` so agents can page through large result sets
- **Helpers**: `ToolHelpers.cs` centralizes pagination (`Paginate`), JSON serialization options, and format-detection — reuse instead of duplicating per-tool

## Project Structure

```
src/WindowsMCP.NET/
  Tools/           # MCP tool implementations (static classes)
  Services/        # Singletons: DesktopService, ScreenCaptureService, UiTreeService, UiAutomationService
  Native/          # P/Invoke: User32, Kernel32
  Models/          # WindowInfo, UiElementNode, AnnotatedTree
  Config/          # AppConfig, CliParser, ConfigManager
  Setup/           # TrayIcon, UpdateChecker, CertificateGenerator, SetupWizard
  Security/        # ApiKeyMiddleware, IpAllowlistMiddleware
tests/
  WindowsMCP.NET.Tests/         # Unit tests
  WindowsMCP.NET.ParityTests/   # Integration/schema parity tests
```

## CI/CD

- GitHub Actions on push to master: build, test, publish, create GitHub release
- CalVer versioning: `YYYY.MM.patch` (auto-incremented)
- PAT injection via MSBuild: `-p:GitHubPat=<token>` replaces `%%GITHUB_PAT%%` in UpdateChecker.cs
