namespace WindowsMcpNet.Models;

public sealed class UiElementNode
{
    public required string Name { get; init; }
    public required string ControlType { get; init; }
    public string? AutomationId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public bool IsInteractive { get; init; }
    public string? Label { get; set; }
    public List<UiElementNode> Children { get; init; } = [];
}
