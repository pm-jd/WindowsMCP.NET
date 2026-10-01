using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class InputTools
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_OEM_PLUS = 0xBB;

    // Named keys for the Shortcut tool (case-insensitive). Letters and digits map via their char code.
    private static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VK_CONTROL, ["control"] = VK_CONTROL,
        ["alt"] = 0x12, ["shift"] = 0x10,
        ["win"] = 0x5B, ["lwin"] = 0x5B, ["rwin"] = 0x5C,
        ["tab"] = 0x09, ["enter"] = VK_RETURN, ["return"] = VK_RETURN,
        ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = VK_DELETE, ["del"] = VK_DELETE, ["insert"] = 0x2D,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["snapshot"] = 0x2C,
        ["capslock"] = 0x14, ["numlock"] = 0x90, ["scrolllock"] = 0x91, ["pause"] = 0x13,
        ["apps"] = 0x5D, ["menu"] = 0x5D,
        ["+"] = VK_OEM_PLUS, ["plus"] = VK_OEM_PLUS, ["="] = VK_OEM_PLUS,
        ["-"] = 0xBD, ["minus"] = 0xBD,
        [","] = 0xBC, ["comma"] = 0xBC,
        ["."] = 0xBE, ["period"] = 0xBE,
        ["/"] = 0xBF, ["slash"] = 0xBF,
        [";"] = 0xBA, ["semicolon"] = 0xBA,
        ["'"] = 0xDE, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC, ["`"] = 0xC0,
        ["numpad0"] = 0x60, ["numpad1"] = 0x61, ["numpad2"] = 0x62, ["numpad3"] = 0x63, ["numpad4"] = 0x64,
        ["numpad5"] = 0x65, ["numpad6"] = 0x66, ["numpad7"] = 0x67, ["numpad8"] = 0x68, ["numpad9"] = 0x69,
        ["multiply"] = 0x6A, ["add"] = 0x6B, ["subtract"] = 0x6D, ["decimal"] = 0x6E, ["divide"] = 0x6F,
        ["volumemute"] = 0xAD, ["volumedown"] = 0xAE, ["volumeup"] = 0xAF,
        ["medianext"] = 0xB0, ["mediaprev"] = 0xB1, ["mediastop"] = 0xB2, ["mediaplay"] = 0xB3,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73, ["f5"] = 0x74, ["f6"] = 0x75,
        ["f7"] = 0x76, ["f8"] = 0x77, ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
        ["f13"] = 0x7C, ["f14"] = 0x7D, ["f15"] = 0x7E, ["f16"] = 0x7F, ["f17"] = 0x80, ["f18"] = 0x81,
        ["f19"] = 0x82, ["f20"] = 0x83, ["f21"] = 0x84, ["f22"] = 0x85, ["f23"] = 0x86, ["f24"] = 0x87,
    };

    [McpServerTool(Name = "Click", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click at coordinates or a labeled UI element.")]
    public static string Click(
        UiTreeService uiTreeService,
        [Description("Coordinate as [x, y] (ignored when label is given)")] int[]? loc = null,
        [Description("UI element label from last Snapshot (e.g. '3')")] string? label = null,
        [Description("Mouse button")] MouseButton button = MouseButton.Left,
        [Description("Number of clicks: 1 for single (default), 2 for double")] int clicks = 1)
    {
        try
        {
            var (cx, cy) = ResolveTarget(uiTreeService, label, loc)
                ?? throw new ArgumentException("Either 'label' or 'loc' ([x, y]) must be provided.");

            var actualClicks = Math.Max(1, clicks);

            if (button == MouseButton.Left && actualClicks == 1)
            {
                InputFactory.LeftClickAt(cx, cy);
            }
            else
            {
                User32.SetCursorPos(cx, cy);

                var (downFlag, upFlag) = button switch
                {
                    MouseButton.Right  => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
                    MouseButton.Middle => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
                    _                  => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
                };

                // Atomic batch: all click events in one SendInput call so Windows sees
                // consecutive timestamps within GetDoubleClickTime() for clicks=2.
                var inputs = new INPUT[actualClicks * 2];
                for (var i = 0; i < actualClicks; i++)
                {
                    inputs[i * 2]     = InputFactory.Mouse(downFlag);
                    inputs[i * 2 + 1] = InputFactory.Mouse(upFlag);
                }
                InputFactory.Send(inputs);
            }

            return $"Clicked {button.Lower()} at ({cx},{cy}){(actualClicks > 1 ? $" ({actualClicks}x)" : "")}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Type", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Type text, optionally clicking a target element first. Newlines are sent as Enter, tabs as Tab.")]
    public static string Type(
        UiTreeService uiTreeService,
        [Description("Text to type")] string text,
        [Description("Optional: click this label before typing")] string? label = null,
        [Description("Coordinate to click before typing as [x, y]")] int[]? loc = null,
        [Description("Select all (Ctrl+A then Delete) before typing")] bool clear = false,
        [Description("Press Enter after typing")] bool press_enter = false)
    {
        try
        {
            if (ResolveTarget(uiTreeService, label, loc) is { } target)
            {
                User32.SetCursorPos(target.X, target.Y);
                InputFactory.Click(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
            }

            if (clear)
                InputFactory.SelectAllAndDelete();

            InputFactory.Send(InputFactory.BuildTextInputs(text));

            if (press_enter)
                InputFactory.PressEnter();

            return $"Typed {text.Length} character(s){(press_enter ? " + Enter" : "")}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Scroll", Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Scroll mouse wheel at coordinates or labeled element.")]
    public static string Scroll(
        UiTreeService uiTreeService,
        [Description("Scroll direction; left/right imply horizontal scrolling")] ScrollDirection direction = ScrollDirection.Down,
        [Description("Number of scroll notches")] int wheel_times = 3,
        [Description("Coordinate as [x, y]")] int[]? loc = null,
        [Description("UI element label")] string? label = null,
        [Description("Scroll axis")] ScrollAxis type = ScrollAxis.Vertical)
    {
        try
        {
            var (cx, cy) = ResolveTarget(uiTreeService, label, loc) ?? (0, 0);
            if (cx != 0 || cy != 0)
                User32.SetCursorPos(cx, cy);

            var isHorizontal = type == ScrollAxis.Horizontal
                || direction is ScrollDirection.Left or ScrollDirection.Right;

            var delta = isHorizontal
                ? (direction == ScrollDirection.Left ? -120 : 120)
                : (direction == ScrollDirection.Up ? 120 : -120);
            delta *= wheel_times;

            var input = InputFactory.Mouse(isHorizontal ? User32.MOUSEEVENTF_HWHEEL : User32.MOUSEEVENTF_WHEEL);
            input.U.mi.mouseData = (uint)delta;
            InputFactory.Send(input);

            return $"Scrolled {direction.Lower()} {wheel_times} notch(es) at ({cx},{cy})";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Move", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Move cursor to coordinates or label, with optional drag.")]
    public static string Move(
        UiTreeService uiTreeService,
        [Description("Coordinate as [x, y]")] int[]? loc = null,
        [Description("UI element label")] string? label = null,
        [Description("Drag: hold mouse button down while moving, release after")] bool drag = false)
    {
        try
        {
            var (cx, cy) = ResolveTarget(uiTreeService, label, loc)
                ?? throw new ArgumentException("Either 'label' or 'loc' ([x, y]) must be provided.");

            if (drag)
                InputFactory.Send(InputFactory.Mouse(User32.MOUSEEVENTF_LEFTDOWN));

            User32.SetCursorPos(cx, cy);

            if (drag)
                InputFactory.Send(InputFactory.Mouse(User32.MOUSEEVENTF_LEFTUP));

            return $"{(drag ? "Dragged" : "Moved")} cursor to ({cx},{cy})";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Shortcut", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Send a keyboard shortcut, e.g. 'ctrl+c', 'alt+f4', 'win+printscreen', 'ctrl++' (Ctrl and the plus key).")]
    public static string Shortcut(
        [Description("Key combination joined with '+', e.g. 'ctrl+c', 'alt+tab', 'win+d'")] string shortcut)
    {
        try
        {
            var vkCodes = ParseShortcut(shortcut);

            // Press all keys down, then release all in reverse order
            var inputs = new INPUT[vkCodes.Length * 2];
            var idx = 0;
            foreach (var vk in vkCodes)
                inputs[idx++] = InputFactory.Key(vk, keyUp: false);
            for (var i = vkCodes.Length - 1; i >= 0; i--)
                inputs[idx++] = InputFactory.Key(vkCodes[i], keyUp: true);

            InputFactory.Send(inputs);
            return $"Sent shortcut: {shortcut}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Wait", ReadOnly = true, Idempotent = true)]
    [Description("Wait for a specified duration in seconds.")]
    public static async Task<string> Wait(
        [Description("Duration in seconds to wait (max 10)")] int duration,
        CancellationToken ct = default)
    {
        try
        {
            duration = Math.Clamp(duration, 0, 10);
            await Task.Delay(TimeSpan.FromSeconds(duration), ct);
            return $"Waited {duration}s";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client cancelled the request; the SDK reports it as cancelled, not as a tool error
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    // --- Helpers ---

    /// <summary>
    /// "ctrl+shift+s" to virtual-key codes in press order. '+' separates keys; a '+' directly
    /// after a separator ("ctrl++") or the names "plus"/"=" mean the plus key itself. Single
    /// letters and digits map through their character code, everything else through the table.
    /// </summary>
    public static ushort[] ParseShortcut(string shortcut)
    {
        var parts = new List<string>();
        var token = new StringBuilder();
        for (var i = 0; i < shortcut.Length; i++)
        {
            var c = shortcut[i];
            if (c != '+')
            {
                token.Append(c);
                continue;
            }

            if (token.Length == 0)
            {
                // Empty token before a '+': a literal plus key when it follows a separator ("ctrl++").
                if (i > 0 && shortcut[i - 1] == '+')
                {
                    parts.Add("+");
                    continue;
                }
                throw new ArgumentException($"Malformed shortcut '{shortcut}': unexpected '+'.");
            }

            parts.Add(token.ToString());
            token.Clear();
        }

        if (token.Length > 0)
            parts.Add(token.ToString());
        else if (parts.Count == 0 || (shortcut.EndsWith('+') && parts[^1] != "+"))
            throw new ArgumentException($"Malformed shortcut '{shortcut}': missing key after '+'.");

        var codes = new ushort[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            if (VkMap.TryGetValue(part, out var vk))
                codes[i] = vk;
            else if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
                codes[i] = (ushort)char.ToUpperInvariant(part[0]);
            else
                throw new ArgumentException($"Unknown key name: '{part}'");
        }
        return codes;
    }

    /// <summary>
    /// Label wins over coordinates. A label that is not in the current UI tree is an error;
    /// no label and no usable coordinate yields null so callers decide whether that is allowed.
    /// </summary>
    private static (int X, int Y)? ResolveTarget(UiTreeService uiTreeService, string? label, int[]? loc)
    {
        if (label is not null)
        {
            return uiTreeService.ResolveLabel(label)
                   ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
        }
        return ToolHelpers.ToPoint(loc);
    }
}
