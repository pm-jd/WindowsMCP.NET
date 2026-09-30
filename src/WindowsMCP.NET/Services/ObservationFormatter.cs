using System.Drawing;
using System.Text;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

/// <summary>
/// Renders an <see cref="Observation"/> for tool output: a compact markdown view (the tool's
/// default) and a JSON envelope (wrapped by <c>ToolHelpers.JsonResult</c> at the call site). Pure —
/// no UIA dependency — so it is unit-testable with synthetic observations (see spec §1, task brief).
/// </summary>
public static class ObservationFormatter
{
    /// <summary>
    /// One block per window (topmost first): a heading, a `focus:` line after the first heading when
    /// set, then its elements. An element belongs to the window whose <see cref="ObservedWindow.Title"/>
    /// equals its <see cref="ObservedElement.Window"/>; a null <c>Window</c> belongs to the last window
    /// (the bottom-most main window). A window with no elements still gets its heading. Blocks are
    /// followed by a `texts:` line (when non-empty) and a footer, each section separated by a blank line.
    /// </summary>
    public static string ToMarkdown(Observation o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var elementsByWindow = GroupElementsByWindow(o);

        var sections = new List<string>(o.Windows.Count + 1);
        for (var i = 0; i < o.Windows.Count; i++)
            sections.Add(BuildWindowBlock(o, i, elementsByWindow.GetValueOrDefault(i, [])));

        sections.Add(BuildFooterSection(o));

        return string.Join("\n\n", sections);
    }

    /// <summary>
    /// Anonymous-shaped envelope: optional element fields (label, panel, window, value, toggle,
    /// selected, expanded, enabled) are omitted rather than emitted as null/false-default. Built with
    /// dictionaries because <c>ToolHelpers.JsonOptions</c> doesn't set <c>DefaultIgnoreCondition</c>.
    /// </summary>
    public static object ToJsonEnvelope(Observation o)
    {
        ArgumentNullException.ThrowIfNull(o);

        return new Dictionary<string, object?>
        {
            ["windows"] = o.Windows.Select(ToWindowJson).ToList(),
            ["focus"] = o.FocusId,
            ["elements"] = o.Elements.Select(ToElementJson).ToList(),
            ["texts"] = o.Texts,
            ["signature"] = o.Signature,
            ["truncated"] = o.Truncated,
            ["timings"] = new Dictionary<string, object?>
            {
                ["walk_ms"] = o.Timings.WalkMs,
                ["hit_ms"] = o.Timings.HitMs,
                ["total_ms"] = o.Timings.TotalMs,
            },
        };
    }

    private static string BuildWindowBlock(Observation o, int windowIndex, List<ObservedElement> elements)
    {
        var window = o.Windows[windowIndex];
        var lines = new List<string> { BuildHeading(window) };

        if (windowIndex == 0 && o.FocusId is not null)
            lines.Add($"focus: {o.FocusId}");

        if (elements.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(elements.Select(BuildElementLine));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Assigns each element to exactly one window block: the FIRST window whose <see cref="ObservedWindow.Title"/>
    /// equals the element's <see cref="ObservedElement.Window"/>, or the last window when <c>Window</c>
    /// is null. Grouping this way up front (rather than matching independently per window) guarantees
    /// each element renders once even when several windows share the same (e.g. empty) title.
    /// </summary>
    private static Dictionary<int, List<ObservedElement>> GroupElementsByWindow(Observation o)
    {
        var groups = new Dictionary<int, List<ObservedElement>>();
        var lastWindowIndex = o.Windows.Count - 1;

        foreach (var e in o.Elements)
        {
            var windowIndex = e.Window is null ? lastWindowIndex : FindFirstWindowIndex(o.Windows, e.Window);
            if (windowIndex < 0)
                continue;

            if (!groups.TryGetValue(windowIndex, out var list))
            {
                list = [];
                groups[windowIndex] = list;
            }

            list.Add(e);
        }

        return groups;
    }

    private static int FindFirstWindowIndex(IReadOnlyList<ObservedWindow> windows, string title)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i].Title == title)
                return i;
        }

        return -1;
    }

    private static string BuildHeading(ObservedWindow w)
    {
        var sb = new StringBuilder()
            .Append("## ").Append(w.Title).Append("  (").Append(w.Process).Append(", pid ").Append(w.Pid).Append(')');
        if (w.Foreground) sb.Append(" · foreground");
        if (w.Modal) sb.Append(" · modal");
        return sb.ToString();
    }

    private static string BuildElementLine(ObservedElement e)
    {
        // Pad to at least 7 (the common case: "e" + 4 hex chars + 2-space separator) but never less
        // than Id.Length + 2, so a 6-hex-char (collision-extended) id still gets a separator before
        // the type instead of fusing with it.
        var idWidth = Math.Max(7, e.Id.Length + 2);
        var sb = new StringBuilder()
            .Append(e.Id.PadRight(idWidth)).Append(e.Type).Append(" '").Append(e.Name).Append('\'');

        // Symmetric with ToElementJson: presence is null-checked, not IsNullOrEmpty, so an empty
        // (but non-null) string still renders its "key=" prefix in both renderers.
        if (e.Label is not null) sb.Append("  label='").Append(e.Label).Append('\'');
        if (e.Panel is not null) sb.Append("  @").Append(e.Panel);
        if (e.Value is not null) sb.Append("  value=").Append(e.Value);
        if (e.Toggle is not null) sb.Append("  toggle=").Append(e.Toggle);
        if (e.Selected) sb.Append("  selected");
        if (e.Expand == "Expanded") sb.Append("  expanded");
        if (!e.Enabled) sb.Append("  disabled");

        return sb.ToString();
    }

    private static string BuildFooterSection(Observation o)
    {
        var lines = new List<string>();
        if (o.Texts.Count > 0)
            lines.Add($"texts: {string.Join(" · ", o.Texts)}");

        var footer = $"signature {o.Signature} · {o.Elements.Count} elements · {o.Timings.TotalMs} ms";
        if (o.Truncated) footer += " · truncated";
        lines.Add(footer);

        return string.Join("\n", lines);
    }

    private static Dictionary<string, object?> ToWindowJson(ObservedWindow w) => new()
    {
        ["title"] = w.Title,
        ["process"] = w.Process,
        ["pid"] = w.Pid,
        ["foreground"] = w.Foreground,
        ["modal"] = w.Modal,
        ["rect"] = ToRectArray(w.Rect),
    };

    private static Dictionary<string, object?> ToElementJson(ObservedElement e)
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["type"] = e.Type,
            ["name"] = e.Name,
        };

        if (e.Label is not null) dict["label"] = e.Label;
        if (e.Panel is not null) dict["panel"] = e.Panel;
        if (e.Window is not null) dict["window"] = e.Window;
        if (e.Value is not null) dict["value"] = e.Value;
        if (e.Toggle is not null) dict["toggle"] = e.Toggle;
        if (e.Selected) dict["selected"] = true;
        if (e.Expand == "Expanded") dict["expanded"] = true;
        if (!e.Enabled) dict["enabled"] = false;
        dict["rect"] = ToRectArray(e.Rect);

        return dict;
    }

    private static int[] ToRectArray(Rectangle r) => [r.X, r.Y, r.Width, r.Height];
}
