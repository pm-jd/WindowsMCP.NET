using System.ComponentModel;
using System.Text.Json;
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
    [Description("Click at coordinates or a labeled UI element.")]
    public static string Click(
        UiTreeService uiTreeService,
        [Description("Coordinate as [x, y] (ignored when label is given)")] JsonElement? loc = null,
        [Description("UI element label from last Snapshot (e.g. '3')")] string? label = null,
        [Description("Mouse button: left, right, middle")] string button = "left",
        [Description("Number of clicks: 1 for single (default), 2 for double")] int clicks = 1)
    {
        try
        {
            int cx, cy;
            if (label is not null)
            {
                var pos = uiTreeService.ResolveLabel(label)
                          ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
                (cx, cy) = pos;
            }
            else if (ParseCoord(loc) is { } coord)
            {
                (cx, cy) = coord;
            }
            else
            {
                throw new ArgumentException("Either 'label' or 'loc' ([x, y]) must be provided.");
            }

            User32.SetCursorPos(cx, cy);

            var (downFlag, upFlag) = button.ToLowerInvariant() switch
            {
                "right"  => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
                "middle" => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
                _        => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
            };

            int actualClicks = Math.Max(1, clicks);
            for (int i = 0; i < actualClicks; i++)
            {
                SendMouseClick(downFlag, upFlag);
            }

            return $"Clicked {button} at ({cx},{cy}){(actualClicks > 1 ? $" ({actualClicks}x)" : "")}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Type", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Type text, optionally clicking a target element first.")]
    public static string Type(
        UiTreeService uiTreeService,
        [Description("Text to type")] string text,
        [Description("Optional: click this label before typing")] string? label = null,
        [Description("Coordinate to click before typing as [x, y]")] JsonElement? loc = null,
        [Description("Select all (Ctrl+A then Delete) before typing")] bool clear = false,
        [Description("Press Enter after typing")] bool press_enter = false)
    {
        try
        {
            if (label is not null)
            {
                var pos = uiTreeService.ResolveLabel(label)
                          ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
                User32.SetCursorPos(pos.X, pos.Y);
                SendMouseClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
            }
            else if (ParseCoord(loc) is { } coord)
            {
                User32.SetCursorPos(coord.X, coord.Y);
                SendMouseClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
            }

            if (clear)
            {
                // Ctrl+A then Delete
                var clearInputs = new INPUT[]
                {
                    MakeVkKey(0x11, keyUp: false),  // Ctrl down
                    MakeVkKey(0x41, keyUp: false),  // A down
                    MakeVkKey(0x41, keyUp: true),   // A up
                    MakeVkKey(0x11, keyUp: true),   // Ctrl up
                    MakeVkKey(0x2E, keyUp: false),  // Delete down
                    MakeVkKey(0x2E, keyUp: true),   // Delete up
                };
                User32.SendInput((uint)clearInputs.Length, clearInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }

            var inputs = new INPUT[text.Length * 2];
            int idx = 0;
            foreach (char ch in text)
            {
                inputs[idx++] = MakeUnicodeKey(ch, keyUp: false);
                inputs[idx++] = MakeUnicodeKey(ch, keyUp: true);
            }

            User32.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());

            if (press_enter)
            {
                var enterInputs = new INPUT[]
                {
                    MakeVkKey(0x0D, keyUp: false),  // Enter down
                    MakeVkKey(0x0D, keyUp: true),   // Enter up
                };
                User32.SendInput((uint)enterInputs.Length, enterInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }

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
        [Description("Scroll direction: up or down (vertical), left or right (horizontal)")] string direction = "down",
        [Description("Number of scroll notches")] int wheel_times = 3,
        [Description("Coordinate as [x, y]")] JsonElement? loc = null,
        [Description("UI element label")] string? label = null,
        [Description("Scroll axis: vertical (default) or horizontal")] string type = "vertical")
    {
        try
        {
            int cx = 0, cy = 0;
            if (label is not null)
            {
                var pos = uiTreeService.ResolveLabel(label)
                          ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
                (cx, cy) = (pos.X, pos.Y);
            }
            else if (ParseCoord(loc) is { } coord)
            {
                (cx, cy) = coord;
            }

            if (cx != 0 || cy != 0)
                User32.SetCursorPos(cx, cy);

            bool isHorizontal = type.Equals("horizontal", StringComparison.OrdinalIgnoreCase)
                || direction.Equals("left", StringComparison.OrdinalIgnoreCase)
                || direction.Equals("right", StringComparison.OrdinalIgnoreCase);

            int delta;
            if (isHorizontal)
            {
                delta = direction.Equals("left", StringComparison.OrdinalIgnoreCase) ? -120 : 120;
            }
            else
            {
                delta = direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? 120 : -120;
            }
            delta *= wheel_times;

            uint scrollFlag = isHorizontal ? User32.MOUSEEVENTF_HWHEEL : User32.MOUSEEVENTF_WHEEL;

            var input = new INPUT
            {
                Type = User32.INPUT_MOUSE,
                U = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = scrollFlag,
                        mouseData = (uint)delta,
                    }
                }
            };
            User32.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            return $"Scrolled {direction} {wheel_times} notch(es) at ({cx},{cy})";
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
        [Description("Coordinate as [x, y]")] JsonElement? loc = null,
        [Description("UI element label")] string? label = null,
        [Description("Drag: hold mouse button down while moving, release after")] bool drag = false)
    {
        try
        {
            int cx, cy;
            if (label is not null)
            {
                var pos = uiTreeService.ResolveLabel(label)
                          ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
                (cx, cy) = (pos.X, pos.Y);
            }
            else if (ParseCoord(loc) is { } coord)
            {
                (cx, cy) = coord;
            }
            else
            {
                throw new ArgumentException("Either 'label' or 'loc' ([x, y]) must be provided.");
            }

            if (drag)
            {
                var mouseDown = new INPUT
                {
                    Type = User32.INPUT_MOUSE,
                    U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = User32.MOUSEEVENTF_LEFTDOWN } }
                };
                User32.SendInput(1, new[] { mouseDown }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }

            User32.SetCursorPos(cx, cy);

            if (drag)
            {
                var mouseUp = new INPUT
                {
                    Type = User32.INPUT_MOUSE,
                    U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = User32.MOUSEEVENTF_LEFTUP } }
                };
                User32.SendInput(1, new[] { mouseUp }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }

            return $"{(drag ? "Dragged" : "Moved")} cursor to ({cx},{cy})";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Shortcut", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Send a keyboard shortcut (e.g. ctrl+c, alt+f4).")]
    public static string Shortcut(
        [Description("Key combination, e.g. 'ctrl+c', 'alt+tab', 'win+d'")] string shortcut)
    {
        try
        {
            var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        [Description("Duration in seconds to wait (max 10)")] int duration)
    {
        try
        {
            duration = Math.Clamp(duration, 0, 10);
            await Task.Delay(TimeSpan.FromSeconds(duration));
            return $"Waited {duration}s";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Parses a JsonElement that is expected to be a [x, y] integer array.
    /// Returns null if the element is null/undefined or not a valid 2-element array.
    /// </summary>
    private static (int X, int Y)? ParseCoord(JsonElement? loc)
    {
        if (!loc.HasValue || loc.Value.ValueKind != JsonValueKind.Array) return null;
        var arr = loc.Value;
        if (arr.GetArrayLength() < 2) return null;
        return (arr[0].GetInt32(), arr[1].GetInt32());
    }

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
