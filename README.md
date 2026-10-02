# WindowsMCP.NET

A .NET MCP (Model Context Protocol) server for Windows desktop automation. Remote-first with Streamable HTTP transport, ships as a portable single-file executable.

Inspired by [Windows-MCP](https://github.com/CursorTouch/Windows-MCP) (Python), reimplemented in C# with enhanced UI Automation via [FlaUI](https://github.com/FlaUI/FlaUI).

## Features

- **21 MCP Tools** for full Windows desktop control
- **Remote-first** — Streamable HTTP with API key auth and HTTPS
- **Portable** — Single `.exe`, no .NET runtime needed
- **UI Automation** — FlaUI (UIA3) for reliable element interaction
- **Verified actions** — `Observe` returns element ids; actions on them report whether the UI changed
- **Easy Setup** — Interactive wizard, 3-step deployment

## Quick Start

### 1. Download

Grab the latest `WindowsMCP.NET.exe` from [Releases](../../releases).

### 2. Run

```bash
WindowsMCP.NET.exe
```

The setup wizard runs automatically on first start:
- Generates an API key
- Creates a self-signed HTTPS certificate
- Shows the Claude Code config snippet

### 3. Connect

Add the displayed JSON to your Claude Code settings:

```json
{
  "mcpServers": {
    "windows-mcp-dotnet": {
      "type": "streamable-http",
      "url": "https://YOUR-PC:8000/mcp",
      "headers": {
        "Authorization": "Bearer wmcp_your_key_here"
      }
    }
  }
}
```

## Tools

| Category | Tools |
|----------|-------|
| **Input** | Click, Type, Scroll, Move, Shortcut, Wait |
| **Screen** | Context (system state), Observe (elements with stable ids), Snapshot (screenshot + UI tree), Screenshot |
| **Apps** | App (launch, ensure, status, switch, resize) |
| **System** | PowerShell, Process, Registry |
| **Data** | Clipboard, FileSystem |
| **UI** | Perform (batched action chains), MultiSelect, MultiEdit |
| **Other** | Notification, Scrape |

## Observe, then act by element id

`Observe` lists the actionable elements of the foreground application with an id each. Pass an id as
`element` to `Click`, `Type`, `Perform`, `MultiSelect` or `MultiEdit`:

```text
Observe                              → Button 'Open' = e1a2b, Edit 'User Name' = e3sqx, …
Click(element="e1a2b")               → via mouse — effect: changed
Type(element="e3sqx", text="jan", clear=true)
                                     → effect: value_verified
```

Every element action reports an `effect`: `changed`, `unchanged`, `value_verified`, `value_mismatch` or
`not_verified` (password fields). An action is refused with an `[ERROR]` instead of guessing when the
target cannot be reached safely — for example when a modal dialog covers it or the session has no
interactive desktop. Ids stay valid while the application runs; observe again after a restart.

See [CLAUDE.md](CLAUDE.md) for the full rules.

## CLI

```bash
WindowsMCP.NET.exe                    # Start server (HTTP)
WindowsMCP.NET.exe --transport stdio  # Start in stdio mode
WindowsMCP.NET.exe setup              # Run setup wizard
WindowsMCP.NET.exe setup --new-key    # Generate new API key
WindowsMCP.NET.exe info               # Show config snippet
```

## Building from Source

```bash
dotnet build WindowsMCP.NET.slnx
dotnet publish src/WindowsMCP.NET/WindowsMCP.NET.csproj -c Release -r win-x64
```

## License

MIT
