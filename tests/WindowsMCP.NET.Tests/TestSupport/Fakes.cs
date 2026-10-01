using System.Drawing;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tests.TestSupport;

/// <summary>Both fakes append to one shared, ordered log so cross-object call order is assertable.</summary>
internal sealed class FakeActionTarget(List<string> log) : IActionTarget
{
    public string ControlType { get; init; } = "Edit";
    public bool HasChildren { get; init; }
    public Rectangle CurrentRect { get; set; } = new(0, 0, 10, 10);
    public Rectangle? RectAfterPattern { get; init; }
    public bool CanSetValue { get; init; }

    public bool InvokeResult { get; init; } = true;
    public bool ExpandCollapseResult { get; init; } = true;
    public bool ToggleResult { get; init; } = true;
    public bool SelectResult { get; init; } = true;
    public bool SetValueResult { get; init; } = true;
    public bool FocusResult { get; init; } = true;
    public bool ReadThrows { get; init; }

    public Queue<string?> ReadValues { get; } = new();
    public string? LastSetValue { get; private set; }

    public bool TryInvoke() => Pattern("Invoke", InvokeResult);
    public bool TryExpandCollapse() => Pattern("ExpandCollapse", ExpandCollapseResult);
    public bool TryToggle() => Pattern("Toggle", ToggleResult);
    public bool TrySelect() => Pattern("Select", SelectResult);

    public bool TrySetValue(string value)
    {
        log.Add("SetValue");
        LastSetValue = value;
        return SetValueResult;
    }

    public string? ReadValue() => ReadThrows ? throw new InvalidOperationException("read failed") : ReadValues.Count > 0 ? ReadValues.Dequeue() : null;

    public bool TryFocus() { log.Add("Focus"); return FocusResult; }

    private bool Pattern(string name, bool result)
    {
        log.Add(name);
        if (RectAfterPattern is { } r)
            CurrentRect = r;
        return result;
    }
}

internal sealed class FakeInputDriver(List<string> log) : IInputDriver
{
    public void LeftClick(Point p) => log.Add($"LeftClick:{p.X},{p.Y}");
    public void TypeText(string text, bool clear, bool pressEnter) => log.Add($"TypeText:{text}|{clear}|{pressEnter}");
    public void PressEnter() => log.Add("PressEnter");
}
