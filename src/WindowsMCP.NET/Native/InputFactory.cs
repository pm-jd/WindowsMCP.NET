using System.Runtime.InteropServices;

namespace WindowsMcpNet.Native;

/// <summary>
/// Builds and sends <c>SendInput</c> records. Shared by every tool that synthesises mouse or
/// keyboard input so the INPUT layout and the text-to-keystroke rules live in one place.
/// </summary>
internal static class InputFactory
{
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_A = 0x41;
    private const ushort VK_DELETE = 0x2E;

    public static INPUT Mouse(uint flags) => new()
    {
        Type = User32.INPUT_MOUSE,
        U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = flags } },
    };

    public static INPUT Key(ushort vk, bool keyUp) => new()
    {
        Type = User32.INPUT_KEYBOARD,
        U = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? User32.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    public static INPUT UnicodeKey(char ch, bool keyUp) => new()
    {
        Type = User32.INPUT_KEYBOARD,
        U = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = User32.KEYEVENTF_UNICODE | (keyUp ? User32.KEYEVENTF_KEYUP : 0),
            },
        },
    };

    /// <summary>
    /// Text to keystrokes: printable characters as Unicode events (layout independent);
    /// '\n' becomes an Enter keystroke and '\t' a Tab keystroke, because most controls ignore
    /// a raw U+000A/U+0009 Unicode event; '\r' is dropped so CRLF yields a single Enter.
    /// </summary>
    public static INPUT[] BuildTextInputs(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    continue;
                case '\n':
                    inputs.Add(Key(VK_RETURN, keyUp: false));
                    inputs.Add(Key(VK_RETURN, keyUp: true));
                    break;
                case '\t':
                    inputs.Add(Key(VK_TAB, keyUp: false));
                    inputs.Add(Key(VK_TAB, keyUp: true));
                    break;
                default:
                    inputs.Add(UnicodeKey(ch, keyUp: false));
                    inputs.Add(UnicodeKey(ch, keyUp: true));
                    break;
            }
        }
        return inputs.ToArray();
    }

    public static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void Click(uint downFlag, uint upFlag) => Send(Mouse(downFlag), Mouse(upFlag));

    /// <summary>Moves the cursor and performs a single left click in one atomic <c>SendInput</c> batch —
    /// the common case of <c>InputTools.Click</c>'s mouse path and the pattern-fallback click in
    /// <c>ActionExecutor</c>.</summary>
    public static void LeftClickAt(int x, int y)
    {
        User32.SetCursorPos(x, y);
        Click(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
    }

    /// <summary>Ctrl+A, Delete — the "clear" idiom sent before typing when <c>clear</c> is requested.</summary>
    public static void SelectAllAndDelete()
    {
        Send(
            Key(VK_CONTROL, keyUp: false),
            Key(VK_A, keyUp: false),
            Key(VK_A, keyUp: true),
            Key(VK_CONTROL, keyUp: true),
            Key(VK_DELETE, keyUp: false),
            Key(VK_DELETE, keyUp: true));
    }

    /// <summary>A single Enter keystroke — sent after typing when <c>press_enter</c> is requested.</summary>
    public static void PressEnter() => Send(Key(VK_RETURN, keyUp: false), Key(VK_RETURN, keyUp: true));
}
