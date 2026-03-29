using System.ComponentModel;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class MultiTools
{
    [McpServerTool(Name = "MultiSelect", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click multiple UI elements while optionally holding Ctrl, useful for multi-selection in lists/trees. " +
                 "Provide element label IDs as an integer array or coordinate pairs as an array of [x,y] arrays.")]
    public static string MultiSelect(
        UiTreeService uiTreeService,
        [Description("Array of element label IDs from last Snapshot, e.g. [3, 7, 12]")] int[]? labels = null,
        [Description("Array of [x, y] coordinates, e.g. [[100,200],[300,400]]")] int[][]? locs = null,
        [Description("Hold Ctrl key while clicking (for multi-selection)")] bool pressCtrl = true)
    {
        var targets = ResolveTargets(uiTreeService, labels, locs);
        if (targets.Count == 0)
            throw new ArgumentException("No targets specified. Provide 'labels' or 'locs'.");

        if (pressCtrl)
        {
            var ctrlDown = MakeVkKey(0x11, keyUp: false);
            User32.SendInput(1, new[] { ctrlDown }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

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
            if (pressCtrl)
            {
                var ctrlUp = MakeVkKey(0x11, keyUp: true);
                User32.SendInput(1, new[] { ctrlUp }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            }
        }

        return $"Multi-selected {clicked.Count} element(s): {string.Join(", ", clicked)}";
    }

    [McpServerTool(Name = "MultiEdit", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click and type into multiple fields sequentially. " +
                 "Provide fields as coordinate-text pairs via locs ([[x,y],[x,y],...] paired with texts) " +
                 "or label-text pairs via labels ([[label,text],[label,text],...]).")]
    public static string MultiEdit(
        UiTreeService uiTreeService,
        [Description("Array of [x, y, text] or [[x,y], text] triplets specifying coordinate and text, e.g. [[100,200,'hello'],[300,400,'world']] — pass as [[x,y,text],...]")] string[][]? locs = null,
        [Description("Array of [label, text] pairs, e.g. [['5','John'],['6','Doe']]")] string[][]? labels = null)
    {
        var pairs = BuildEditPairs(uiTreeService, locs, labels);
        if (pairs.Count == 0)
            throw new ArgumentException("No fields specified. Provide 'locs' or 'labels'.");

        var results = new List<string>();
        foreach (var (cx, cy, text, desc) in pairs)
        {
            // Click the field
            User32.SetCursorPos(cx, cy);
            SendClick(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);

            // Type the text
            var typeInputs = new INPUT[text.Length * 2];
            int idx = 0;
            foreach (char ch in text)
            {
                typeInputs[idx++] = MakeUnicodeKey(ch, keyUp: false);
                typeInputs[idx++] = MakeUnicodeKey(ch, keyUp: true);
            }
            User32.SendInput((uint)typeInputs.Length, typeInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());

            results.Add($"{desc}=\"{text}\"");
        }

        return $"Edited {results.Count} field(s): {string.Join(", ", results)}";
    }

    // --- Helpers ---

    private static List<(int X, int Y, string Desc)> ResolveTargets(
        UiTreeService uiTreeService,
        int[]? labels,
        int[][]? locs)
    {
        var targets = new List<(int, int, string)>();

        if (labels is not null)
        {
            foreach (var labelId in labels)
            {
                var labelStr = labelId.ToString();
                var pos = uiTreeService.ResolveLabel(labelStr)
                          ?? throw new InvalidOperationException($"Label '{labelStr}' not found in UI tree.");
                targets.Add((pos.X, pos.Y, $"[{labelStr}]"));
            }
        }

        if (locs is not null)
        {
            foreach (var loc in locs)
            {
                if (loc is not null && loc.Length >= 2)
                    targets.Add((loc[0], loc[1], $"({loc[0]},{loc[1]})"));
            }
        }

        return targets;
    }

    private static List<(int X, int Y, string Text, string Desc)> BuildEditPairs(
        UiTreeService uiTreeService,
        string[][]? locs,
        string[][]? labels)
    {
        var pairs = new List<(int, int, string, string)>();

        if (locs is not null)
        {
            foreach (var entry in locs)
            {
                // entry is [x, y, text] as strings
                if (entry is null || entry.Length < 3) continue;
                if (!int.TryParse(entry[0], out int x) || !int.TryParse(entry[1], out int y)) continue;
                var text = entry[2];
                pairs.Add((x, y, text, $"({x},{y})"));
            }
        }

        if (labels is not null)
        {
            foreach (var entry in labels)
            {
                // entry is [label, text]
                if (entry is null || entry.Length < 2) continue;
                var labelStr = entry[0];
                var text = entry[1];
                var pos = uiTreeService.ResolveLabel(labelStr)
                          ?? throw new InvalidOperationException($"Label '{labelStr}' not found in UI tree.");
                pairs.Add((pos.X, pos.Y, text, $"[{labelStr}]"));
            }
        }

        return pairs;
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
