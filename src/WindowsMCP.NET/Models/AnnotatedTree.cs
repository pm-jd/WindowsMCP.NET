namespace WindowsMcpNet.Models;

public sealed class AnnotatedTree
{
    public required List<UiElementNode> Roots { get; init; }
    public required Dictionary<string, UiElementNode> LabelMap { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var root in Roots)
            AppendNode(sb, root, indent: 0);
        return sb.ToString();
    }

    private static void AppendNode(System.Text.StringBuilder sb, UiElementNode node, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var label = node.Label is not null ? $"[{node.Label}] " : "";
        sb.AppendLine($"{prefix}{label}{node.ControlType}: \"{node.Name}\" ({node.X},{node.Y} {node.Width}x{node.Height})");
        foreach (var child in node.Children)
            AppendNode(sb, child, indent + 1);
    }
}
