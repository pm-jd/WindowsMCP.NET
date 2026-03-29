using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public sealed class UiTreeService
{
    private readonly UiAutomationService _uiAutomation;
    private readonly ILogger<UiTreeService> _logger;

    private AnnotatedTree? _cache;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(2);

    public UiTreeService(UiAutomationService uiAutomation, ILogger<UiTreeService> logger)
    {
        _uiAutomation = uiAutomation;
        _logger = logger;
    }

    public AnnotatedTree BuildAnnotatedTree()
    {
        if (_cache is not null && DateTimeOffset.UtcNow - _cache.Timestamp < _cacheTtl)
        {
            _logger.LogDebug("Returning cached UI tree");
            return _cache;
        }

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

    public (int X, int Y)? ResolveLabel(string label)
    {
        var tree = _cache ?? BuildAnnotatedTree();

        if (!tree.LabelMap.TryGetValue(label, out var node))
            return null;

        return (node.X + node.Width / 2, node.Y + node.Height / 2);
    }

    public void InvalidateCache()
    {
        _cache = null;
        _logger.LogDebug("UI tree cache invalidated");
    }

    public List<(int X, int Y, string Label)> GetAnnotationPoints()
    {
        var tree = _cache ?? BuildAnnotatedTree();
        return tree.LabelMap
            .Select(kvp => (kvp.Value.X + kvp.Value.Width / 2, kvp.Value.Y, kvp.Key))
            .ToList();
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
