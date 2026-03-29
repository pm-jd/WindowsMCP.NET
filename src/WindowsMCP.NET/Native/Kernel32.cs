using System.Runtime.InteropServices;

namespace WindowsMcpNet.Native;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    internal const uint GMEM_MOVEABLE = 0x0002;
}
