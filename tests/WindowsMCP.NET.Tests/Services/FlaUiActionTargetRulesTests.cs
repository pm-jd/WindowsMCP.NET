using WindowsMcpNet.Native;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// The pure rules behind "which window does a click at this point reach": <see cref="WindowHit"/>
/// (shared by the click guard and by Observe's hit-test) and the two rules of
/// <see cref="FlaUiActionTarget.OwnsPoint"/>. The Win32/UIA reads that feed them (<c>WindowFromPoint</c>,
/// <c>GetAncestor</c>, <c>IsChild</c>, <c>NativeWindowHandle</c>) need a desktop; the decisions do not.
/// </summary>
public class FlaUiActionTargetRulesTests
{
    private const int TopLevel = 0x10;   // the window the element was resolved in
    private const int Control = 0x20;    // the element's own native window (or that of its nearest ancestor)
    private const int Inner = 0x21;      // a child window of Control
    private const int Sibling = 0x30;    // another control of the same top-level window
    private const int Other = 0x90;      // another top-level window

    [Theory]
    [InlineData(0x20, 0x20, true)]    // the element's own window is what a click there would hit
    [InlineData(0x30, 0x20, false)]   // another application — or a popup, menu or dialog of the same one — lies on top
    [InlineData(0, 0x20, false)]      // no window at that point (off screen)
    [InlineData(0, 0, false)]         // nothing known about the element's window: "no window" must not match it
    public void IsInWindow_OnlyTheTopLevelWindowItself(int topLevelAtPoint, int window, bool expected) =>
        Assert.Equal(expected, WindowHit.IsInWindow(topLevelAtPoint, window));

    // --- OwnsWindow (BF3): the click point proves the control, not only the window -------------------

    /// <summary>Child windows of <see cref="Control"/> and of <see cref="TopLevel"/>, as <c>IsChild</c> answers.</summary>
    private static bool IsChild(nint parent, nint window) =>
        (parent == Control && window == Inner)
        || (parent == TopLevel && window is Control or Inner or Sibling);

    private static bool Owns(int windowAtPoint, int topLevelAtPoint, int nativeWindow,
        bool nativeContainsPoint = true, bool offscreen = false) =>
        FlaUiActionTarget.OwnsWindow(windowAtPoint, topLevelAtPoint, TopLevel,
            () => nativeWindow, _ => nativeContainsPoint, () => offscreen, IsChild);

    [Theory]
    // The native window is the element's own HWND (dialog button, edit field, check box) or, for a
    // windowless element, that of the nearest ancestor that has one (toolbar button in its ToolStrip,
    // docking tab in its tab strip).
    [InlineData(Control, TopLevel, Control, true)]    // the point is in that window itself
    [InlineData(Inner, TopLevel, Control, true)]      // ... or in a child window of it (the edit of a combo box)
    [InlineData(Sibling, TopLevel, Control, false)]   // another control's window lies there: overlap, or the native window is clipped away at that point
    [InlineData(TopLevel, TopLevel, Control, false)]  // the native window is hidden, disabled or clipped away: an ancestor takes the click
    // The native window is the top-level window itself (title-bar button, menu item of a popup).
    [InlineData(TopLevel, TopLevel, TopLevel, true)]  // non-client area, or content drawn on the window
    [InlineData(Sibling, TopLevel, TopLevel, true)]   // native window = top-level window: every child of it passes
    // Not even the right top-level window.
    [InlineData(Other, Other, Control, false)]        // another application, or a popup/dialog of the same one
    [InlineData(0, 0, Control, false)]                // no window at the point
    public void OwnsWindow_ThePointMustBeInTheElementsNativeWindow_OrAChildOfIt(
        int windowAtPoint, int topLevelAtPoint, int nativeWindow, bool expected) =>
        Assert.Equal(expected, Owns(windowAtPoint, topLevelAtPoint, nativeWindow));

    [Theory]
    [InlineData(Control)]
    [InlineData(Sibling)]
    [InlineData(TopLevel)]
    public void OwnsWindow_NativeWindowUnknown_OnlyTheTopLevelWindowIsChecked(int windowAtPoint) =>
        Assert.True(Owns(windowAtPoint, TopLevel, nativeWindow: 0));

