using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class MultiTools
{
    private const ushort VK_CONTROL = 0x11;

    [McpServerTool(Name = "MultiSelect", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Click multiple UI elements while optionally holding Ctrl, useful for multi-selection in lists/trees. " +
                 "Provide element label IDs as an integer array or coordinate pairs as an array of [x,y] arrays.")]
    public static string MultiSelect(
        UiTreeService uiTreeService,
        [Description("Element label IDs from last Snapshot, e.g. [3, 7, 12]")] int[]? labels = null,
        [Description("Array of [x, y] coordinates, e.g. [[100,200],[300,400]]")] int[][]? locs = null,
        [Description("Hold Ctrl key while clicking (for multi-selection)")] bool press_ctrl = true)
    {
        try
        {
            var targets = ResolveTargets(uiTreeService, labels, locs);
            if (targets.Count == 0)
                throw new ArgumentException("No targets specified. Provide 'labels' or 'locs'.");

            if (press_ctrl)
                InputFactory.Send(InputFactory.Key(VK_CONTROL, keyUp: false));

            var clicked = new List<string>();
            try
            {
                foreach (var (x, y, desc) in targets)
                {
                    User32.SetCursorPos(x, y);
                    InputFactory.Click(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
                    clicked.Add(desc);
                }
            }
            finally
            {
                if (press_ctrl)
                    InputFactory.Send(InputFactory.Key(VK_CONTROL, keyUp: true));
            }

            return $"Multi-selected {clicked.Count} element(s): {string.Join(", ", clicked)}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
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
        try
        {
            var pairs = BuildEditPairs(uiTreeService, locs, labels);
            if (pairs.Count == 0)
                throw new ArgumentException("No fields specified. Provide 'locs' or 'labels'.");

            var results = new List<string>();
            foreach (var (cx, cy, text, desc) in pairs)
            {
                User32.SetCursorPos(cx, cy);
                InputFactory.Click(User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP);
                InputFactory.Send(InputFactory.BuildTextInputs(text));
                results.Add($"{desc}=\"{text}\"");
            }

            return $"Edited {results.Count} field(s): {string.Join(", ", results)}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    // --- Helpers ---

    private static List<(int X, int Y, string Desc)> ResolveTargets(
        UiTreeService uiTreeService,
        int[]? labels,
        int[][]? locs)
    {
        var targets = new List<(int, int, string)>();

        foreach (var label in labels ?? [])
        {
            var labelStr = label.ToString();
            var pos = uiTreeService.ResolveLabel(labelStr)
                      ?? throw new InvalidOperationException($"Label '{labelStr}' not found in UI tree.");
            targets.Add((pos.X, pos.Y, $"[{labelStr}]"));
        }

        foreach (var loc in locs ?? [])
        {
            if (ToolHelpers.ToPoint(loc) is { } p)
                targets.Add((p.X, p.Y, $"({p.X},{p.Y})"));
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
                var labelStr = entry[0].ValueKind == JsonValueKind.Number
                    ? entry[0].GetInt32().ToString()
                    : entry[0].GetString() ?? string.Empty;
                var text = entry[1].GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(labelStr)) continue;
                var pos = uiTreeService.ResolveLabel(labelStr)
                          ?? throw new InvalidOperationException($"Label '{labelStr}' not found in UI tree.");
                pairs.Add((pos.X, pos.Y, text, $"[{labelStr}]"));
            }
        }

        return pairs;
    }
}
