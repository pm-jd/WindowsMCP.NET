using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class AppTools
{
    [McpServerTool(Name = "App", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Launch, switch to, or resize a window. " +
                 "mode: launch (start app by executable/URI), switch (focus by window title), resize (move/size window).")]
    public static async Task<string> App(
        DesktopService desktopService,
        [Description("Mode: launch, switch, or resize")] string mode = "launch",
        [Description("App name/executable for launch; window title substring for switch/resize")] string name = "",
        [Description("Window position as [x, y] (for resize)")] JsonElement? window_loc = null,
        [Description("Window size as [width, height] (for resize)")] JsonElement? window_size = null)
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "launch" => await LaunchApp(desktopService, name),
                "switch" => SwitchToApp(desktopService, name),
                "resize" => ResizeApp(desktopService, name, window_loc, window_size),
                _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: launch, switch, or resize.")
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
