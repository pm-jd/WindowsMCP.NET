using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class SnapshotTools
{
    [McpServerTool(Name = "Snapshot", ReadOnly = true, Idempotent = true)]
    [Description("Capture the screen and build the UI element tree. " +
                 "Returns an annotated screenshot (PNG) and the text tree. " +
                 "annotate=true overlays numbered labels on interactive elements.")]
    public static IList<ContentBlock> Snapshot(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Overlay element labels on the screenshot")] bool annotate = true,
        [Description("Display index (null = primary)")] int? display = null)
    {
        uiTreeService.InvalidateCache();
        var tree = uiTreeService.BuildAnnotatedTree();

        var pngBytes = captureService.CaptureScreen(display);

        if (annotate)
        {
            var points = uiTreeService.GetAnnotationPoints()
                .Select(p => (p.X, p.Y, p.Label))
                .ToList<(int X, int Y, string Label)>();
            pngBytes = captureService.AnnotateScreenshot(pngBytes, points);
        }

        return new List<ContentBlock>
        {
            ImageContentBlock.FromBytes(pngBytes, "image/png"),
            new TextContentBlock { Text = tree.ToText() },
        };
    }

    [McpServerTool(Name = "Screenshot", ReadOnly = true, Idempotent = true)]
    [Description("Capture the screen quickly. Returns only the PNG image without rebuilding the UI tree.")]
    public static IList<ContentBlock> Screenshot(
        ScreenCaptureService captureService,
        [Description("Display index (null = primary)")] int? display = null)
    {
        var pngBytes = captureService.CaptureScreen(display);

        return new List<ContentBlock>
        {
            ImageContentBlock.FromBytes(pngBytes, "image/png"),
        };
    }
}
