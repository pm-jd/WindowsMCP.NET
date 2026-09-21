using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
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
    public static async Task<CallToolResult> App(
        DesktopService desktopService,
        [Description("Operation")] AppMode mode = AppMode.Launch,
        [Description("App name. For ensure/status: process name (e.g. 'notepad'); " +
                     "falls back to fuzzy window title match. For launch: executable/URI.")]
        string name = "",
        [Description("Window position as [x, y] (for resize)")] int[]? window_loc = null,
        [Description("Window size as [width, height] (for resize)")] int[]? window_size = null,
        [Description("Optional launch command for mode=ensure when process not found " +
                     "(e.g. full path or URI like 'ms-teams:'). Defaults to `name`.")]
        string? launch_command = null,
        [Description("Behavior on multiple matches (ensure/status): use the first match or report an error.")]
        AmbiguousPolicy ambiguous = AmbiguousPolicy.First,
        [Description("Output format (for mode=status). " +
                     "json shape: {running:bool, match_count:int, matches:[{pid:int, process:str, title:str}]}")]
        OutputFormat format = OutputFormat.Markdown,
        CancellationToken ct = default)
    {
        try
        {
            return mode switch
            {
                AppMode.Launch => ToolHelpers.TextResult(await LaunchApp(desktopService, name, ct)),
                AppMode.Ensure => ToolHelpers.TextResult(await EnsureApp(desktopService, name, launch_command, ambiguous, ct)),
                AppMode.Status => StatusApp(desktopService, name, ambiguous, format),
                AppMode.Switch => ToolHelpers.TextResult(SwitchToApp(desktopService, name)),
                AppMode.Resize => ToolHelpers.TextResult(ResizeApp(desktopService, name, window_loc, window_size)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client cancelled the request; the SDK reports it as cancelled, not as a tool error
        }
        catch (Exception ex)
        {
            return ToolHelpers.ErrorResult(ex);
        }
    }

    private static async Task<string> LaunchApp(DesktopService desktopService, string name, CancellationToken ct)
    {
        var window = await desktopService.LaunchApp(name, null, ct);
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
        int[]? window_loc, int[]? window_size)
    {
        var windows = desktopService.ListWindows();
        var match = windows.FirstOrDefault(w =>
            w.Title.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No window matching '{name}' found.";

        var loc = ToolHelpers.ToPoint(window_loc);
        var size = ToolHelpers.ToPoint(window_size);

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
        string name, string? launchCommand, AmbiguousPolicy ambiguous, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("'name' is required for mode=ensure.");

        var matches = desktopService.FindMatches(name);

        if (matches.Count == 0)
        {
            var window = await desktopService.LaunchApp(name, launchCommand, ct);
            if (window is null)
                return $"Launched '{name}' (window not yet visible)";
            return $"Launched '{name}' — window: \"{window.Title}\" PID={window.ProcessId}";
        }

        if (matches.Count > 1 && ambiguous == AmbiguousPolicy.Error)
            return FormatAmbiguous(matches);

        var (target, _) = matches[0];
        bool focused = desktopService.BringToForeground(target.Handle);
        return focused
            ? $"Focused \"{target.Title}\" (PID={target.ProcessId})"
            : $"Attempted focus on \"{target.Title}\" (PID={target.ProcessId}) — window may not have come to front";
    }

    private static CallToolResult StatusApp(DesktopService desktopService, string name, AmbiguousPolicy ambiguous, OutputFormat format)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("'name' is required for mode=status.");

        var matches = desktopService.FindMatches(name);

        if (format == OutputFormat.Json)
        {
            return ToolHelpers.JsonResult(new
            {
                running = matches.Count > 0,
                match_count = matches.Count,
                matches = matches.Select(m => new
                {
                    pid = m.Window.ProcessId,
                    process = m.ProcessName,
                    title = m.Window.Title,
                }).ToArray(),
            });
        }

        if (matches.Count == 0)
            return ToolHelpers.TextResult("Not running");

        if (matches.Count > 1 && ambiguous == AmbiguousPolicy.Error)
            return ToolHelpers.TextResult(FormatAmbiguous(matches));

        var (target, _) = matches[0];
        return ToolHelpers.TextResult($"Running: PID={target.ProcessId}, window=\"{target.Title}\"");
    }

    private static string FormatAmbiguous(List<(WindowInfo Window, string ProcessName)> matches)
    {
        var parts = matches.Select(m =>
            $"[PID {m.Window.ProcessId} '{m.ProcessName}' — \"{m.Window.Title}\"]");
        return $"Multiple matches: {string.Join(", ", parts)}. Specify a more specific name.";
    }
}
