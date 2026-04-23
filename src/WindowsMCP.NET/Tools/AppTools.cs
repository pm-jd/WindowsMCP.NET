using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class AppTools
{
    [McpServerTool(Name = "App", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Launch, focus, check, or resize a window. " +
                 "mode: launch (start app), ensure (focus if running, else launch), " +
                 "status (check if running), switch (focus by window title), resize.")]
    public static async Task<string> App(
        DesktopService desktopService,
        [Description("Mode: launch, ensure, status, switch, or resize")] string mode = "launch",
        [Description("App name. For ensure/status: process name (e.g. 'notepad'); " +
                     "falls back to fuzzy window title match. For launch: executable/URI.")]
        string name = "",
        [Description("Window position as [x, y] (for resize)")] JsonElement? window_loc = null,
        [Description("Window size as [width, height] (for resize)")] JsonElement? window_size = null,
        [Description("Optional launch command for mode=ensure when process not found " +
                     "(e.g. full path or URI like 'ms-teams:'). Defaults to `name`.")]
        string? launch_command = null,
        [Description("Behavior on multiple matches (ensure/status): 'first' (default) or 'error'.")]
        string ambiguous = "first")
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "launch" => await LaunchApp(desktopService, name),
                "ensure" => await EnsureApp(desktopService, name, launch_command, ambiguous),
                "status" => StatusApp(desktopService, name, ambiguous),
                "switch" => SwitchToApp(desktopService, name),
                "resize" => ResizeApp(desktopService, name, window_loc, window_size),
                _ => throw new ArgumentException(
                    $"Unknown mode '{mode}'. Use: launch, ensure, status, switch, or resize.")
            };
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static async Task<string> LaunchApp(DesktopService desktopService, string name)
    {
        var window = await desktopService.LaunchApp(name);
        if (window is null)
            return $"Launched '{name}' (window not yet visible)";
        return $"Launched '{name}' — window: \"{window.Title}\" PID={window.ProcessId}";
    }

    private static string SwitchToApp(DesktopService desktopService, string name)
    {
        var window = desktopService.SwitchToWindow(name);
        if (window is null)
            return $"No window matching '{name}' found.";
        return $"Switched to \"{window.Title}\" (PID={window.ProcessId})";
    }

    private static string ResizeApp(DesktopService desktopService, string name,
        JsonElement? window_loc, JsonElement? window_size)
    {
        var windows = desktopService.ListWindows();
        var match = windows.FirstOrDefault(w =>
            w.Title.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No window matching '{name}' found.";

        var loc = ParseCoord(window_loc);
        var size = ParseCoord(window_size);

        int rx = loc.HasValue ? loc.Value.X : match.X;
        int ry = loc.HasValue ? loc.Value.Y : match.Y;
        int rw = size.HasValue ? size.Value.X : match.Width;
        int rh = size.HasValue ? size.Value.Y : match.Height;

        var ok = desktopService.ResizeWindow(match.Handle, rx, ry, rw, rh);
        return ok
            ? $"Resized \"{match.Title}\" to ({rx},{ry}) {rw}x{rh}"
            : $"Failed to resize \"{match.Title}\"";
    }

    private static async Task<string> EnsureApp(DesktopService desktopService,
        string name, string? launchCommand, string ambiguous)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("'name' is required for mode=ensure.");
        ValidateAmbiguous(ambiguous);

        var matches = desktopService.FindMatches(name);

        if (matches.Count == 0)
        {
            var window = await desktopService.LaunchApp(name, launchCommand);
            if (window is null)
                return $"Launched '{name}' (window not yet visible)";
            return $"Launched '{name}' — window: \"{window.Title}\" PID={window.ProcessId}";
        }

        if (matches.Count > 1 && ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
            return FormatAmbiguous(matches);

        var (target, _) = matches[0];
        bool focused = desktopService.BringToForeground(target.Handle);
        return focused
            ? $"Focused \"{target.Title}\" (PID={target.ProcessId})"
            : $"Attempted focus on \"{target.Title}\" (PID={target.ProcessId}) — window may not have come to front";
    }

    private static string StatusApp(DesktopService desktopService, string name, string ambiguous)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("'name' is required for mode=status.");
        ValidateAmbiguous(ambiguous);

        var matches = desktopService.FindMatches(name);

        if (matches.Count == 0)
            return "Not running";

        if (matches.Count > 1 && ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
            return FormatAmbiguous(matches);

        var (target, _) = matches[0];
        return $"Running: PID={target.ProcessId}, window=\"{target.Title}\"";
    }

    private static void ValidateAmbiguous(string ambiguous)
    {
        if (!ambiguous.Equals("first", StringComparison.OrdinalIgnoreCase) &&
            !ambiguous.Equals("error", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("'ambiguous' must be 'first' or 'error'.");
    }

    private static string FormatAmbiguous(List<(WindowInfo Window, string ProcessName)> matches)
    {
        var parts = matches.Select(m =>
            $"[PID {m.Window.ProcessId} '{m.ProcessName}' — \"{m.Window.Title}\"]");
        return $"Multiple matches: {string.Join(", ", parts)}. Specify a more specific name.";
    }

    /// <summary>
    /// Parses a JsonElement that is expected to be a [x, y] integer array.
    /// Returns null if the element is null/undefined or not a valid 2-element array.
    /// </summary>
    private static (int X, int Y)? ParseCoord(JsonElement? elem)
    {
        if (!elem.HasValue || elem.Value.ValueKind != JsonValueKind.Array) return null;
        var arr = elem.Value;
        if (arr.GetArrayLength() < 2) return null;
        return (arr[0].GetInt32(), arr[1].GetInt32());
    }
}
