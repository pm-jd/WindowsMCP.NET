using WindowsMcpNet.Models;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>
/// The rule table of the Expect tool (design spec §1.2): answers conditions against one
/// <see cref="Observation"/>. Pure. An answer is <see cref="ExpectResult.Pass"/> or
/// <see cref="ExpectResult.Fail"/> only when the observation proves it; where it cannot — the
/// collection ran out of time, the element list is cut, a window did not answer, the control does not
/// report the state, the selector fits several elements or is not proven unique — the answer is
/// <see cref="ExpectResult.Unknown"/> with the reason. Nothing is guessed: a wrong "pass" in a test
/// run is worse than no answer.
/// </summary>
public static class ExpectationEvaluator
{
    /// <summary>Most element ids listed for one condition.</summary>
    public const int MaxElementIds = 5;

    private const string OutOfTime = "the observation ran out of time — what is listed may be hidden and windows may be missing";

    /// <param name="resolveId">Looks an element id up in the server's store (null: unknown id). With it,
    /// an id condition finds its element by locator and window — id strings of one element can differ
    /// between observations. Without it (tests, offline evaluation) ids are compared as strings.</param>
    public static IReadOnlyList<ConditionOutcome> Evaluate(
        Observation observation, IReadOnlyList<Expectation> expectations, Func<string, StoredElement?>? resolveId = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(expectations);

        var outcomes = new List<ConditionOutcome>(expectations.Count);
        foreach (var expectation in expectations)
        {
            // Not an observation of what was asked about (nothing in scope, another application in
            // front): no condition can be answered from it.
            var (result, actual, ids) = observation.Unobserved is { } why
                ? (ExpectResult.Unknown, why, [])
                : expectation switch
                {
                    WindowExpectation w => EvaluateWindow(observation, w),
                    ElementExpectation e => EvaluateElement(observation, e, resolveId),
                    TextExpectation t => EvaluateText(observation, t),
                    _ => throw new ArgumentException($"condition {expectation.Index}: unsupported condition type {expectation.GetType().Name}"),
                };
            outcomes.Add(new ConditionOutcome(expectation.Index, result, Describe(expectation), actual, ids));
        }

        return outcomes;
    }

    /// <summary>All pass → pass; one fail → fail (whatever else is unknown); otherwise unknown.</summary>
    public static ExpectResult Overall(IReadOnlyList<ConditionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        if (outcomes.Any(o => o.Result == ExpectResult.Fail))
            return ExpectResult.Fail;
        return outcomes.All(o => o.Result == ExpectResult.Pass) ? ExpectResult.Pass : ExpectResult.Unknown;
    }

    /// <summary>The condition as one line, e.g. <c>window 'Login' open, modal</c>,
    /// <c>Button 'OK' enabled</c>, <c>element ejsbw value 'x'</c>, <c>text 'finished'</c>.</summary>
    public static string Describe(Expectation expectation) => expectation switch
    {
        WindowExpectation w =>
            $"window {Quoted(w.Title, w.Match)} {(w.State == ExpectWindowState.Open ? "open" : "closed")}"
            + (w.Modal switch { true => ", modal", false => ", not modal", null => "" }),
        ElementExpectation e => DescribeElement(e),
        TextExpectation t => $"text '{Show(t.Text)}'",
        _ => throw new ArgumentException($"unsupported condition type {expectation?.GetType().Name}"),
    };

    private static string DescribeElement(ElementExpectation e)
    {
        string subject;
        if (e.Id is not null)
        {
            subject = $"element {Show(e.Id)}";
        }
        else
        {
            subject = e.Type ?? "element";
            if (e.Name is not null) subject += $" {Quoted(e.Name, e.Match)}";
            if (e.Panel is not null) subject += $" @{Show(e.Panel)}";
            if (e.InWindow is not null) subject += $" in '{Show(e.InWindow)}'";
        }

        var checks = new List<string>(2);
        var hasValue = (e.Value ?? e.ValueContains) is not null;
        if (e.State != ExpectState.Exists || !hasValue)
            checks.Add(ExpectationParser.SnakeName(e.State).Replace('_', ' '));
        if (e.Value is not null)
            checks.Add($"value '{Show(e.Value, ObservationFormatter.MaxValueChars)}'");
        if (e.ValueContains is not null)
            checks.Add($"value contains '{Show(e.ValueContains, ObservationFormatter.MaxValueChars)}'");

        return $"{subject} {string.Join(", ", checks)}";
    }

