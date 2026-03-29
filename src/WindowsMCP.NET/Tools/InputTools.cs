using System.ComponentModel;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class InputTools
{
    // VK code map for Shortcut tool
    private static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"]      = 0x11,
        ["control"]   = 0x11,
        ["alt"]       = 0x12,
        ["shift"]     = 0x10,
        ["win"]       = 0x5B,
        ["tab"]       = 0x09,
        ["enter"]     = 0x0D,
        ["return"]    = 0x0D,
        ["esc"]       = 0x1B,
        ["escape"]    = 0x1B,
        ["space"]     = 0x20,
        ["backspace"] = 0x08,
        ["delete"]    = 0x2E,
        ["del"]       = 0x2E,
        ["up"]        = 0x26,
        ["down"]      = 0x28,
        ["left"]      = 0x25,
        ["right"]     = 0x27,
        ["home"]      = 0x24,
        ["end"]       = 0x23,
        ["pageup"]    = 0x21,
        ["pagedown"]  = 0x22,
        ["insert"]    = 0x2D,
        ["f1"]        = 0x70,
        ["f2"]        = 0x71,
        ["f3"]        = 0x72,
        ["f4"]        = 0x73,
        ["f5"]        = 0x74,
        ["f6"]        = 0x75,
        ["f7"]        = 0x76,
        ["f8"]        = 0x77,
        ["f9"]        = 0x78,
        ["f10"]       = 0x79,
        ["f11"]       = 0x7A,
        ["f12"]       = 0x7B,
    };

    [McpServerTool(Name = "Click", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click at a screen coordinate or on a UI element identified by label. " +
                 "button: left (default), right, middle. double: double-click if true.")]
    public static string Click(
        UiTreeService uiTreeService,
        [Description("X coordinate (ignored when label is given)")] int? x = null,
        [Description("Y coordinate (ignored when label is given)")] int? y = null,
        [Description("UI element label from last Snapshot (e.g. '3')")] string? label = null,
        [Description("Mouse button: left, right, middle")] string button = "left",
        [Description("Double-click")] bool @double = false)
    {
        int cx, cy;
        if (label is not null)
        {
            var pos = uiTreeService.ResolveLabel(label)
                      ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
            (cx, cy) = pos;
        }
        else if (x.HasValue && y.HasValue)
        {
            (cx, cy) = (x.Value, y.Value);
        }
        else
        {
            throw new ArgumentException("Either 'label' or both 'x' and 'y' must be provided.");
        }

        User32.SetCursorPos(cx, cy);

        var (downFlag, upFlag) = button.ToLowerInvariant() switch
        {
            "right"  => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
            "middle" => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
            _        => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
        };

        int clicks = @double ? 2 : 1;
        for (int i = 0; i < clicks; i++)
        {
            SendMouseClick(downFlag, upFlag);
        }

        return $"Clicked {button} at ({cx},{cy}){(@double ? " (double)" : "")}";
    }

    [McpServerTool(Name = "Type", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Type text using Unicode key events. Optionally click a label first.")]
    public static string Type(
        UiTreeService uiTreeService,
        [Description("Text to type")] string text,
        [Description("Optional: click this label before typing")] string? label = null)
    {
        if (label is not null)
        {
            var pos = uiTreeService.ResolveLabel(label)
                      ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
            User32.SetCursorPos(pos.X, pos.Y);
            SendMouseClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
        }

        var inputs = new INPUT[text.Length * 2];
        int idx = 0;
        foreach (char ch in text)
        {
            inputs[idx++] = MakeUnicodeKey(ch, keyUp: false);
            inputs[idx++] = MakeUnicodeKey(ch, keyUp: true);
        }

        User32.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        return $"Typed {text.Length} character(s)";
    }

    [McpServerTool(Name = "Scroll", Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Scroll the mouse wheel at given coordinates or label. direction: up or down, amount: notches.")]
    public static string Scroll(
        UiTreeService uiTreeService,
        [Description("Scroll direction: up or down")] string direction = "down",
        [Description("Number of scroll notches")] int amount = 3,
        [Description("X coordinate")] int? x = null,
        [Description("Y coordinate")] int? y = null,
        [Description("UI element label")] string? label = null)
    {
        int cx = x ?? 0, cy = y ?? 0;
        if (label is not null)
        {
            var pos = uiTreeService.ResolveLabel(label)
                      ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
            (cx, cy) = (pos.X, pos.Y);
        }
        else if (x.HasValue && y.HasValue)
        {
            (cx, cy) = (x.Value, y.Value);
        }

        if (cx != 0 || cy != 0)
            User32.SetCursorPos(cx, cy);

        int delta = direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? 120 : -120;
        delta *= amount;

        var input = new INPUT
        {
            Type = User32.INPUT_MOUSE,
            U = new INPUT_UNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = User32.MOUSEEVENTF_WHEEL,
                    mouseData = (uint)delta,
                }
            }
        };
        User32.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        return $"Scrolled {direction} {amount} notch(es) at ({cx},{cy})";
    }

    [McpServerTool(Name = "Move", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Move the mouse cursor to a coordinate or UI element label.")]
    public static string Move(
        UiTreeService uiTreeService,
        [Description("X coordinate")] int? x = null,
        [Description("Y coordinate")] int? y = null,
        [Description("UI element label")] string? label = null)
    {
        int cx, cy;
        if (label is not null)
        {
            var pos = uiTreeService.ResolveLabel(label)
                      ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
            (cx, cy) = (pos.X, pos.Y);
        }
        else if (x.HasValue && y.HasValue)
        {
            (cx, cy) = (x.Value, y.Value);
        }
        else
        {
            throw new ArgumentException("Either 'label' or both 'x' and 'y' must be provided.");
        }

        User32.SetCursorPos(cx, cy);
        return $"Moved cursor to ({cx},{cy})";
    }

    [McpServerTool(Name = "Shortcut", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Send a keyboard shortcut. Format: 'ctrl+c', 'alt+f4', 'ctrl+shift+s', etc.")]
    public static string Shortcut(
        [Description("Key combination, e.g. 'ctrl+c', 'alt+tab', 'win+d'")] string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vkCodes = new ushort[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (VkMap.TryGetValue(part, out var vk))
            {
                vkCodes[i] = vk;
            }
            else if (part.Length == 1)
            {
                vkCodes[i] = (ushort)char.ToUpperInvariant(part[0]);
            }
            else
            {
                throw new ArgumentException($"Unknown key name: '{part}'");
            }
        }

        // Press all keys down, then release all in reverse order
        var inputs = new INPUT[vkCodes.Length * 2];
        int idx = 0;
        foreach (var vk in vkCodes)
            inputs[idx++] = MakeVkKey(vk, keyUp: false);
        for (int i = vkCodes.Length - 1; i >= 0; i--)
            inputs[idx++] = MakeVkKey(vkCodes[i], keyUp: true);

        User32.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        return $"Sent shortcut: {keys}";
    }

    [McpServerTool(Name = "Wait", ReadOnly = true, Idempotent = true)]
    [Description("Wait for a specified number of milliseconds.")]
    public static async Task<string> Wait(
        [Description("Milliseconds to wait (max 10000)")] int ms = 500)
    {
        ms = Math.Clamp(ms, 0, 10_000);
        await Task.Delay(ms);
        return $"Waited {ms}ms";
    }

    // --- Helpers ---

    private static void SendMouseClick(uint downFlag, uint upFlag)
    {
        var inputs = new INPUT[]
        {
            new INPUT { Type = User32.INPUT_MOUSE, U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = downFlag } } },
            new INPUT { Type = User32.INPUT_MOUSE, U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = upFlag } } },
        };
        User32.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeUnicodeKey(char ch, bool keyUp)
    {
        return new INPUT
        {
            Type = User32.INPUT_KEYBOARD,
            U = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = User32.KEYEVENTF_UNICODE | (keyUp ? User32.KEYEVENTF_KEYUP : 0),
                }
            }
        };
    }

    private static INPUT MakeVkKey(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            Type = User32.INPUT_KEYBOARD,
            U = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? User32.KEYEVENTF_KEYUP : 0,
                }
            }
        };
    }
}
