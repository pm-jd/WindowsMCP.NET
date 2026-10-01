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
    /// <summary>Longest value shown for an element; the rest is replaced by <c>…(+n chars)</c>. The
    /// signature is computed from the full value, so a change beyond this limit is still detected.</summary>
    public const int MaxValueChars = 200;

    /// <summary>Longest name, label, panel or context text shown, truncated the same way.</summary>
    public const int MaxNameChars = 120;

    // Unicode line breaks that are not control characters (written as numbers so that no editor or
    // tool can turn them into the characters themselves).
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// One block per window (topmost first): a heading, a `focus:` line after the first heading when
    /// set, then its elements. An element belongs to the window whose <see cref="ObservedWindow.Title"/>
    /// equals its <see cref="ObservedElement.Window"/>; a null <c>Window</c> belongs to the last window
    /// (the bottom-most main window). A window with no elements still gets its heading — unless it is
    /// mere noise (<see cref="IsNoise"/>). Blocks are followed by a `texts:` line (when non-empty), one
    /// line about windows that did not answer UI Automation (when there are any) and a footer, each
    /// section separated by a blank line.
    /// </summary>
    public static string ToMarkdown(Observation o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var elementsByWindow = GroupElementsByWindow(o);

        var sections = new List<string>(o.Windows.Count + 1);
        for (var i = 0; i < o.Windows.Count; i++)
        {
            var elements = elementsByWindow.GetValueOrDefault(i, []);
            if (elements.Count == 0 && IsNoise(o.Windows[i]))
                continue;

            sections.Add(BuildWindowBlock(o.Windows[i], sections.Count == 0 ? o.FocusId : null, elements));
        }

        sections.Add(BuildFooterSection(o));

        return string.Join("\n\n", sections);
    }

    /// <summary>A window that says nothing when it has no element to show: untitled, not the foreground
    /// window and readable — the helper windows a menu drop-down brings along. A titled, foreground or
    /// unreadable window always keeps its heading.</summary>
    private static bool IsNoise(ObservedWindow w) =>
        string.IsNullOrWhiteSpace(w.Title) && !w.Foreground && !w.Unreadable;

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
            ["texts"] = o.Texts.Select(t => Clip(t, MaxNameChars)).ToList(),
            ["signature"] = o.Signature,
            ["truncated"] = o.Truncated,
            ["omitted"] = o.Omitted,
            ["timings"] = new Dictionary<string, object?>
            {
                ["walk_ms"] = o.Timings.WalkMs,
                ["hit_ms"] = o.Timings.HitMs,
                ["total_ms"] = o.Timings.TotalMs,
            },
        };
    }

    private static string BuildWindowBlock(ObservedWindow window, string? focusId, List<ObservedElement> elements)
    {
        var lines = new List<string> { BuildHeading(window) };

        if (focusId is not null)
            lines.Add($"focus: {focusId}");

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
            .Append("## ").Append(EscapeControl(w.Title)).Append("  (").Append(w.Process).Append(", pid ").Append(w.Pid).Append(')');
        if (w.Foreground) sb.Append(" · foreground");
        if (w.Modal) sb.Append(" · modal");
        if (w.Unreadable) sb.Append(" · unreadable");
        return sb.ToString();
    }

    private static string BuildElementLine(ObservedElement e)
    {
        // Pad to at least 7 (the common case: "e" + 4 hex chars + 2-space separator) but never less
        // than Id.Length + 2, so a 6-hex-char (collision-extended) id still gets a separator before
        // the type instead of fusing with it.
        var idWidth = Math.Max(7, e.Id.Length + 2);
        var sb = new StringBuilder()
            .Append(e.Id.PadRight(idWidth)).Append(e.Type).Append(" '").Append(Line(e.Name, MaxNameChars)).Append('\'');

        // Symmetric with ToElementJson: presence is null-checked, not IsNullOrEmpty, so an empty
        // (but non-null) string still renders its "key=" prefix in both renderers.
        if (e.Label is not null) sb.Append("  label='").Append(Line(e.Label, MaxNameChars)).Append('\'');
        if (e.Panel is not null) sb.Append("  @").Append(Line(e.Panel, MaxNameChars));
        if (e.Value is not null) sb.Append("  value=").Append(Line(e.Value, MaxValueChars));
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
            lines.Add($"texts: {string.Join(" · ", o.Texts.Select(t => Line(t, MaxNameChars)))}");

        // Said in words, not only in the headings: an agent that sees no elements must know that this
        // is "did not answer", not "nothing there", and what still works.
        var unreadable = o.Windows.Count(w => w.Unreadable);
        if (unreadable > 0)
        {
            lines.Add($"{unreadable} window(s) did not answer UI Automation — an earlier action may still be running " +
                      "(for example a modal dialog opened by Invoke). Use Screenshot and keyboard/coordinates until it is closed.");
        }

        var footer = $"signature {o.Signature} · {o.Elements.Count} elements · {o.Timings.TotalMs} ms";
        if (o.Omitted > 0)
        {
            // The cut is made exactly at max_elements, so the number of elements listed IS the limit
            // that was in effect — the one the caller has to raise to see the rest.
            footer += $" · truncated: {o.Omitted} more elements (max_elements={o.Elements.Count})";
        }
        else if (o.Truncated)
        {
            footer += " · truncated";
        }

        lines.Add(footer);

        return string.Join("\n", lines);
    }

    private static Dictionary<string, object?> ToWindowJson(ObservedWindow w)
    {
        var dict = new Dictionary<string, object?>
        {
            ["title"] = w.Title,
            ["process"] = w.Process,
            ["pid"] = w.Pid,
            ["foreground"] = w.Foreground,
            ["modal"] = w.Modal,
        };

        if (w.Unreadable) dict["unreadable"] = true;
        dict["rect"] = ToRectArray(w.Rect);

        return dict;
    }

    private static Dictionary<string, object?> ToElementJson(ObservedElement e)
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["type"] = e.Type,
            ["name"] = Clip(e.Name, MaxNameChars),
        };

        if (e.Label is not null) dict["label"] = Clip(e.Label, MaxNameChars);
        if (e.Panel is not null) dict["panel"] = Clip(e.Panel, MaxNameChars);
        if (e.Window is not null) dict["window"] = e.Window;
        if (e.Value is not null) dict["value"] = Clip(e.Value, MaxValueChars);
        if (e.Toggle is not null) dict["toggle"] = e.Toggle;
        if (e.Selected) dict["selected"] = true;
        if (e.Expand == "Expanded") dict["expanded"] = true;
        if (!e.Enabled) dict["enabled"] = false;
        dict["rect"] = ToRectArray(e.Rect);

        return dict;
    }

    /// <summary>Markdown rendering of one piece of UI text: limited to <paramref name="maxChars"/> and
    /// free of line breaks and tabs, so that one element always stays one line. Also how the action
    /// results quote a field value.</summary>
    internal static string Line(string text, int maxChars) => EscapeControl(Clip(text, maxChars));

    /// <summary>At most <paramref name="maxChars"/> characters of <paramref name="text"/>; a longer text
    /// keeps its first <paramref name="maxChars"/> characters followed by <c>…(+n chars)</c> with the
    /// number of characters left out. Never cuts a surrogate pair in half.</summary>
    internal static string Clip(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        var kept = maxChars > 0 && char.IsHighSurrogate(text[maxChars - 1]) ? maxChars - 1 : maxChars;
        return $"{text.AsSpan(0, kept)}…(+{text.Length - kept} chars)";
    }

    /// <summary>Renders carriage return, line feed and tab as the two-character escapes backslash-r,
    /// backslash-n and backslash-t, and any other control or line-separator character as a
    /// backslash-u escape. Everything else — backslashes included — is left as it is.</summary>
    internal static string EscapeControl(string text)
    {
        StringBuilder? escaped = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var escape = c switch
            {
                '\r' => @"\r",
                '\n' => @"\n",
                '\t' => @"\t",
                LineSeparator or ParagraphSeparator => $@"\u{(int)c:x4}",
                _ when char.IsControl(c) => $@"\u{(int)c:x4}",
                _ => null,
            };

            if (escape is null)
            {
                escaped?.Append(c);
                continue;
            }

            escaped ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
            escaped.Append(escape);
        }

        return escaped?.ToString() ?? text;
    }

    private static int[] ToRectArray(Rectangle r) => [r.X, r.Y, r.Width, r.Height];
}
