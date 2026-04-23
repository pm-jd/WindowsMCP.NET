# Tray Menu — Autostart Toggle

**Date:** 2026-04-23
**Status:** Approved, ready for implementation

## Problem

The Autostart setting (run WindowsMCP.NET at user logon) can only be configured during the initial `SetupWizard` console prompt. Once the tray-only build is running, there is no way to toggle it without re-running setup.

## Goal

Add a checkable `Autostart` entry to the tray context menu that reflects the current state and toggles it on click.

## Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Menu position | Between `Check for Updates` and the second separator (settings group, no extra separator) |
| 2 | Naming | `Autostart` — matches existing `AutoStartManager` / `AppConfig.Autostart` |
| 3 | Failure feedback | `MessageBox.Show(...)` warning, leave `Checked` unchanged on failure |
| 4 | API shape | `AutoStartManager.Enable()` / `Disable()` return `bool` (success) — caller can decide how to react |
| 5 | External-change refresh | Re-read `IsEnabled()` on `ContextMenuStrip.Opening` (handles state changed by another instance / setup wizard) |

## Affected Files

- `src/WindowsMCP.NET/Setup/AutoStartManager.cs` — change `Enable()`/`Disable()` return type from `void` to `bool`
- `src/WindowsMCP.NET/Setup/TrayIconManager.cs` — add menu item, click handler, and `Opening` refresh
- `src/WindowsMCP.NET/Setup/SetupWizard.cs` — no behavioral change; `bool` returns are ignored (best-effort, console output preserved)

## UX Sketch

```
WindowsMCP.NET — http://192.168.40.122:8000
─────────────────────────────────
Show Console
Copy Config Snippet
Check for Updates
✓ Autostart                       ← new (checkmark when enabled)
─────────────────────────────────
Stop Server
```

## Behaviour

1. **Initial render**: `Checked = AutoStartManager.IsEnabled()` (one `schtasks /Query` call when tray starts).
2. **Menu opens**: re-query `IsEnabled()` to reflect external changes.
3. **Click**:
   - Currently checked → `Disable()`. On `true` → uncheck. On `false` → `MessageBox` warning, stay checked.
   - Currently unchecked → `Enable()`. On `true` → check. On `false` → `MessageBox` warning, stay unchecked.
4. `CheckOnClick = false` — we manage `Checked` manually so a failed toggle does not desync UI from reality.

## Out of Scope

- Tests for `TrayIconManager` / `AutoStartManager` — UI thread + external-process code, no existing test scaffolding.
- Replacing the schtasks-based mechanism with the Registry `Run` key.
- Localization of menu labels.
