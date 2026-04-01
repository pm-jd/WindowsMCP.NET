using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ClipboardTools
{
    [McpServerTool(Name = "Clipboard", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Get or set the Windows clipboard text content. mode: get or set.")]
    public static string Clipboard(
        [Description("Mode: get or set")] string mode,
        [Description("Text to place on clipboard (required for mode=set)")] string? text = null)
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "get" => ClipboardGet(),
                "set" => ClipboardSet(text),
                _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: get or set.")
            };
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ClipboardGet()
    {
        if (!User32.OpenClipboard(nint.Zero))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            var hMem = User32.GetClipboardData(User32.CF_UNICODETEXT);
            if (hMem == nint.Zero)
                return "(clipboard is empty or not text)";

            var ptr = Kernel32.GlobalLock(hMem);
            if (ptr == nint.Zero)
                throw new InvalidOperationException("GlobalLock failed.");

            try
            {
                return Marshal.PtrToStringUni(ptr) ?? "";
            }
            finally
            {
                Kernel32.GlobalUnlock(hMem);
            }
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    private static string ClipboardSet(string? text)
    {
        if (text is null)
            throw new ArgumentException("'text' is required for mode=set.");

        if (!User32.OpenClipboard(nint.Zero))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            User32.EmptyClipboard();

            // Allocate global memory: (length + 1) wide chars (null terminator)
            int byteCount = (text.Length + 1) * 2;
            var hMem = Kernel32.GlobalAlloc(Kernel32.GMEM_MOVEABLE, (nuint)byteCount);
            if (hMem == nint.Zero)
                throw new OutOfMemoryException("GlobalAlloc failed.");

            var ptr = Kernel32.GlobalLock(hMem);
            if (ptr == nint.Zero)
            {
                Kernel32.GlobalFree(hMem);
                throw new InvalidOperationException("GlobalLock failed.");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                // Write null terminator
                Marshal.WriteInt16(ptr, text.Length * 2, 0);
            }
            finally
            {
                Kernel32.GlobalUnlock(hMem);
            }

            var result = User32.SetClipboardData(User32.CF_UNICODETEXT, hMem);
            if (result == nint.Zero)
            {
                Kernel32.GlobalFree(hMem);
                throw new InvalidOperationException("SetClipboardData failed.");
            }

            return $"Clipboard set ({text.Length} character(s))";
        }
        finally
        {
            User32.CloseClipboard();
        }
    }
}