    [Theory]
    [InlineData(Sibling)]    // Windows 11 task bar: the buttons hang under an InputSite window elsewhere,
    [InlineData(TopLevel)]   // and the window at their centre is the task list or the task bar itself
    public void OwnsWindow_NativeWindowDoesNotContainThePoint_NotOffscreen_OnlyTheTopLevelWindowIsChecked(int windowAtPoint)
    {
        // The window found by walking up the UI tree does not even contain the click point, so it is
        // not where the element is drawn (XAML islands): the window check cannot be made.
        Assert.True(Owns(windowAtPoint, TopLevel, Control, nativeContainsPoint: false, offscreen: false));
        Assert.False(Owns(Other, Other, Control, nativeContainsPoint: false)); // the top-level check still holds
    }

    [Theory]
    [InlineData(Sibling)]
    [InlineData(TopLevel)]
    public void OwnsWindow_NativeWindowDoesNotContainThePoint_TargetIsOffscreen_IsRefused(int windowAtPoint)
    {
        // The other way to be outside one's native window: scrolled or overflowed out of it — a list,
        // tree or data item, a toolbar button, a tab. The element says so itself (IsOffscreen), and
        // whatever lies at that point is not the element.
        Assert.False(Owns(windowAtPoint, TopLevel, Control, nativeContainsPoint: false, offscreen: true));
    }

    [Theory]
    [InlineData(Control)]   // the point is in the native window: the window check decides
    [InlineData(0)]         // native window unknown: top-level check only, as before
    public void OwnsWindow_IsOffscreen_IsOnlyAskedWhenTheNativeWindowDoesNotContainThePoint(int nativeWindow)
    {
        var asked = false;

        var owns = FlaUiActionTarget.OwnsWindow(Control, TopLevel, TopLevel,
            () => nativeWindow, _ => true, () => { asked = true; return true; }, IsChild);

        Assert.True(owns);
        Assert.False(asked);
    }

    [Fact]
    public void OwnsWindow_WrongTopLevelWindow_DoesNotEvenLookForTheNativeWindow()
    {
        // The native window costs UIA calls (a walk up the tree); it is only determined when needed.
        var asked = false;

        var owns = FlaUiActionTarget.OwnsWindow(Other, Other, TopLevel,
            () => { asked = true; return Control; }, _ => { asked = true; return true; },
            () => { asked = true; return false; }, IsChild);

        Assert.False(owns);
        Assert.False(asked);
    }

    // --- NearestWindow (BF3): the element's own native window -----------------------------------------

    private sealed record Node(int Window, Node? Parent);

    private static nint Nearest(Node? start, int maxDepth = 64) =>
        FlaUiActionTarget.NearestWindow(start, n => n.Window, n => n.Parent, maxDepth);

    [Fact]
    public void NearestWindow_ControlWithItsOwnWindow_IsThatWindow()
    {
        var button = new Node(Control, new Node(TopLevel, null));

        Assert.Equal((nint)Control, Nearest(button));
    }

    [Fact]
    public void NearestWindow_WindowlessElement_IsTheWindowOfTheNearestAncestorThatHasOne()
    {
        // TabItem (windowless) -> Tab (windowless) -> tab strip control -> top-level window
        var tabItem = new Node(0, new Node(0, new Node(Control, new Node(TopLevel, null))));

        Assert.Equal((nint)Control, Nearest(tabItem));
    }

    [Fact]
    public void NearestWindow_WindowlessElementDirectlyInTheTopLevelWindow_IsTheTopLevelWindow()
    {
        // Close button -> TitleBar -> Window
        var closeButton = new Node(0, new Node(0, new Node(TopLevel, null)));

        Assert.Equal((nint)TopLevel, Nearest(closeButton));
    }

    [Fact]
    public void NearestWindow_NoneFound_IsZero()
    {
        Assert.Equal(nint.Zero, Nearest(null));
        Assert.Equal(nint.Zero, Nearest(new Node(0, new Node(0, null))));
        // The depth bound ends a walk through a misbehaving provider before it reaches a window.
        Assert.Equal(nint.Zero, Nearest(new Node(0, new Node(0, new Node(Control, null))), maxDepth: 2));
    }
}
