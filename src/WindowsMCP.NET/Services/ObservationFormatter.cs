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

        var sections = new List<string>(o.Windows.Count + 1);
        for (var i = 0; i < o.Windows.Count; i++)
            sections.Add(BuildWindowBlock(o, i));

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

    private static string BuildWindowBlock(Observation o, int windowIndex)
    {
        var window = o.Windows[windowIndex];
        var lines = new List<string> { BuildHeading(window) };

        if (windowIndex == 0 && o.FocusId is not null)
            lines.Add($"focus: {o.FocusId}");

        var elements = ElementsForWindow(o, windowIndex);
        if (elements.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(elements.Select(BuildElementLine));
        }

        return string.Join("\n", lines);
    }

    private static List<ObservedElement> ElementsForWindow(Observation o, int windowIndex)
    {
        var isLastWindow = windowIndex == o.Windows.Count - 1;
        var title = o.Windows[windowIndex].Title;
        return o.Elements.Where(e => e.Window is null ? isLastWindow : e.Window == title).ToList();
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
        var sb = new StringBuilder()
            .Append(e.Id.PadRight(7)).Append(e.Type).Append(" '").Append(e.Name).Append('\'');

        if (!string.IsNullOrEmpty(e.Label)) sb.Append("  label='").Append(e.Label).Append('\'');
        if (!string.IsNullOrEmpty(e.Panel)) sb.Append("  @").Append(e.Panel);
        if (!string.IsNullOrEmpty(e.Value)) sb.Append("  value=").Append(e.Value);
        if (!string.IsNullOrEmpty(e.Toggle)) sb.Append("  toggle=").Append(e.Toggle);
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
