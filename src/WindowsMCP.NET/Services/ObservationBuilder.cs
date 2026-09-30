using System.Security.Cryptography;
using System.Text;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

/// <summary>
/// Turns a raw UIA node walk into a compact <see cref="Observation"/>: drops hit-invisible
/// subtrees, filters down to actionable elements, derives labels/panels, orders everything,
/// assigns stable per-observation ids and computes the change-detection signature. Pure — no UIA
/// dependency — so it is unit-testable with synthetic node lists (see spec §6, §1-§2).
/// </summary>
public static class ObservationBuilder
{
    private const string HexAlphabet = "0123456789abcdef";

    // Dictionary<TKey,TValue> requires a notnull TKey, which int? (Nullable<int>) does not satisfy
    // even though it can never actually be null as a value type — so "no parent" (a window root) is
    // keyed as -1 (node.Index is always >= 0, so -1 can't collide with a real parent index).
    private const int NoParent = -1;

    private static readonly HashSet<string> ActionableTypes = new(StringComparer.Ordinal)
    {
        "Button", "SplitButton", "CheckBox", "RadioButton", "ComboBox", "Edit", "Spinner",
        "Slider", "Hyperlink", "MenuItem", "TabItem", "ListItem", "TreeItem", "DataItem", "HeaderItem"
    };

