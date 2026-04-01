using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ContextTools
{
    private static readonly HashSet<string> ValidModules = ["window", "screen", "ui_tree", "clipboard", "processes"];
    private static readonly List<string> DefaultModules = ["window", "screen"];

    [McpServerTool(Name = "Context", ReadOnly = true, Idempotent = true)]
    [Description("Get current system state. include: array of modules to return — " +
                 "window (active window info), screen (screenshot), ui_tree (UI element labels), " +
                 "clipboard (text content), processes (top processes). Default: [window, screen].")]
    public static IList<ContentBlock> Context(
        DesktopService desktopService,
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Modules to include: window, screen, ui_tree, clipboard, processes")] JsonElement? include = null)
    {
        var modules = ParseInclude(include);
        var sb = new StringBuilder();
        byte[]? screenshotPng = null;

        foreach (var module in modules)
        {
            try
            {
                switch (module)
                {
                    case "window":
                        AppendWindowInfo(sb, desktopService);
                        break;
                    case "screen":
                        screenshotPng = captureService.CaptureScreen(null);
                        break;
                    case "ui_tree":
                        AppendUiTree(sb, uiTreeService, captureService, ref screenshotPng);
                        break;
                    case "clipboard":
                        AppendClipboard(sb);
                        break;
                    case "processes":
                        AppendProcesses(sb);
                        break;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"-- {module} --");
                sb.AppendLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                sb.AppendLine();
            }
        }

        var result = new List<ContentBlock>();
        if (screenshotPng is not null)
            result.Add(ImageContentBlock.FromBytes(screenshotPng, "image/png"));
        if (sb.Length > 0)
            result.Add(new TextContentBlock { Text = sb.ToString().TrimEnd() });
        return result;
    }

    public static List<string> ParseInclude(JsonElement? include)
    {
        if (!include.HasValue || include.Value.ValueKind != JsonValueKind.Array)
            return new List<string>(DefaultModules);

        var arr = include.Value;
        if (arr.GetArrayLength() == 0)
            return new List<string>(DefaultModules);

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var val = item.GetString()?.ToLowerInvariant();
            if (val is not null && ValidModules.Contains(val))
                result.Add(val);
        }
        return result.Count > 0 ? result : new List<string>(DefaultModules);
    }

    private static void AppendWindowInfo(StringBuilder sb, DesktopService desktopService)
    {
        sb.AppendLine("-- Active Window --");
        var win = desktopService.GetForegroundWindow();
        if (win is null)
        {
            sb.AppendLine("(no foreground window)");
        }
        else
        {
            string processName = "(unknown)";
            try { processName = Process.GetProcessById((int)win.ProcessId).ProcessName; } catch { }
            sb.AppendLine($"Title:    {win.Title}");
            sb.AppendLine($"Process:  {processName} (PID {win.ProcessId})");
            sb.AppendLine($"Bounds:   [{win.X}, {win.Y}, {win.Width}, {win.Height}]");
        }
        sb.AppendLine();
    }

    private static void AppendUiTree(StringBuilder sb, UiTreeService uiTreeService,
        ScreenCaptureService captureService, ref byte[]? screenshotPng)
    {
        uiTreeService.InvalidateCache();
        var tree = uiTreeService.BuildAnnotatedTree();
        sb.AppendLine($"-- UI Tree ({tree.LabelMap.Count} interactive elements) --");
        sb.AppendLine(tree.ToText());

        if (screenshotPng is not null)
        {
            var points = uiTreeService.GetAnnotationPoints()
                .Select(p => (p.X, p.Y, p.Label))
                .ToList<(int X, int Y, string Label)>();
            screenshotPng = captureService.AnnotateScreenshot(screenshotPng, points);
        }
    }

    private static void AppendClipboard(StringBuilder sb)
    {
        sb.AppendLine("-- Clipboard --");
        var text = ClipboardTools.Clipboard("get");
        if (text.Length > 1000)
            text = text[..1000] + $"... ({text.Length} chars total)";
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static void AppendProcesses(StringBuilder sb)
    {
        sb.AppendLine("-- Top Processes --");
        sb.AppendLine($"{"PID",7}  {"Memory",9}  Window Title");
        var processes = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != nint.Zero; } catch { return false; } })
            .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
            .Take(10)
            .ToList();

        foreach (var p in processes)
        {
            try
            {
                var mem = p.WorkingSet64 / (1024 * 1024);
                var title = p.MainWindowTitle;
                if (string.IsNullOrEmpty(title)) title = p.ProcessName;
                sb.AppendLine($"{p.Id,7}  {mem,6} MB  {title}");
            }
            catch { }
        }
        sb.AppendLine();
    }
}
