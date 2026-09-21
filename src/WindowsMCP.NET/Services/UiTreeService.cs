using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

/// <summary>
/// Owns the current annotated UI tree. Snapshot/Context rebuild it explicitly; label lookups
/// (Click/Type/Perform) reuse the last tree so numbered labels stay stable between calls.
/// </summary>
public sealed class UiTreeService
{
    private readonly UiAutomationService _uiAutomation;
    private readonly ILogger<UiTreeService> _logger;

    private AnnotatedTree? _cache;
    private readonly Lock _lock = new();

    public UiTreeService(UiAutomationService uiAutomation, ILogger<UiTreeService> logger)
    {
        _uiAutomation = uiAutomation;
        _logger = logger;
    }

    /// <summary>Rebuilds the tree from the live desktop and makes it the current one.</summary>
    public AnnotatedTree BuildAnnotatedTree()
    {
        lock (_lock)
        {
            _logger.LogDebug("Building fresh UI tree");
            var roots = _uiAutomation.GetDesktopTree();
            var labelMap = new Dictionary<string, UiElementNode>();
            var counter = 1;

            AssignLabels(roots, labelMap, ref counter);

            _cache = new AnnotatedTree
            {
                Roots = roots,
                LabelMap = labelMap,
                Timestamp = DateTimeOffset.UtcNow,
            };

            _logger.LogInformation("UI tree built with {Count} interactive elements", labelMap.Count);
            return _cache;
        }
    }

    public (int X, int Y)? ResolveLabel(string label)
    {
        var tree = Current();

        if (!tree.LabelMap.TryGetValue(label, out var node))
            return null;

        return (node.X + node.Width / 2, node.Y + node.Height / 2);
    }

    public void InvalidateCache()
    {
        lock (_lock)
        {
            _cache = null;
        }
        _logger.LogDebug("UI tree cache invalidated");
    }

    public List<(int X, int Y, string Label)> GetAnnotationPoints()
    {
        var tree = Current();
        return tree.LabelMap
            .Select(kvp => (kvp.Value.X + kvp.Value.Width / 2, kvp.Value.Y, kvp.Key))
            .ToList();
    }

    private AnnotatedTree Current()
    {
        lock (_lock)
        {
            return _cache ?? BuildAnnotatedTree();
        }
    }

    private static void AssignLabels(List<UiElementNode> nodes, Dictionary<string, UiElementNode> labelMap, ref int counter)
    {
        foreach (var node in nodes)
        {
            if (node.IsInteractive)
            {
                var label = counter.ToString();
                node.Label = label;
                labelMap[label] = node;
                counter++;
            }
            AssignLabels(node.Children, labelMap, ref counter);
        }
    }
}
