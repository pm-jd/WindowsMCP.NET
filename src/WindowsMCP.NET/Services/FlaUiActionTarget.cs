using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace WindowsMcpNet.Services;

/// <summary>
/// Live <see cref="IActionTarget"/> over a FlaUI <see cref="AutomationElement"/>. Each <c>Try*</c>
/// method looks up the matching UIA pattern and swallows any exception (pattern unsupported, element
/// gone, COM failure) into a <see langword="false"/> return, so <see cref="ActionExecutor"/> can fall
/// back to the mouse/keyboard path without special-casing FlaUI errors.
/// </summary>
public sealed class FlaUiActionTarget(AutomationElement el) : IActionTarget
{
    public string ControlType => el.ControlType.ToString();

    /// <summary>At least one control-view child — the same "IsControlElement" view
    /// <see cref="ObservationService"/> walks, so this agrees with what an <c>Observe</c> call
    /// would have reported as this element's children.</summary>
    public bool HasChildren => el.FindAllChildren(ControlViewCondition()).Length > 0;

    public Rectangle CurrentRect => el.BoundingRectangle;

    public bool TryInvoke() => TryPattern(el.Patterns.Invoke.PatternOrDefault, p => p.Invoke());

    /// <summary>Expands the element, or collapses it when it is already expanded.</summary>
    public bool TryExpandCollapse() => TryPattern(el.Patterns.ExpandCollapse.PatternOrDefault, p =>
    {
        if (p.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Expanded)
            p.Collapse();
        else
            p.Expand();
    });

    public bool TryToggle() => TryPattern(el.Patterns.Toggle.PatternOrDefault, p => p.Toggle());

    public bool TrySelect() => TryPattern(el.Patterns.SelectionItem.PatternOrDefault, p => p.Select());

    public bool CanSetValue
    {
        get
        {
            var pattern = el.Patterns.Value.PatternOrDefault;
            return pattern is not null && !pattern.IsReadOnly.ValueOrDefault;
        }
    }

    public bool TrySetValue(string value) => TryPattern(el.Patterns.Value.PatternOrDefault, p => p.SetValue(value));

    public string? ReadValue() => el.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;

    public bool TryFocus()
    {
        try
        {
            el.Focus();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryPattern<TPattern>(TPattern? pattern, Action<TPattern> act) where TPattern : class
    {
        if (pattern is null)
            return false;

        try
        {
            act(pattern);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The same "control view" filter <see cref="ObservationService"/>'s collecting
    /// <c>CacheRequest</c> applies (<c>IsControlElement = true</c>).</summary>
    private PropertyCondition ControlViewCondition() =>
        new(el.Automation.PropertyLibrary.Element.IsControlElement, true);
}
