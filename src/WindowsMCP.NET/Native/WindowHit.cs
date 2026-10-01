using System.Drawing;

namespace WindowsMcpNet.Native;

/// <summary>
/// The one definition of "the window at a screen point" — the window a mouse click there reaches.
/// Plain Win32 (<c>WindowFromPoint</c>), no UI Automation: UIA's hit-test is unreliable in popups,
/// menus and dialogs (and for docking tab items), costs a cross-process call and cannot answer while
/// the application is busy. Shared by the click guard of the element actions
/// (<c>FlaUiActionTarget.OwnsPoint</c>) and by the visibility hit-test of <c>Observe</c>.
/// </summary>
internal static class WindowHit
{
    /// <summary>The deepest window at <paramref name="p"/> that takes mouse input (disabled, hidden
    /// and hit-test-transparent windows are passed over); zero when there is none.</summary>
    public static nint WindowAt(Point p) => User32.WindowFromPoint(new POINT { X = p.X, Y = p.Y });

    /// <summary>The top-level window <paramref name="window"/> belongs to — itself when it is one.</summary>
    public static nint TopLevelOf(nint window) => User32.GetAncestor(window, User32.GA_ROOT);

    /// <summary>Whether <paramref name="p"/> lies in the rectangle of <paramref name="window"/> (false
    /// when the window is gone).</summary>
    public static bool Contains(nint window, Point p) =>
        User32.GetWindowRect(window, out var rect)
        && p.X >= rect.Left && p.X < rect.Right && p.Y >= rect.Top && p.Y < rect.Bottom;

    /// <summary>The rule both callers share: the top-level window found at a point is
    /// <paramref name="window"/>. "No window there" never matches — not even an unknown window.</summary>
    public static bool IsInWindow(nint topLevelAtPoint, nint window) =>
        window != nint.Zero && topLevelAtPoint == window;
}
