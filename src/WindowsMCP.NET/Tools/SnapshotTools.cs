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
                 "use_annotation=true overlays numbered labels on interactive elements. " +
                 "use_vision=false skips screenshot capture. use_dom=false skips UI tree building.")]
    public static IList<ContentBlock> Snapshot(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Overlay element labels on the screenshot")] bool use_annotation = true,
        [Description("Capture screenshot (set false to skip image)")] bool use_vision = true,
        [Description("Build UI element tree (set false to skip DOM)")] bool use_dom = true,
        [Description("Build UI element tree using UI Automation (alias for use_dom)")] bool use_ui_tree = true,
        [Description("Display index (null = primary)")] int? display = null,
        [Description("Reference line for width annotation (not yet implemented)")] int? width_reference_line = null,
        [Description("Reference line for height annotation (not yet implemented)")] int? height_reference_line = null)
    {
        try
        {
            var result = new List<ContentBlock>();

            // use_ui_tree is an alias for use_dom
            bool buildTree = use_dom && use_ui_tree;

            if (buildTree)
            {
                uiTreeService.InvalidateCache();
                var tree = uiTreeService.BuildAnnotatedTree();
                result.Add(new TextContentBlock { Text = tree.ToText() });
            }

            if (use_vision)
            {
                var pngBytes = captureService.CaptureScreen(display);

                if (use_annotation)
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
        catch (Exception ex)
        {
            return new List<ContentBlock>
            {
                new TextContentBlock { Text = $"[ERROR] {ex.GetType().Name}: {ex.Message}" }
            };
        }
    }

    [McpServerTool(Name = "Screenshot", ReadOnly = true, Idempotent = true)]
    [Description("Capture the screen quickly. Returns only the PNG image without rebuilding the UI tree.")]
    public static IList<ContentBlock> Screenshot(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Overlay cached element labels on the screenshot")] bool use_annotation = true,
        [Description("Display index (null = primary)")] int? display = null,
        [Description("Reference line for width annotation (not yet implemented)")] int? width_reference_line = null,
        [Description("Reference line for height annotation (not yet implemented)")] int? height_reference_line = null)
    {
        try
        {
            var pngBytes = captureService.CaptureScreen(display);

            if (use_annotation)
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
        catch (Exception ex)
        {
            return new List<ContentBlock>
            {
                new TextContentBlock { Text = $"[ERROR] {ex.GetType().Name}: {ex.Message}" }
            };
        }
    }
}
