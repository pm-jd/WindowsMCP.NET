# WindowsMCP.NET

A .NET MCP (Model Context Protocol) server for Windows desktop automation. Remote-first with Streamable HTTP transport, ships as a portable single-file executable.

Inspired by [Windows-MCP](https://github.com/CursorTouch/Windows-MCP) (Python), reimplemented in C# with enhanced UI Automation via [FlaUI](https://github.com/FlaUI/FlaUI).

## Features

- **18 MCP Tools** for full Windows desktop control
- **Remote-first** — Streamable HTTP with API key auth and HTTPS
- **Portable** — Single `.exe`, no .NET runtime needed
- **UI Automation** — FlaUI (UIA3) for reliable element interaction
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
| **Screen** | Snapshot (screenshot + UI tree), Screenshot |
| **Apps** | App (launch, switch, resize) |
| **System** | PowerShell, Process, Registry |
| **Data** | Clipboard, FileSystem |
| **UI** | MultiSelect, MultiEdit |
| **Other** | Notification, Scrape |

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
