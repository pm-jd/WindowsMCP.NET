using System.ComponentModel;
using System.Drawing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Models;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ObserveTools
{
    [McpServerTool(Name = "Observe", ReadOnly = true, Idempotent = true)]
    [Description("Compact, stable-id state of the UI for precise interaction. scope: foreground (the active " +
                 "application's windows, default), process (a named process's windows — requires 'process'), " +
                 "desktop (all visible top-level windows). Pass an element's id as \"element\" to Click/Type/Perform " +
                 "instead of coordinates — ids stay valid across the last several Observe calls. " +
                 "format=json shape: {windows:[{title,process,pid,foreground,modal,rect:[x,y,w,h]}], " +
                 "focus:string|null, elements:[{id,type,name,label?,panel?,window?,value?,toggle?,selected?," +
                 "expanded?,enabled?,rect:[x,y,w,h]}], texts:[string], signature:string, truncated:bool, " +
                 "timings:{walk_ms,hit_ms,total_ms}}. Set screenshot=true to also attach a downscaled JPEG " +
                 "of the observed windows.")]
    public static CallToolResult Observe(
        ObservationService observation,
        ObservationStore store,
        ScreenCaptureService capture,
        [Description("Which windows to observe")] ObserveScope scope = ObserveScope.Foreground,
        [Description("Process name, required when scope=process (e.g. 'notepad')")] string? process = null,
        [Description("Output format")] OutputFormat format = OutputFormat.Markdown,
        [Description("Also attach a downscaled JPEG screenshot of the observed windows")] bool screenshot = false,
        [Description("Maximum elements to return (clamped 10-500)")] int max_elements = 150,
        CancellationToken ct = default)
    {
        try
        {
            var clampedMaxElements = Math.Clamp(max_elements, 10, 500);
            var result = observation.Observe(scope, process, clampedMaxElements, ct);
            store.Remember(result);

            var toolResult = format == OutputFormat.Json
                ? ToolHelpers.JsonResult(ObservationFormatter.ToJsonEnvelope(result))
                : ToolHelpers.TextResult(ObservationFormatter.ToMarkdown(result));

            if (screenshot)
            {
                var region = ScreenshotRegion(result.Windows);
                if (!region.IsEmpty)
                    toolResult.Content.Add(ImageContentBlock.FromBytes(capture.CaptureRegionJpeg(region), "image/jpeg"));
            }

            return toolResult;
        }
        catch (Exception ex)
        {
            return ToolHelpers.ErrorResult(ex);
        }
    }

    /// <summary>Union of every observed window's bounds, clipped to the virtual screen — the region a
    /// screenshot needs to cover, and never wider than what's actually capturable via GDI.</summary>
    private static Rectangle ScreenshotRegion(IReadOnlyList<ObservedWindow> windows)
    {
        if (windows.Count == 0)
            return Rectangle.Empty;

        var union = windows[0].Rect;
        for (var i = 1; i < windows.Count; i++)
            union = Rectangle.Union(union, windows[i].Rect);

        var virtualScreen = new Rectangle(
            User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));

        return Rectangle.Intersect(union, virtualScreen);
    }
}
