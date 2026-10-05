using System.Runtime.InteropServices;

namespace WindowsMcpNet.Native;

internal static partial class Dwmapi
{
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>The window's visible frame, without the invisible resize borders GetWindowRect includes.</summary>
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>Non-zero for windows DWM hides although they are "visible" (suspended UWP apps, other virtual desktops).</summary>
    internal const int DWMWA_CLOAKED = 14;
}
