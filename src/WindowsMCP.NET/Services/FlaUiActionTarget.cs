using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

/// <summary>
/// Live <see cref="IActionTarget"/> over a FlaUI <see cref="AutomationElement"/>.
/// <para>
/// Which members may throw is part of the contract (see <see cref="IActionTarget"/>): only the two
/// reads that happen BEFORE an action — <see cref="IsEnabled"/> and <see cref="CurrentRect"/> — and
/// then as <see cref="ElementNotFoundException"/> for <paramref name="id"/>, with nothing done. Every
/// other member swallows a vanished element or a COM failure into its neutral answer (false / null),
/// because it can be read after an action was sent and must not turn that action into an error.
/// </para>
/// </summary>
/// <param name="el">The resolved live element.</param>
/// <param name="id">The element id it was resolved from (for <see cref="ElementNotFoundException"/>).</param>
/// <param name="controlType">The element's control type, read once when it was resolved.</param>
/// <param name="windowHandle">The top-level window the element was resolved in.</param>
public sealed class FlaUiActionTarget(AutomationElement el, string id, string controlType, nint windowHandle) : IActionTarget
{
    /// <summary>Upper bound for the walk from the focused element up to the target (a UI tree is never
    /// this deep; the bound only makes a misbehaving provider terminate).</summary>
    private const int MaxFocusWalkDepth = 64;

    public string ControlType => controlType;

    /// <summary>Read only before acting. An element that does not report IsEnabled counts as enabled;
    /// one that reports <c>false</c> is refused by the executor.</summary>
    public bool IsEnabled => ReadOrGone(() => !el.Properties.IsEnabled.TryGetValue(out var enabled) || enabled);

    /// <summary>UIA <c>IsPassword</c>; false when it cannot be read.</summary>
    public bool IsPassword => Guard(() => el.Properties.IsPassword.ValueOrDefault, false);

    /// <summary>At least one control-view child — the same "IsControlElement" view
    /// <see cref="ObservationService"/> walks, so this agrees with what an <c>Observe</c> call
    /// would have reported as this element's children.</summary>
    public bool HasChildren => Guard(() => el.FindAllChildren(ControlViewCondition()).Length > 0, false);

    /// <summary>The element's rectangle right now. Throws <see cref="ElementNotFoundException"/> when
    /// the element is gone; the executor catches that wherever an action was already sent.</summary>
    public Rectangle CurrentRect => ReadOrGone(() => el.Properties.BoundingRectangle.ValueOrDefault);

    /// <summary>Walks from the system's focused element up its control-view parents to this element.</summary>
    public bool HasKeyboardFocus => Guard(() =>
    {
        var automation = el.Automation;
        var walker = automation.TreeWalkerFactory.GetControlViewWalker();
        var current = automation.FocusedElement();
        for (var depth = 0; current is not null && depth < MaxFocusWalkDepth; depth++)
        {
            if (current.Equals(el))
                return true;

            current = walker.GetParent(current);
        }

        return false;
    }, false);

    /// <summary>Plain Win32, no UI Automation: UIA's hit-test is unreliable in popups, menus and
    /// dialogs (and for docking tab items), costs a cross-process call and cannot answer while the
    /// application is busy. <c>WindowFromPoint</c> names the window a click at <paramref name="p"/>
    /// would go to; its top-level ancestor must be the window the element was resolved in.</summary>
    public bool OwnsPoint(Point p) => IsResolvedWindow(
        User32.GetAncestor(User32.WindowFromPoint(new POINT { X = p.X, Y = p.Y }), User32.GA_ROOT), windowHandle);

    /// <summary>The rule behind <see cref="OwnsPoint"/>: the top-level window found at the point is the
    /// one the element was resolved in. "No window there" never matches — not even an unknown window.</summary>
    internal static bool IsResolvedWindow(nint topLevelAtPoint, nint resolvedWindow) =>
        resolvedWindow != nint.Zero && topLevelAtPoint == resolvedWindow;

    public PatternCallResult TryInvoke() => Attempt(() => el.Patterns.Invoke.PatternOrDefault, p => p.Invoke());

    /// <summary>Expands the element, or collapses it when it is already expanded.</summary>
    public PatternCallResult TryExpandCollapse() => Attempt(() => el.Patterns.ExpandCollapse.PatternOrDefault, p =>
    {
        if (p.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Expanded)
            p.Collapse();
        else
            p.Expand();
    });

    public PatternCallResult TryToggle() => Attempt(() => el.Patterns.Toggle.PatternOrDefault, p => p.Toggle());

    public PatternCallResult TrySelect() => Attempt(() => el.Patterns.SelectionItem.PatternOrDefault, p => p.Select());

    public bool CanSetValue => Guard(() => WritableValuePattern() is not null, false);

    public PatternCallResult TrySetValue(string value) => Attempt(WritableValuePattern, p => p.SetValue(value));

    /// <summary>The element's current value — never that of a password field: <see langword="null"/>
    /// there, so the secret is not read (<see cref="ActionExecutor.Type"/> reports such a field as
    /// not verified, see <see cref="IsPassword"/>).</summary>
    public string? ReadValue() => Guard(
        () => el.Properties.IsPassword.ValueOrDefault ? null : el.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault,
        null);

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

    private IValuePattern? WritableValuePattern()
    {
        var pattern = el.Patterns.Value.PatternOrDefault;
        return pattern is not null && !pattern.IsReadOnly.ValueOrDefault ? pattern : null;
    }

    /// <summary>See <see cref="PatternCall.Attempt"/>.</summary>
    private static PatternCallResult Attempt<TPattern>(Func<TPattern?> getPattern, Action<TPattern> call) where TPattern : class =>
        PatternCall.Attempt(getPattern, call, PatternCall.DefaultLimit);

    /// <summary>Live reads can throw once the element has vanished (e.g. after the action closed its
    /// window); the action itself already happened, so a failed read must not turn into an error.</summary>
    private static T Guard<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>For the reads that precede an action: a dead element is reported as exactly that.</summary>
    private T ReadOrGone<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            throw new ElementNotFoundException(id);
        }
    }

    /// <summary>The same "control view" filter <see cref="ObservationService"/>'s collecting
    /// <c>CacheRequest</c> applies (<c>IsControlElement = true</c>).</summary>
    private PropertyCondition ControlViewCondition() =>
        new(el.Automation.PropertyLibrary.Element.IsControlElement, true);
}
