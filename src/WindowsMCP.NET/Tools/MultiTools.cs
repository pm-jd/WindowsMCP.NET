using System.ComponentModel;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class MultiTools
{
    [McpServerTool(Name = "MultiSelect", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click multiple UI elements while holding Ctrl, useful for multi-selection in lists/trees. " +
                 "Provide comma-separated labels or coordinate pairs like '100,200;300,400'.")]
    public static string MultiSelect(
        UiTreeService uiTreeService,
        [Description("Comma-separated element labels from last Snapshot, e.g. '3,7,12'")] string? labels = null,
        [Description("Semicolon-separated x,y coordinates, e.g. '100,200;300,400'")] string? coordinates = null)
    {
        var targets = ResolveTargets(uiTreeService, labels, coordinates);
        if (targets.Count == 0)
            throw new ArgumentException("No targets specified. Provide 'labels' or 'coordinates'.");

        // Press Ctrl down
        var ctrlDown = MakeVkKey(0x11, keyUp: false);
        User32.SendInput(1, new[] { ctrlDown }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());

        var clicked = new List<string>();
        try
        {
            foreach (var (x, y, desc) in targets)
            {
                User32.SetCursorPos(x, y);
                SendClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
                clicked.Add(desc);
            }
        }
        finally
        {
            // Always release Ctrl
            var ctrlUp = MakeVkKey(0x11, keyUp: true);
            User32.SendInput(1, new[] { ctrlUp }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        return $"Multi-selected {clicked.Count} element(s): {string.Join(", ", clicked)}";
    }

    [McpServerTool(Name = "MultiEdit", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click and type into multiple fields sequentially. " +
                 "Provide label:text pairs separated by semicolons, e.g. '5:John;6:Doe;7:john@example.com'.")]
    public static string MultiEdit(
        UiTreeService uiTreeService,
        [Description("Semicolon-separated label:text pairs, e.g. '5:hello;8:world'")] string fields,
        [Description("Select all (Ctrl+A) before typing into each field")] bool selectAll = true)
    {
        var pairs = ParseLabelTextPairs(fields);
        if (pairs.Count == 0)
            throw new ArgumentException("No label:text pairs found in 'fields'.");

        var results = new List<string>();
        foreach (var (labelOrCoord, text) in pairs)
        {
            // Resolve position
            int cx, cy;
            if (TryParseCoord(labelOrCoord, out cx, out cy))
            {
                // direct coordinate
            }
            else
            {
                var pos = uiTreeService.ResolveLabel(labelOrCoord)
                          ?? throw new InvalidOperationException($"Label '{labelOrCoord}' not found in UI tree.");
                (cx, cy) = (pos.X, pos.Y);
            }

            // Click the field
            User32.SetCursorPos(cx, cy);
            SendClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);

            // Optionally select all
            if (selectAll)
            {
                var inputs = new INPUT[]
                {
                    MakeVkKey(0x11, keyUp: false),  // Ctrl down
                    MakeVkKey(0x41, keyUp: false),  // A down
                    MakeVkKey(0x41, keyUp: true),   // A up
                    MakeVkKey(0x11, keyUp: true),   // Ctrl up
                };
                User32.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }

            // Type the text
            var typeInputs = new INPUT[text.Length * 2];
            int idx = 0;
            foreach (char ch in text)
            {
                typeInputs[idx++] = MakeUnicodeKey(ch, keyUp: false);
                typeInputs[idx++] = MakeUnicodeKey(ch, keyUp: true);
            }
            User32.SendInput((uint)typeInputs.Length, typeInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());

            results.Add($"{labelOrCoord}=\"{text}\"");
        }

        return $"Edited {results.Count} field(s): {string.Join(", ", results)}";
    }

    // --- Helpers ---

    private static List<(int X, int Y, string Desc)> ResolveTargets(
        UiTreeService uiTreeService,
        string? labels,
        string? coordinates)
    {
        var targets = new List<(int, int, string)>();

        if (labels is not null)
        {
            foreach (var label in labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var pos = uiTreeService.ResolveLabel(label)
                          ?? throw new InvalidOperationException($"Label '{label}' not found in UI tree.");
                targets.Add((pos.X, pos.Y, $"[{label}]"));
            }
        }

        if (coordinates is not null)
        {
            foreach (var pair in coordinates.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseCoord(pair, out int x, out int y))
                    targets.Add((x, y, $"({x},{y})"));
            }
        }

        return targets;
    }

    private static List<(string Label, string Text)> ParseLabelTextPairs(string input)
    {
        var pairs = new List<(string, string)>();
        foreach (var part in input.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = part.IndexOf(':');
            if (colonIdx < 1) continue;
            var label = part[..colonIdx].Trim();
            var text  = part[(colonIdx + 1)..];
            pairs.Add((label, text));
        }
        return pairs;
    }

    private static bool TryParseCoord(string s, out int x, out int y)
    {
        x = y = 0;
        var parts = s.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y))
            return true;
        return false;
    }

    private static void SendClick(uint downFlag, uint upFlag)
    {
        var inputs = new INPUT[]
        {
            new INPUT { Type = User32.INPUT_MOUSE, U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = downFlag } } },
            new INPUT { Type = User32.INPUT_MOUSE, U = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = upFlag } } },
        };
        User32.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
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
}
