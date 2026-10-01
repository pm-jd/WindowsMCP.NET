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
    /// <summary>Upper bound for a walk up the UI tree — from the focused element to the target, from
    /// the target to its native window (a UI tree is never this deep; the bound only makes a
    /// misbehaving provider terminate).</summary>
    private const int MaxWalkDepth = 64;

    private nint? _nativeWindow;

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
        for (var depth = 0; current is not null && depth < MaxWalkDepth; depth++)
        {
            if (current.Equals(el))
                return true;

            current = walker.GetParent(current);
        }

        return false;
    }, false);

    /// <summary>SelectionItem <c>IsSelected</c>; null when the element has no such pattern, does not
    /// report the property, or the read fails (it is read after a Select was sent).</summary>
    public bool? IsSelected => Guard<bool?>(
        () => el.Patterns.SelectionItem.PatternOrDefault is { } pattern && pattern.IsSelected.TryGetValue(out var selected)
            ? selected
            : null,
        null);

    /// <summary><see cref="WindowHit"/> names the window a click at <paramref name="p"/> would go to;
    /// <see cref="OwnsWindow"/> decides whether that is this element.</summary>
    public bool OwnsPoint(Point p)
    {
        var windowAtPoint = WindowHit.WindowAt(p);
        return OwnsWindow(windowAtPoint, WindowHit.TopLevelOf(windowAtPoint), windowHandle,
            () => NativeWindow, window => WindowHit.Contains(window, p), () => IsOffscreen, User32.IsChild);
    }

    /// <summary>UIA <c>IsOffscreen</c> as the element reports it right now; false when it does not
    /// report it or the read fails (it may be read after an action was sent).</summary>
    private bool IsOffscreen => Guard(() => el.Properties.IsOffscreen.TryGetValue(out var offscreen) && offscreen, false);

    /// <summary>
    /// The rule behind <see cref="OwnsPoint"/>, in the order it is checked:
    /// <list type="number">
    /// <item>The top-level window at the point must be the window the element was resolved in — not
    /// another application, and not a popup, menu or dialog of the same one.</item>
    /// <item>The element's native window (<paramref name="nativeWindow"/>, see
    /// <see cref="NearestWindow"/>) is asked for — only now, it costs UIA calls. Unknown (zero): nothing
    /// more can be checked, (1) is all.</item>
    /// <item>The native window contains the point (<paramref name="containsPoint"/>): the window at
    /// the point must be that window or a child window of it. This refuses a point where another
    /// control's window lies — a sibling window over the element, or what shows there because the
    /// native window is clipped away by a scroll container, hidden or disabled.</item>
    /// <item>The native window does NOT contain the point: it is not where the element is drawn, so
    /// (3) cannot be asked. Either the tree does not follow the windows (Windows 11 task bar, a XAML
    /// island: the buttons hang under an <c>InputSite</c> window lying elsewhere, the window at their
    /// centre is the task list) — or the element is scrolled or overflowed out of its host (list, tree
    /// and data items, toolbar buttons, tabs). The element itself tells the two apart: owned unless
    /// it reports <c>IsOffscreen</c> (<paramref name="isOffscreen"/>).</item>
    /// </list>
    /// What this does NOT prove — a click can still land on something else when: a windowless element
    /// is overlapped by another windowless element of the same native window; an element is overlapped
    /// by a child window of its own native window (it counts as "a child of it"); the native window is
    /// the top-level window itself (title-bar button, menu item of a popup: every window in it is a
    /// child of it, so (3) adds nothing to (1)); a windowless element is clipped inside its host while
    /// its centre stays within the host's rectangle.
    /// </summary>
    internal static bool OwnsWindow(
        nint windowAtPoint, nint topLevelAtPoint, nint resolvedWindow,
        Func<nint> nativeWindow, Func<nint, bool> containsPoint, Func<bool> isOffscreen, Func<nint, nint, bool> isChild)
    {
        if (!WindowHit.IsInWindow(topLevelAtPoint, resolvedWindow))
            return false;

        var native = nativeWindow();
        if (native == nint.Zero)
            return true;

        if (!containsPoint(native))
            return !isOffscreen();

        return windowAtPoint == native || isChild(native, windowAtPoint);
    }

    /// <summary>The window that draws this element and takes its clicks: its own HWND, or — for a
    /// windowless element such as a toolbar button, menu item, title-bar button or docking tab — that
    /// of the nearest ancestor that has one. Determined once, on first use; zero when it cannot be
    /// determined (the element is gone, the application does not answer).</summary>
    private nint NativeWindow => _nativeWindow ??= Guard(
        () => NearestWindow(
            el, e => e.Properties.NativeWindowHandle.ValueOrDefault,
            el.Automation.TreeWalkerFactory.GetRawViewWalker().GetParent, MaxWalkDepth),
        nint.Zero);

    /// <summary>The first non-zero window handle on the way from <paramref name="start"/> up its
    /// parents (the element itself included); zero when there is none within <paramref name="maxDepth"/>
    /// elements.</summary>
    internal static nint NearestWindow<TElement>(
        TElement? start, Func<TElement, nint> windowOf, Func<TElement, TElement?> parentOf, int maxDepth)
        where TElement : class
    {
        var current = start;
        for (var depth = 0; current is not null && depth < maxDepth; depth++)
        {
            var window = windowOf(current);
            if (window != nint.Zero)
                return window;

            current = parentOf(current);
        }

        return nint.Zero;
    }

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
