using System.Drawing;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tests.TestSupport;

/// <summary>Both fakes append to one shared, ordered log so cross-object call order is assertable.
/// Only calls that DO something are logged (pattern calls, SetValue, SetFocus, mouse, keyboard);
/// state reads (enabled, focus, rect, hit-test, value) are not. The guard defaults are the safe ones —
/// enabled, focused, not covered — so a test that does not care about a guard is not affected by it.</summary>
internal sealed class FakeActionTarget(List<string> log) : IActionTarget
{
    private Rectangle _rect = new(0, 0, 10, 10);

    public string ControlType { get; init; } = "Edit";
    public bool IsEnabled { get; init; } = true;
    public bool HasChildren { get; init; }
    public Rectangle? RectAfterPattern { get; init; }
    public bool CanSetValue { get; init; }

    /// <summary>Throws like a live target whose element has vanished.</summary>
    public Rectangle CurrentRect
    {
        get => RectGone ? throw new ElementNotFoundException("e0gone") : _rect;
        set => _rect = value;
    }

    public bool RectGone { get; set; }
    public bool RectGoneAfterPattern { get; init; }

    /// <summary>Another window covers the click point (<see cref="OwnsPoint"/> is false).</summary>
    public bool Covered { get; set; }
    public bool CoveredAfterPattern { get; init; }

    /// <summary>What <see cref="HasKeyboardFocus"/> reports, unless <see cref="FocusArrivesWithClick"/> is set.</summary>
    public bool KeyboardFocus { get; set; } = true;

    /// <summary>Keyboard focus only arrives once a mouse click has been logged (SetFocus alone is not enough).</summary>
    public bool FocusArrivesWithClick { get; init; }

    public bool InvokeResult { get; init; } = true;
    public bool ExpandCollapseResult { get; init; } = true;
    public bool ToggleResult { get; init; } = true;
    public bool SelectResult { get; init; } = true;
    public bool SetValueResult { get; init; } = true;
    public bool FocusResult { get; init; } = true;
    public bool ReadThrows { get; init; }

    public Queue<string?> ReadValues { get; } = new();
    public string? LastSetValue { get; private set; }

    public bool HasKeyboardFocus =>
        FocusArrivesWithClick ? log.Exists(l => l.StartsWith("LeftClick", StringComparison.Ordinal)) : KeyboardFocus;

    public bool OwnsPoint(Point p) => !Covered;

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
            _rect = r;
        if (RectGoneAfterPattern)
            RectGone = true;
        if (CoveredAfterPattern)
            Covered = true;
        return result;
    }
}

internal sealed class FakeInputDriver(List<string> log) : IInputDriver
{
    public void LeftClick(Point p) => log.Add($"LeftClick:{p.X},{p.Y}");
    public void TypeText(string text, bool clear, bool pressEnter) => log.Add($"TypeText:{text}|{clear}|{pressEnter}");
    public void PressEnter() => log.Add("PressEnter");
}
