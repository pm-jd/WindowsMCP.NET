using System.ComponentModel;
using System.Text.Json;
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
        [Description("Array of element label IDs from last Snapshot, e.g. [3, 7, 12]")] JsonElement? labels = null,
        [Description("Array of [x, y] coordinates, e.g. [[100,200],[300,400]]")] JsonElement? locs = null,
        [Description("Hold Ctrl key while clicking (for multi-selection)")] bool press_ctrl = true)
    {
        var targets = ResolveTargets(uiTreeService, labels, locs);
        if (targets.Count == 0)
            throw new ArgumentException("No targets specified. Provide 'labels' or 'locs'.");

        if (press_ctrl)
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
            if (press_ctrl)
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
        [Description("Array of [x, y, text] triplets specifying coordinate and text, e.g. [[100,200,'hello'],[300,400,'world']]")] JsonElement? locs = null,
        [Description("Array of [label, text] pairs, e.g. [['5','John'],['6','Doe']]")] JsonElement? labels = null)
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
        JsonElement? labels,
        JsonElement? locs)
    {
        var targets = new List<(int, int, string)>();

        // labels: array of integer label IDs, e.g. [3, 7, 12]
        if (labels.HasValue && labels.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in labels.Value.EnumerateArray())
            {
                var labelStr = item.ValueKind == JsonValueKind.Number
                    ? item.GetInt32().ToString()
                    : item.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(labelStr)) continue;
                var pos = uiTreeService.ResolveLabel(labelStr)
                          ?? throw new InvalidOperationException($"Label '{labelStr}' not found in UI tree.");
                targets.Add((pos.X, pos.Y, $"[{labelStr}]"));
            }
        }

        // locs: array of [x, y] arrays, e.g. [[100,200],[300,400]]
        if (locs.HasValue && locs.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in locs.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2) continue;
                int x = item[0].GetInt32();
                int y = item[1].GetInt32();
                targets.Add((x, y, $"({x},{y})"));
            }
        }

        return targets;
    }

    private static List<(int X, int Y, string Text, string Desc)> BuildEditPairs(
        UiTreeService uiTreeService,
        JsonElement? locs,
        JsonElement? labels)
    {
        var pairs = new List<(int, int, string, string)>();

        // locs: array of [x, y, text] triplets, e.g. [[100,200,"hello"],[300,400,"world"]]
        if (locs.HasValue && locs.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in locs.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 3) continue;
                int x, y;
                // x and y may be numbers or numeric strings
                if (entry[0].ValueKind == JsonValueKind.Number)
                    x = entry[0].GetInt32();
                else if (!int.TryParse(entry[0].GetString(), out x)) continue;
                if (entry[1].ValueKind == JsonValueKind.Number)
                    y = entry[1].GetInt32();
                else if (!int.TryParse(entry[1].GetString(), out y)) continue;
                var text = entry[2].GetString() ?? string.Empty;
                pairs.Add((x, y, text, $"({x},{y})"));
            }
        }

        // labels: array of [label, text] pairs, e.g. [["5","John"],["6","Doe"]]
        if (labels.HasValue && labels.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in labels.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2) continue;
                var labelStr = entry[0].GetString() ?? string.Empty;
                var text = entry[1].GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(labelStr)) continue;
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