    private static readonly HashSet<string> PanelTypes = new(StringComparer.Ordinal)
    {
        "Pane", "Group", "ToolBar", "Tab", "MenuBar", "Menu", "StatusBar", "Tree", "List", "Table", "DataGrid"
    };

    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal) { "Text", "Group", "Header" };

    private static readonly HashSet<string> LabelSourceTypes = new(StringComparer.Ordinal)
    {
        "Edit", "ComboBox", "Spinner", "Slider"
    };

    /// <summary>
    /// Builds the <see cref="Observation"/> for one collection pass. <paramref name="windows"/> must
    /// be topmost-first; <paramref name="nodes"/> must have <see cref="ObservedNode.Index"/> equal to
    /// each node's position in the list, with exactly one root (Depth 0, Parent null) per window.
    /// </summary>
    public static Observation Build(
        IReadOnlyList<ObservedWindow> windows,
        IReadOnlyList<ObservedNode> nodes,
        int maxElements,
        ObservationTimings timings,
        bool budgetExceeded)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(timings);

        var byIndex = new ObservedNode[nodes.Count];
        foreach (var node in nodes)
            byIndex[node.Index] = node;

        var droppedMemo = new bool?[nodes.Count];
        var dropped = new bool[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
            dropped[i] = IsDropped(i, byIndex, droppedMemo);

        var siblingIndexes = ComputeSiblingIndexes(nodes);
        var childrenByParent = BuildChildrenByParent(nodes);
        var lastWindowIndex = windows.Count - 1;

        var candidates = new List<(ObservedNode Node, ElementLocator Locator)>();
        foreach (var node in nodes)
        {
            if (node.Parent is null || dropped[node.Index])
                continue;
            if (!ActionableTypes.Contains(node.ControlType))
                continue;
            if (node.Name.Length == 0 && string.IsNullOrEmpty(node.Value))
                continue;

            var window = windows[node.Window];
            var path = BuildLocatorPath(node, byIndex, siblingIndexes);
            candidates.Add((node, new ElementLocator(window.Process, window.ClassName, path)));
        }

        var ordered = candidates
            .OrderBy(c => c.Node.Window)
            .ThenBy(c => c.Node.Rect.Y)
            .ThenBy(c => c.Node.Rect.X)
            .ToList();

        var truncated = budgetExceeded || ordered.Count > maxElements;
        var trimmed = ordered.Take(Math.Max(0, maxElements)).ToList();

        var ids4 = new string[trimmed.Count];
        for (var i = 0; i < trimmed.Count; i++)
            ids4[i] = trimmed[i].Locator.Id(4);

        var collisionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids4)
            collisionCounts[id] = collisionCounts.GetValueOrDefault(id) + 1;

        var elements = new List<ObservedElement>(trimmed.Count);
        string? focusId = null;
        for (var i = 0; i < trimmed.Count; i++)
        {
            var (node, locator) = trimmed[i];
            var id = collisionCounts[ids4[i]] > 1 ? locator.Id(6) : ids4[i];
            var label = ResolveLabel(node, byIndex, childrenByParent, dropped);
            var panel = ResolvePanel(node, byIndex);
            var windowTitle = node.Window == lastWindowIndex ? null : windows[node.Window].Title;

            var element = new ObservedElement(
                id, node.ControlType, node.Name, label, panel, windowTitle,
                node.Value, node.Toggle, node.Selected, node.Expand, node.Enabled, node.Rect, locator);
            elements.Add(element);

            if (node.Focused && focusId is null)
                focusId = id;
        }

        var texts = new List<string>();
        var seenTexts = new HashSet<string>(StringComparer.Ordinal);
        var textNodes = nodes
            .Where(n => n.Parent is not null && !dropped[n.Index] && TextTypes.Contains(n.ControlType) && n.Name.Length > 0)
            .OrderBy(n => n.Window)
            .ThenBy(n => n.Rect.Y)
            .ThenBy(n => n.Rect.X);
        foreach (var node in textNodes)
        {
            if (seenTexts.Add(node.Name))
                texts.Add(node.Name);
        }

        var signature = ComputeSignature(windows, elements);

        return new Observation(windows, focusId, elements, texts, signature, truncated, timings);
    }

    /// <summary>
    /// First 8 lower-case hex characters of a SHA-256 hash over window <c>Handle+Title</c> and, per
    /// element, <c>Id|Name|Value|Toggle|Selected|Expand|Enabled</c> — deliberately excluding rects so
    /// pure movement/resizing doesn't change the signature.
    /// </summary>
    public static string ComputeSignature(IReadOnlyList<ObservedWindow> windows, IReadOnlyList<ObservedElement> elements)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(elements);

        var canonical = new StringBuilder();
        foreach (var window in windows)
            canonical.Append(window.Handle).Append('|').Append(window.Title).Append('\n');
        foreach (var element in elements)
        {
            canonical.Append(element.Id).Append('|').Append(element.Name).Append('|').Append(element.Value).Append('|')
                .Append(element.Toggle).Append('|').Append(element.Selected).Append('|').Append(element.Expand).Append('|')
                .Append(element.Enabled).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return LowerHex(hash.AsSpan(0, 4));
    }

    /// <summary>
    /// Whether <paramref name="index"/>'s node is dropped together with its subtree: cascades from an
    /// already-dropped parent, or the node itself has <c>HitVisible == false</c> — except a
    /// <c>TabItem</c> whose parent is a (kept) <c>Tab</c>.
    /// </summary>
    private static bool IsDropped(int index, ObservedNode[] byIndex, bool?[] memo)
    {
        if (memo[index] is bool cached)
            return cached;

        var node = byIndex[index];
        bool result;
        if (node.Parent is not int parentIndex)
        {
            result = false;
        }
        else if (IsDropped(parentIndex, byIndex, memo))
        {
            result = true;
        }
        else if (node.HitVisible == false)
        {
            var parent = byIndex[parentIndex];
            var exemptTabItem = node.ControlType == "TabItem" && parent.ControlType == "Tab";
            result = !exemptTabItem;
        }
        else
        {
            result = false;
        }

        memo[index] = result;
        return result;
    }

    /// <summary>
    /// Per-node count of earlier siblings (same Parent, lower Index) sharing the same
    /// (ControlType, Key) — the disambiguating index in each <see cref="LocatorStep"/>.
    /// </summary>
    private static int[] ComputeSiblingIndexes(IReadOnlyList<ObservedNode> nodes)
    {
        var siblingIndexes = new int[nodes.Count];
        var counters = new Dictionary<(int Parent, string ControlType, string Key), int>();
        foreach (var node in nodes.OrderBy(n => n.Index))
        {
            var key = (node.Parent ?? NoParent, node.ControlType, LocatorKey(node));
            counters.TryGetValue(key, out var count);
            siblingIndexes[node.Index] = count;
            counters[key] = count + 1;
        }

        return siblingIndexes;
    }

    private static Dictionary<int, List<int>> BuildChildrenByParent(IReadOnlyList<ObservedNode> nodes)
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        foreach (var node in nodes.OrderBy(n => n.Index))
        {
            var parentKey = node.Parent ?? NoParent;
            if (!childrenByParent.TryGetValue(parentKey, out var siblings))
            {
                siblings = [];
                childrenByParent[parentKey] = siblings;
            }

            siblings.Add(node.Index);
        }

        return childrenByParent;
    }

    /// <summary>Locator steps from the window root's child down to <paramref name="node"/> (root excluded).</summary>
    private static List<LocatorStep> BuildLocatorPath(ObservedNode node, ObservedNode[] byIndex, int[] siblingIndexes)
    {
        var steps = new List<LocatorStep>();
        var current = node;
        while (current.Parent is int parentIndex)
        {
            steps.Add(new LocatorStep(current.ControlType, LocatorKey(current), siblingIndexes[current.Index]));
            current = byIndex[parentIndex];
        }

        steps.Reverse();
        return steps;
    }

    private static string LocatorKey(ObservedNode node) => node.AutomationId.Length > 0 ? node.AutomationId : node.Name;

    /// <summary>
    /// For Edit/ComboBox/Spinner/Slider whose Name is empty or equals Value: the Name of the nearest
    /// preceding sibling of type Text with a non-empty name; else null.
    /// </summary>
    private static string? ResolveLabel(
        ObservedNode node, ObservedNode[] byIndex, Dictionary<int, List<int>> childrenByParent, bool[] dropped)
    {
        if (!LabelSourceTypes.Contains(node.ControlType))
            return null;
        if (node.Name.Length != 0 && node.Name != node.Value)
            return null;

        var siblings = childrenByParent[node.Parent ?? NoParent];
        var position = siblings.IndexOf(node.Index);
        for (var i = position - 1; i >= 0; i--)
        {
            var siblingIndex = siblings[i];
            if (dropped[siblingIndex])
                continue;

            var sibling = byIndex[siblingIndex];
            if (sibling.ControlType == "Text" && sibling.Name.Length > 0)
                return sibling.Name;
        }

        return null;
    }

    /// <summary>
    /// Nearest ancestor (excluding the window root) of a panel-like type with a non-empty name;
    /// unnamed ToolBar/MenuBar fall back to fixed labels; ancestors of a panel-like type that qualify
    /// for neither are skipped in favour of one further up; null if none qualify.
    /// </summary>
    private static string? ResolvePanel(ObservedNode node, ObservedNode[] byIndex)
    {
        var current = node.Parent is int parentIndex ? byIndex[parentIndex] : null;
        while (current is not null && current.Parent is not null)
        {
            if (PanelTypes.Contains(current.ControlType))
            {
                if (current.Name.Length > 0)
                    return current.Name;
                if (current.ControlType == "ToolBar")
                    return "Toolbar";
                if (current.ControlType == "MenuBar")
                    return "Menu bar";
            }

            current = current.Parent is int nextIndex ? byIndex[nextIndex] : null;
        }

        return null;
    }

    private static string LowerHex(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(HexAlphabet[b >> 4]);
            sb.Append(HexAlphabet[b & 0xF]);
        }

        return sb.ToString();
    }
}
