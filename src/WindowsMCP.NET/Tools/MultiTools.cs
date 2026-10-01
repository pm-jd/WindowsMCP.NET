using System.ComponentModel;
using System.Drawing;
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
                 "Provide element ids from Observe via 'elements' (takes precedence), element label IDs as an integer array, or coordinate pairs as an array of [x,y] arrays.")]
    public static string MultiSelect(
        UiTreeService uiTreeService,
        ObservationService observationService,
        ObservationStore observationStore,
        [Description("Element label IDs from last Snapshot, e.g. [3, 7, 12]")] int[]? labels = null,
        [Description("Array of [x, y] coordinates, e.g. [[100,200],[300,400]]")] int[][]? locs = null,
        [Description("Hold Ctrl key while clicking (for multi-selection)")] bool press_ctrl = true,
        [Description("Element ids from the last Observe, e.g. ['e7q2k','e3x9a']; takes precedence over labels and locs; each is resolved against the live UI and clicked at the centre of its current rectangle")] string[]? elements = null,
        CancellationToken ct = default)
    {
        try
        {
            if (elements is { Length: > 0 })
            {
                var resolved = elements
                    .Select(id => ElementTargets.Resolve(id, observationStore, observationService))
                    .ToList();
                return SelectElements(resolved, press_ctrl,
                    down => InputFactory.Send(InputFactory.Key(VK_CONTROL, keyUp: !down)),
                    point => InputFactory.LeftClickAt(point.X, point.Y));
            }

            var targets = ResolveTargets(uiTreeService, labels, locs);
            if (targets.Count == 0)
                throw new ArgumentException("No targets specified. Provide 'labels', 'locs' or 'elements'.");

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
                 "or label-text pairs via labels ([[label,text],[label,text],...]). " +
                 "With element ids from Observe, use 'elements' ([[id,text],...]); it takes precedence and sets each value directly where possible.")]
    public static string MultiEdit(
        UiTreeService uiTreeService,
        ObservationService observationService,
        ObservationStore observationStore,
        ActionExecutor executor,
        [Description("Array of [x, y, text] triplets specifying coordinate and text, e.g. [[100,200,'hello'],[300,400,'world']]")] JsonElement? locs = null,
        [Description("Array of [label, text] pairs, e.g. [['5','John'],['6','Doe']]")] JsonElement? labels = null,
        [Description("Array of [elementId, text] pairs from the last Observe, e.g. [['e7q2k','John'],['e3x9a','Doe']]; takes precedence over locs and labels; each field is set via its value where possible and read back")] JsonElement? elements = null,
        CancellationToken ct = default)
    {
        try
        {
            if (elements is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
            {
                return EditElements(elements.Value,
                    id => ElementTargets.Resolve(id, observationStore, observationService),
                    executor, r => ElementTargets.SignatureFor(r, observationService, ct));
            }

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

    /// <summary>
    /// Element path of MultiSelect (after all ids were resolved). EVERY target is checked — enabled, on
    /// screen, click point not covered by another application's window — before Ctrl goes down or
    /// anything is clicked, so a target that cannot be clicked leaves the UI untouched. Each click point
    /// is taken and checked again right before its click (an earlier click may have moved or covered the
    /// element); a failure then stops the run, releases Ctrl and reports what was already clicked.
    /// </summary>
    internal static string SelectElements(
        IReadOnlyList<ResolvedElement> resolved, bool pressCtrl, Action<bool> holdCtrl, Action<Point> click)
    {
        foreach (var element in resolved)
        {
            ActionGuards.EnsureEnabled(element.Target);
            ActionGuards.ClickPoint(element.Target);
        }

        if (pressCtrl)
            holdCtrl(true);

        var clicked = new List<string>();
        try
        {
            foreach (var element in resolved)
            {
                click(ActionGuards.ClickPoint(element.Target));
                clicked.Add(element.Describe);
            }
        }
        catch (Exception ex) when (clicked.Count > 0 && ex is not OperationCanceledException)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message} (already clicked: {string.Join(", ", clicked)})";
        }
        finally
        {
            if (pressCtrl)
                holdCtrl(false);
        }

        return $"Multi-selected {clicked.Count} element(s): {string.Join(", ", clicked)}";
    }

    /// <summary>Parses <c>[[id, text], ...]</c>, resolves ALL ids before the first edit (so a stale id
    /// cannot leave earlier fields edited), then types into each. A failure after at least one edit
    /// returns an error that lists what was already edited.</summary>
    internal static string EditElements(
        JsonElement elements, Func<string, ResolvedElement> resolve, ActionExecutor executor,
        Func<ResolvedElement, Func<string>?> signatureFor)
    {
        if (elements.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("'elements' must be an array of [elementId, text] pairs.");

        var entries = new List<(string Id, string Text)>();
        foreach (var entry in elements.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2
                || entry[0].ValueKind != JsonValueKind.String || entry[1].ValueKind != JsonValueKind.String)
                throw new ArgumentException("Each 'elements' entry must be an array of exactly two strings: [elementId, text].");

            entries.Add((entry[0].GetString()!, entry[1].GetString()!));
        }

        if (entries.Count == 0)
            throw new ArgumentException("No fields specified. Provide 'locs', 'labels' or 'elements'.");

        var resolved = entries.Select(e => (Element: resolve(e.Id), e.Text)).ToList();

        var results = new List<string>();
        foreach (var (element, text) in resolved)
        {
            try
            {
                var outcome = executor.Type(element.Target, text, clear: true, pressEnter: false, signatureFor(element), 300);
                results.Add($"{element.Describe}: {outcome.Effect.ToWire()}");
            }
            catch (Exception ex) when (results.Count > 0 && ex is not OperationCanceledException)
            {
                return $"[ERROR] {ex.GetType().Name}: {ex.Message} (already edited: {string.Join(", ", results)})";
            }
        }

        return $"Edited {results.Count} field(s): {string.Join(", ", results)}";
    }

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
