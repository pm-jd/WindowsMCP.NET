# WindowsMCP.NET — Design Specification

**Date:** 2026-03-29
**Status:** Approved
**License:** MIT

## Overview

WindowsMCP.NET is a .NET C# reimplementation of the Python-based [Windows-MCP](https://github.com/CursorTouch/Windows-MCP) server. It provides 18 MCP tools for full Windows desktop automation, with a focus on remote operation via Streamable HTTP. It ships as a portable, self-contained single-file executable.

## Goals

- 1:1 feature parity with the Python Windows-MCP (v0.7.0, 18 tools)
- Enhanced UI Automation via FlaUI (UIA3)
- Remote-first: Streamable HTTP as primary transport with authentication and HTTPS
- Portable: Native AOT single-file `.exe`, no .NET Runtime required
- Easy deployment: Interactive first-run setup wizard, 3-step deployment
- GitHub-hosted, MIT-licensed

## Non-Goals

- Telemetry/Analytics (deferred)
- Remote proxy mode (as in Python original's sandbox mode)
- Plugin system
- Cross-platform (Windows-only)

---

## Architecture

```
WindowsMCP.NET.exe (single file, Native AOT, ~30-50 MB)
│
├── Transport: Streamable HTTP (primary) + stdio (secondary)
├── Security: API-Key + Auto-HTTPS + IP-Allowlist
├── Setup: Interactive First-Run Wizard
│
├── Tools/ (18 tools, 9 classes)
├── Services/ (4 singleton services via DI)
├── Native/ (P/Invoke & COM interop)
├── Models/ (shared DTOs / records)
└── config.json (beside .exe, portable)
```

### Project Structure

```
WindowsMCP.NET/
├── .github/
│   └── workflows/
│       └── build.yml
├── src/
│   └── WindowsMCP.NET/
│       ├── Program.cs
│       ├── WindowsMCP.NET.csproj
│       ├── Tools/
│       │   ├── InputTools.cs
│       │   ├── SnapshotTools.cs
│       │   ├── AppTools.cs
│       │   ├── SystemTools.cs
│       │   ├── ClipboardTools.cs
│       │   ├── FileSystemTools.cs
│       │   ├── NotificationTools.cs
│       │   ├── MultiTools.cs
│       │   └── ScrapeTools.cs
│       ├── Services/
│       │   ├── DesktopService.cs
│       │   ├── ScreenCaptureService.cs
│       │   ├── UiAutomationService.cs
│       │   └── UiTreeService.cs
│       ├── Native/
│       │   ├── User32.cs
│       │   ├── Kernel32.cs
│       │   ├── Shell32.cs
│       │   └── Dxgi.cs
│       └── Models/
├── WindowsMCP.NET.sln
├── Directory.Build.props
├── LICENSE
├── README.md
└── .gitignore
```

---

## Technology Stack

| Component | Choice | Reason |
|-----------|--------|--------|
| Runtime | .NET 9.0 (`net9.0-windows`) | Latest, best AOT support |
| MCP SDK | `ModelContextProtocol` 1.2.x | Official C# SDK, AOT-verified |
| HTTP Transport | `ModelContextProtocol.AspNetCore` 1.2.x | Streamable HTTP via Kestrel |
| UI Automation | `FlaUI.UIA3` | Mature .NET UIA3 wrapper, MIT |
| Fuzzy Matching | `FuzzySharp` | Pure .NET, AOT-compatible |
| HTML→Markdown | `ReverseMarkdown` | Pure .NET, AOT-compatible |
| Screenshots (primary) | DXGI Desktop Duplication API | GPU-based, high performance |
| Screenshots (fallback) | `System.Drawing.Common` (GDI+) | Broad compatibility |
| Publishing | Native AOT, single-file, self-contained | Portable .exe |

---

## Tools

### InputTools.cs — 6 Tools

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **Click** | `loc: int[]?`, `label: string?`, `button: string = "left"`, `clicks: int = 1` | Destructive, OpenWorld | Mouse click at coordinates or UI label |
| **Type** | `text: string`, `loc: int[]?`, `label: string?`, `clear: bool = false`, `pressEnter: bool = false` | Destructive, OpenWorld | Type text at location |
| **Scroll** | `loc: int[]?`, `direction: string = "down"`, `type: string = "vertical"`, `wheelTimes: int = 3` | Destructive | Scroll content |
| **Move** | `loc: int[]?`, `label: string?`, `drag: bool = false` | Destructive | Move mouse, optional drag |
| **Shortcut** | `shortcut: string` | Destructive, OpenWorld | Execute keyboard shortcut (e.g. "ctrl+c") |
| **Wait** | `duration: double = 1.0` | ReadOnly, Idempotent | Pause in seconds |

Label resolution: When `label` is provided instead of `loc`, coordinates are resolved via `UiTreeService.ResolveLabel()`. Mouse/keyboard input via `user32.dll` P/Invoke (`SendInput`).

### SnapshotTools.cs — 2 Tools

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **Snapshot** | `useVision: bool = true`, `useDom: bool = true`, `useAnnotation: bool = true`, `display: int[]?` | ReadOnly, Idempotent | Screenshot + UI tree + annotated labels |
| **Screenshot** | `useAnnotation: bool = true`, `display: int[]?` | ReadOnly, Idempotent | Fast screenshot only (no UI tree) |

Returns: Screenshot as Base64 `ImageContent` + optional UI tree as `TextContent`.

### AppTools.cs — 1 Tool

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **App** | `mode: string`, `name: string`, `windowLoc: int[]?`, `windowSize: int[]?` | Destructive, OpenWorld | Launch (`launch`), switch (`switch`), resize (`resize`) |

### SystemTools.cs — 3 Tools

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **PowerShell** | `command: string`, `timeout: int = 30` | Destructive, OpenWorld | Execute PowerShell command |
| **Process** | `mode: string`, `name: string?`, `pid: int?`, `sortBy: string = "memory"`, `limit: int = 20`, `force: bool = false` | mode-dependent | List or kill processes |
| **Registry** | `mode: string`, `path: string`, `name: string?`, `value: string?`, `type: string = "String"` | mode-dependent | Read/write/delete/list registry entries |

### ClipboardTools.cs — 1 Tool

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **Clipboard** | `mode: string = "get"`, `text: string?` | mode-dependent | Get or set clipboard content |

### FileSystemTools.cs — 1 Tool

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **FileSystem** | `mode: string`, `path: string`, `destination: string?`, `content: string?`, `pattern: string?`, `recursive: bool = false`, `append: bool = false`, `overwrite: bool = false` | mode-dependent | File operations: read, write, copy, move, delete, list, search, info |

### NotificationTools.cs — 1 Tool

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **Notification** | `title: string`, `message: string` | Destructive, OpenWorld | Send Windows toast notification |

### MultiTools.cs — 2 Tools

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **MultiSelect** | `locs: int[][]?`, `labels: string[]?`, `pressCtrl: bool = true` | Destructive | Select multiple UI elements |
| **MultiEdit** | `locs: object[]?`, `labels: object[]?` | Destructive | Edit multiple input fields at once |

### ScrapeTools.cs — 1 Tool

| Tool | Parameters | Annotations | Description |
|------|-----------|-------------|-------------|
| **Scrape** | `url: string`, `query: string?` | ReadOnly, OpenWorld | Fetch web page, convert HTML to Markdown |

---

## Services

### ScreenCaptureService (Singleton)

- **Primary backend:** DXGI Desktop Duplication API via P/Invoke
- **Fallback backend:** GDI+ `Graphics.CopyFromScreen`
- **API:** `CaptureScreen(int? displayIndex = null) → byte[]` (PNG bytes)
- **Features:** Multi-monitor support, automatic fallback on error, optional annotation overlay (numbered labels on interactive elements)

### UiAutomationService (Singleton)

- **Based on:** FlaUI.UIA3
- **API:**
  - `GetDesktopTree() → UiElementNode` — Full desktop UI tree
  - `FindElement(label: string) → UiElement?` — Find element by label/name (fuzzy matching via FuzzySharp, Levenshtein distance)
  - `FindElements(automationId/name/controlType) → List<UiElement>` — Targeted search
  - `GetClickablePoint(element) → Point` — Get clickable point for element
  - `GetElementProperties(element) → Dictionary` — Read all properties

### UiTreeService (Singleton)

- **Purpose:** Caches UI tree, assigns numbered labels to interactive elements
- **API:**
  - `BuildAnnotatedTree() → AnnotatedTree` — Build UI tree with labels
  - `ResolveLabel(label: string) → UiElement?` — Resolve label to element
  - `InvalidateCache()` — Invalidate on focus change
- **Caching:** Time-based (2 second TTL), rebuilt on Snapshot calls

### DesktopService (Singleton)

- **Purpose:** Window management via Win32 API
- **API:**
  - `LaunchApp(name) → WindowInfo`
  - `SwitchToWindow(name/handle)`
  - `ResizeWindow(handle, x, y, width, height)`
  - `GetForegroundWindow() → WindowInfo`
  - `ListWindows() → List<WindowInfo>`
- **Implementation:** `user32.dll` P/Invoke (`FindWindow`, `SetForegroundWindow`, `MoveWindow`, `EnumWindows`, etc.)

---

## Native Layer

All P/Invoke signatures use `[LibraryImport]` (source-generated) for AOT compatibility. COM interop via `[GeneratedComInterface]` where possible.

### Native/User32.cs
- Mouse: `SetCursorPos`, `SendInput` (MOUSEINPUT)
- Keyboard: `SendInput` (KEYBDINPUT)
- Windows: `FindWindow`, `SetForegroundWindow`, `MoveWindow`, `GetWindowRect`, `EnumWindows`, `GetWindowText`, `ShowWindow`
- Clipboard: `OpenClipboard`, `GetClipboardData`, `SetClipboardData`, `CloseClipboard`

### Native/Kernel32.cs
- `CreateProcess`, `GetCurrentThreadId`

### Native/Shell32.cs
- Toast notifications via COM interop or PowerShell fallback

### Native/Dxgi.cs
- DXGI Desktop Duplication: `IDXGIFactory1`, `IDXGIAdapter1`, `IDXGIOutput1`, `IDXGIOutputDuplication`
- Direct3D 11: `ID3D11Device`, `ID3D11DeviceContext`
- Frame capture → Bitmap → PNG bytes
- Multi-monitor via `EnumOutputs`

---

## Transport & Remote Operation

### Dual Transport
- **Primary:** Streamable HTTP via ASP.NET Core Kestrel
- **Secondary:** stdio for local usage

### Security (HTTP mode)
- **API-Key authentication:** Bearer token via `Authorization` header. Key set via env variable `WMCP_API_KEY` or config. HTTP mode refuses to start without a configured key.
- **HTTPS:** Kestrel with TLS certificate (auto-generated self-signed or user-provided). Configurable via `--cert` / `--cert-password` or config.
- **IP allowlist (optional):** `--allow-ip` to restrict access to specific IPs.
- **Rate limiting:** Basic rate limiting via ASP.NET Core middleware.

### Claude Code Remote Integration
```json
{
  "mcpServers": {
    "windows-mcp-dotnet": {
      "type": "streamable-http",
      "url": "https://192.168.1.100:8000/mcp",
      "headers": {
        "Authorization": "Bearer wmcp_a3f8k2...x9m1"
      }
    }
  }
}
```

---

## Setup & Deployment

### First-Run Wizard
On first start without configuration, an interactive setup runs:
1. Auto-generates API key (prefix: `wmcp_`)
2. Offers self-signed certificate generation (via `System.Security.Cryptography.X509Certificates`, no OpenSSL needed)
3. Configures port
4. Saves to `config.json` beside the `.exe`
5. Displays ready-to-paste Claude Code JSON snippet (with hostname + API key)

### config.json
```json
{
  "transport": "http",
  "host": "0.0.0.0",
  "port": 8000,
  "apiKey": "wmcp_a3f8k2...x9m1",
  "https": {
    "enabled": true,
    "certPath": "cert.pfx",
    "certPassword": "..."
  },
  "allowedIps": []
}
```

Location: Beside the `.exe` (portable). Certificate password is stored using DPAPI (`ProtectedData`) so it is encrypted at rest and only readable by the same Windows user account.

### CLI Commands
```bash
WindowsMCP.NET.exe              # Start server (runs wizard on first start)
WindowsMCP.NET.exe setup        # Re-run setup wizard
WindowsMCP.NET.exe setup --new-key    # Generate new API key
WindowsMCP.NET.exe setup --new-cert   # Generate new certificate
WindowsMCP.NET.exe info         # Show Claude Code config snippet
WindowsMCP.NET.exe --transport stdio  # Start in stdio mode
```

### CLI Arguments
```
WindowsMCP.NET.exe [OPTIONS]

Transport:
  --transport <stdio|http>      Transport mode (default: http)
  --host <host>                 Bind address (default: 0.0.0.0)
  --port <port>                 Port (default: 8000)

Security (HTTP mode):
  --api-key <key>               API key (alt: WMCP_API_KEY env)
  --cert <path>                 TLS certificate path (.pfx)
  --cert-password <pw>          Certificate password
  --allow-ip <ip>               Allowed IPs (repeatable)

General:
  --log-level <level>           Log level (default: Information)
  --version                     Show version
  --help                        Show help
```

### Deployment: 3 Steps
1. **Copy** the `.exe` to the target machine
2. **Run** it (setup wizard runs automatically)
3. **Paste** the displayed JSON snippet into Claude Code settings

### Self-Signed Certificate
- Generated via `System.Security.Cryptography.X509Certificates`
- Subject: `CN=WindowsMCP.NET`
- SAN: Hostname + all local IPs (auto-detected)
- Validity: 2 years
- Saved as `.pfx` beside config

---

## Error Handling

- Every tool catches exceptions and returns MCP-conformant error messages
- Actionable messages: e.g. `"Element with label '5' not found. Run 'Snapshot' first to get current labels."`
- Timeout handling for PowerShell commands and Scrape
- Graceful degradation: Screenshot falls back from DXGI to GDI+ transparently

## Return Formats

- **Screenshots:** `ImageContent` with Base64-encoded PNG
- **Text results:** `TextContent` with structured text (Markdown tables for process lists, JSON for registry values, etc.)
- **Snapshot:** Combined `ImageContent` (screenshot) + `TextContent` (UI tree with labels)
- **Scrape:** `TextContent` with Markdown-converted HTML

---

## AOT Considerations

| Dependency | AOT Status | Mitigation |
|-----------|-----------|-----------|
| `ModelContextProtocol` | Verified AOT-compatible | None needed |
| `ModelContextProtocol.AspNetCore` | ASP.NET Core supports AOT | None needed |
| `FlaUI.UIA3` | Partial — COM-based | Trimming directives (`rd.xml`); fallback: direct UIA3 COM via `[GeneratedComInterface]` |
| `FuzzySharp` | Pure .NET | None needed |
| `ReverseMarkdown` | Pure .NET | None needed |
| `System.Drawing.Common` | AOT-compatible on Windows | None needed |

If FlaUI causes AOT issues, fallback strategy: direct UIA3 COM access via custom `[GeneratedComInterface]` declarations.

---

## Logging

- Via `Microsoft.Extensions.Logging`
- stdio mode: Logs to **stderr** (stdout reserved for MCP protocol)
- HTTP mode: Logs to console (configurable)
- Log level configurable via `--log-level` or config
