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
                 "Returns an annotated screenshot (PNG) and/or the text tree. " +
                 "useAnnotation=true overlays numbered labels on interactive elements. " +
                 "useVision=false skips screenshot capture. useDom=false skips UI tree building.")]
    public static IList<ContentBlock> Snapshot(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Overlay element labels on the screenshot")] bool useAnnotation = true,
        [Description("Capture screenshot (set false to skip image)")] bool useVision = true,
        [Description("Build UI element tree (set false to skip DOM)")] bool useDom = true,
        [Description("Display index (null = primary)")] int? display = null)
    {
        var result = new List<ContentBlock>();

        if (useDom)
        {
            uiTreeService.InvalidateCache();
            var tree = uiTreeService.BuildAnnotatedTree();
            result.Add(new TextContentBlock { Text = tree.ToText() });
        }

        if (useVision)
        {
            var pngBytes = captureService.CaptureScreen(display);

            if (useAnnotation)
            {
                var points = uiTreeService.GetAnnotationPoints()
                    .Select(p => (p.X, p.Y, p.Label))
                    .ToList<(int X, int Y, string Label)>();
                pngBytes = captureService.AnnotateScreenshot(pngBytes, points);
            }

            result.Insert(0, ImageContentBlock.FromBytes(pngBytes, "image/png"));
        }

        return result;
    }

    [McpServerTool(Name = "Screenshot", ReadOnly = true, Idempotent = true)]
    [Description("Capture the screen quickly. Returns only the PNG image without rebuilding the UI tree.")]
    public static IList<ContentBlock> Screenshot(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Overlay cached element labels on the screenshot")] bool useAnnotation = true,
        [Description("Display index (null = primary)")] int? display = null)
    {
        var pngBytes = captureService.CaptureScreen(display);

        if (useAnnotation)
        {
            var points = uiTreeService.GetAnnotationPoints()
                .Select(p => (p.X, p.Y, p.Label))
                .ToList<(int X, int Y, string Label)>();
            pngBytes = captureService.AnnotateScreenshot(pngBytes, points);
        }

        return new List<ContentBlock>
        {
            ImageContentBlock.FromBytes(pngBytes, "image/png"),
        };
    }
}