    private static (ExpectResult, string, IReadOnlyList<string>) EvaluateWindow(Observation o, WindowExpectation w)
    {
        var matches = o.Windows.Where(window => Matches(window.Title, w.Title, w.Match)).ToList();

        if (matches.Count == 0)
        {
            // Windows reached after the time budget are missing from the list: "not listed" is no proof.
            if (o.BudgetExceeded)
                return (ExpectResult.Unknown, OutOfTime, []);
            return (w.State == ExpectWindowState.Closed ? ExpectResult.Pass : ExpectResult.Fail, "not open", []);
        }

        if (w.State == ExpectWindowState.Closed)
            return (ExpectResult.Fail, "open", []);
        if (w.Modal is not { } modal)
            return (ExpectResult.Pass, matches[0].Modal ? "open, modal" : "open", []);

        var agreeing = matches.Count(window => window.Modal == modal);
        if (agreeing == matches.Count)
            return (ExpectResult.Pass, modal ? "open, modal" : "open, not modal", []);
        if (agreeing == 0)
            return (ExpectResult.Fail, modal ? "open, but not modal" : "open, but modal", []);
        return (ExpectResult.Unknown, $"{matches.Count} windows have that title, {agreeing} of them {(modal ? "modal" : "not modal")}", []);
    }

    private static (ExpectResult, string, IReadOnlyList<string>) EvaluateElement(
        Observation o, ElementExpectation e, Func<string, StoredElement?>? resolveId)
    {
        // Nodes left without a hit-test stay listed although they may sit in a hidden docking panel:
        // neither a match nor its state is proven then.
        if (o.BudgetExceeded)
            return (ExpectResult.Unknown, OutOfTime, []);

        List<ObservedElement> matches;
        StoredElement? stored = null;
        if (e.Id is null)
        {
            matches = o.Elements.Where(element => MatchesSelector(o, element, e)).ToList();
        }
        else if (resolveId is null)
        {
            matches = o.Elements.Where(element => element.Id == e.Id).ToList();
        }
        else
        {
            stored = resolveId(e.Id);
            if (stored is null)
                return (ExpectResult.Unknown, $"unknown element id '{Show(e.Id)}' — it is not from a recent Observe or Expect of this server", []);
            matches = MatchStored(o, stored);
        }

        IReadOnlyList<string> ids = matches.Take(MaxElementIds).Select(element => element.Id).ToList();

        if (matches.Count == 0)
        {
            // "Not there" is only proven by a complete list of windows that all answered …
            if (WhyAbsenceIsUnproven(o) is { } reason)
                return (ExpectResult.Unknown, $"{reason} — absence cannot be proven", ids);
            // … and, for an id, only when the application it belongs to was observed at all.
            if (stored is not null && !o.Windows.Any(w => w.Process == stored.Locator.Process))
                return (ExpectResult.Unknown, $"the element's application ({stored.Locator.Process}) is not in the observed scope", ids);

            var none = e.Id is not null ? $"no element with id '{Show(e.Id)}'" : "no such element";
            return (e.State == ExpectState.Absent ? ExpectResult.Pass : ExpectResult.Fail, none, ids);
        }

        if (e.State == ExpectState.Absent)
            return (ExpectResult.Fail, matches.Count == 1 ? "found" : $"found {matches.Count}", ids);

        var hasValue = (e.Value ?? e.ValueContains) is not null;
        if (e.State == ExpectState.Exists && !hasValue)
            return (ExpectResult.Pass, matches.Count == 1 ? "found" : $"found {matches.Count}", ids);

        // Every other check is about one element: with several candidates either answer could be wrong …
        if (matches.Count > 1)
            return (ExpectResult.Unknown, $"{matches.Count} elements match — narrow the selector (panel, in_window) or use an element id", ids);
        // … and a selector's one listed match is only "the" element when nothing was left out.
        if (e.Id is null && WhyAbsenceIsUnproven(o) is { } incomplete)
            return (ExpectResult.Unknown, $"{incomplete} — a second match cannot be ruled out; use an element id", ids);

        var element0 = matches[0];
        var parts = new List<(ExpectResult Result, string Actual)>(2);
        if (e.State != ExpectState.Exists)
            parts.Add(CheckState(element0, e.State));
        if (hasValue)
            parts.Add(CheckValue(element0, e));

        var result = parts.Any(p => p.Result == ExpectResult.Fail) ? ExpectResult.Fail
            : parts.All(p => p.Result == ExpectResult.Pass) ? ExpectResult.Pass
            : ExpectResult.Unknown;
        return (result, string.Join(", ", parts.Select(p => p.Actual)), ids);
    }

    /// <summary>The window affinity of ids (see <c>ObservationService.FindLive</c>): the element with
    /// that locator in the window it was observed in; an element of a main window is also followed
    /// into another window with the same locator (same process name and class), a transient one is not.</summary>
    private static List<ObservedElement> MatchStored(Observation o, StoredElement stored)
    {
        var sameLocator = o.Elements.Where(element => element.Locator.Equals(stored.Locator)).ToList();
        var inItsWindow = sameLocator.Where(element => element.WindowHandle == stored.WindowHandle).ToList();
        if (inItsWindow.Count > 0 || stored.Transient)
            return inItsWindow;
        return sameLocator;
    }

