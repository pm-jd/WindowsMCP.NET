using System.ComponentModel;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class AppTools
{
    [McpServerTool(Name = "App", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Launch, switch to, or resize a window. " +
                 "mode: launch (start app by executable/URI), switch (focus by window title), resize (move/size window).")]
    public static string App(
        DesktopService desktopService,
        [Description("Mode: launch, switch, or resize")] string mode,
        [Description("App name/executable for launch; window title substring for switch/resize")] string name,
        [Description("X position for resize")] int? x = null,
        [Description("Y position for resize")] int? y = null,
        [Description("Width for resize")] int? width = null,
        [Description("Height for resize")] int? height = null)
    {
        return mode.ToLowerInvariant() switch
        {
            "launch" => LaunchApp(desktopService, name),
            "switch" => SwitchToApp(desktopService, name),
            "resize" => ResizeApp(desktopService, name, x, y, width, height),
            _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: launch, switch, or resize.")
        };
    }

    private static string LaunchApp(DesktopService desktopService, string name)
    {
        var window = desktopService.LaunchApp(name);
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
        int? x, int? y, int? width, int? height)
    {
        var windows = desktopService.ListWindows();
        var match = windows.FirstOrDefault(w =>
            w.Title.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No window matching '{name}' found.";

        int rx = x ?? match.X;
        int ry = y ?? match.Y;
        int rw = width ?? match.Width;
        int rh = height ?? match.Height;

        var ok = desktopService.ResizeWindow(match.Handle, rx, ry, rw, rh);
        return ok
            ? $"Resized \"{match.Title}\" to ({rx},{ry}) {rw}x{rh}"
            : $"Failed to resize \"{match.Title}\"";
    }
}