    private static (ExpectResult, string) CheckState(ObservedElement element, ExpectState state)
    {
        switch (state)
        {
            case ExpectState.Enabled:
            case ExpectState.Disabled:
                return (Verdict(element.Enabled == (state == ExpectState.Enabled)), element.Enabled ? "enabled" : "disabled");

            case ExpectState.Selected:
            case ExpectState.NotSelected:
                if (element.Selected)
                    return (Verdict(state == ExpectState.Selected), "selected");
                // Docking tabs report IsSelected=false whatever is shown (measured on MCS), so "false"
                // proves nothing for a tab item …
                if (element.Type == "TabItem")
                    return (ExpectResult.Unknown, "a TabItem does not report its selection reliably");
                // … and for a control without the selection pattern "false" is merely the default.
                if (!element.SelectionReported)
                    return (ExpectResult.Unknown, $"a {element.Type} reports no selection state (for a check box use checked/unchecked)");
                return (Verdict(state == ExpectState.NotSelected), "not selected");

            case ExpectState.Checked:
            case ExpectState.Unchecked:
                return element.Toggle switch
                {
                    "On" => (Verdict(state == ExpectState.Checked), "checked"),
                    "Off" => (Verdict(state == ExpectState.Unchecked), "unchecked"),
                    null => (ExpectResult.Unknown, "reports no toggle state"),
                    var other => (ExpectResult.Unknown, $"toggle state is {other.ToLowerInvariant()}"),
                };

            case ExpectState.Expanded:
            case ExpectState.Collapsed:
                return element.Expand switch
                {
                    "Expanded" => (Verdict(state == ExpectState.Expanded), "expanded"),
                    "Collapsed" => (Verdict(state == ExpectState.Collapsed), "collapsed"),
                    null => (ExpectResult.Unknown, "reports no expand state"),
                    var other => (ExpectResult.Unknown, $"expand state is {other}"),
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "not a state of a single element");
        }
    }

    private static (ExpectResult, string) CheckValue(ObservedElement element, ElementExpectation e)
    {
        if (element.Password)
            return (ExpectResult.Unknown, "a password field's value is never collected");
        // No value collected: an empty field when the control answered the Value property, otherwise
        // a control whose content cannot be read that way (range-only slider, text-pattern document).
        if (element.Value is null && !element.ValueReported)
            return (ExpectResult.Unknown, $"this {element.Type} reports no value");

        var value = element.Value ?? "";
        var holds = e.Value is not null
            ? string.Equals(value, e.Value, StringComparison.Ordinal)
            : value.Contains(e.ValueContains!, StringComparison.Ordinal);
        return (Verdict(holds), $"value is '{Show(value, ObservationFormatter.MaxValueChars)}'");
    }

    private static (ExpectResult, string, IReadOnlyList<string>) EvaluateText(Observation o, TextExpectation t)
    {
        if (o.BudgetExceeded)
            return (ExpectResult.Unknown, OutOfTime, []);

        var found = o.Texts.FirstOrDefault(text => text.Contains(t.Text, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
            return (ExpectResult.Pass, $"found '{Show(found)}'", []);

        if (o.Truncated)
            return (ExpectResult.Unknown, "the observation is truncated — absence cannot be proven", []);
        if (o.Windows.Any(w => w.Unreadable))
            return (ExpectResult.Unknown, "a window in scope is unreadable — the text may be inside it", []);
        return (ExpectResult.Fail, "no such text", []);
    }

    /// <summary>Why the element list may be missing something, or null when it is complete.</summary>
    private static string? WhyAbsenceIsUnproven(Observation o)
    {
        if (o.Omitted > 0)
            return $"the element list is cut ({o.Omitted} more)";
        if (o.Truncated)
            return "the observation is truncated";
        if (o.Windows.Any(w => w.Unreadable))
            return "a window in scope is unreadable";
        return null;
    }

    private static bool MatchesSelector(Observation o, ObservedElement element, ElementExpectation e)
    {
        if (e.Type is not null && !string.Equals(element.Type, e.Type, StringComparison.OrdinalIgnoreCase))
            return false;
        if (e.Name is not null && !Matches(element.Name, e.Name, e.Match)
            && !(element.Label is not null && Matches(element.Label, e.Name, e.Match)))
            return false;
        if (e.Panel is not null && !(element.Panel is not null && Matches(element.Panel, e.Panel, e.Match)))
            return false;
        if (e.InWindow is not null)
        {
            // An element without a window title belongs to the main (bottom-most) window.
            var title = element.Window ?? (o.Windows.Count > 0 ? o.Windows[^1].Title : null);
            if (title is null || !Matches(title, e.InWindow, e.Match))
                return false;
        }

        return true;
    }

    private static bool Matches(string actual, string expected, ExpectMatch match) => match == ExpectMatch.Contains
        ? actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
        : string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static ExpectResult Verdict(bool holds) => holds ? ExpectResult.Pass : ExpectResult.Fail;

    private static string Quoted(string text, ExpectMatch match) =>
        $"{(match == ExpectMatch.Contains ? "containing " : "")}'{Show(text)}'";

    /// <summary>UI or caller text for a result line: limited like in Observe and free of line breaks.</summary>
    private static string Show(string text, int maxChars = ObservationFormatter.MaxNameChars) =>
        ObservationFormatter.EscapeControl(ObservationFormatter.Clip(text, maxChars));
}
