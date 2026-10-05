using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ExpectationEvaluator"/> is the rule table of the Expect tool (design spec §1.2): one
/// test per cell. The point of the table is the third column — an answer is pass or fail only when
/// the observation proves it, everything else is unknown.
/// </summary>
public class ExpectationEvaluatorTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 10, HitMs: 5, TotalMs: 15);
    private static readonly ElementLocator Locator = new("app.exe", "MainWin", []);

    private static ObservedWindow Window(string title, bool modal = false, bool unreadable = false) =>
        new(1, title, "MainWin", "app.exe", 100, false, modal, new Rectangle(0, 0, 800, 600)) { Unreadable = unreadable };

    private static ObservedElement Element(
        string id, string type, string name, string? label = null, string? panel = null, string? window = null,
        string? value = null, string? toggle = null, bool selected = false, string? expand = null, bool enabled = true,
        bool password = false) =>
        new(id, type, name, label, panel, window, value, toggle, selected, expand, enabled, new Rectangle(0, 0, 10, 10), Locator)
        {
            Password = password,
        };

    private static Observation Obs(
        IReadOnlyList<ObservedElement>? elements = null, IReadOnlyList<ObservedWindow>? windows = null,
        IReadOnlyList<string>? texts = null, bool truncated = false, int omitted = 0) =>
        new(windows ?? [Window("Main")], null, elements ?? [], texts ?? [], "sig", truncated, Timings) { Omitted = omitted };

    private static WindowExpectation Win(
        string title, ExpectWindowState state = ExpectWindowState.Open, bool? modal = null, ExpectMatch match = ExpectMatch.Exact) =>
        new(1, title, state, modal, match);

    private static ElementExpectation Sel(
        string? type = null, string? name = null, ExpectState state = ExpectState.Exists, string? panel = null,
        string? inWindow = null, string? value = null, string? valueContains = null, ExpectMatch match = ExpectMatch.Exact) =>
        new(1, null, type, name, panel, inWindow, state, value, valueContains, match);

    private static ElementExpectation ById(string id, ExpectState state = ExpectState.Exists, string? value = null) =>
        new(1, id, null, null, null, null, state, value, null, ExpectMatch.Exact);

    private static ConditionOutcome Eval(Observation observation, Expectation expectation) =>
        Assert.Single(ExpectationEvaluator.Evaluate(observation, [expectation]));

    // ── windows ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Window_Open_Found_Pass()
    {
        var outcome = Eval(Obs(windows: [Window("Login", modal: true), Window("Main")]), Win("Login"));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal("open, modal", outcome.Actual);
        Assert.Equal(1, outcome.Index);
    }

    [Fact]
    public void Window_Open_NotFound_Fail()
    {
        var outcome = Eval(Obs(), Win("Login"));

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Contains("not open", outcome.Actual);
    }

    [Theory]
    [InlineData(true, true, ExpectResult.Pass)]
    [InlineData(false, false, ExpectResult.Pass)]
    [InlineData(true, false, ExpectResult.Fail)]
    [InlineData(false, true, ExpectResult.Fail)]
    public void Window_Open_Modal(bool isModal, bool expectedModal, ExpectResult expected)
    {
        var outcome = Eval(Obs(windows: [Window("Login", modal: isModal), Window("Main")]), Win("Login", modal: expectedModal));

        Assert.Equal(expected, outcome.Result);
    }

    [Fact]
    public void Window_Open_ContainsMatch_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(windows: [Window("MCS 1.13 - Program")]), Win("mcs 1.13", match: ExpectMatch.Contains)).Result);

    [Fact]
    public void Window_Open_ExactMatch_DoesNotMatchAPart() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs(windows: [Window("MCS 1.13 - Program")]), Win("MCS 1.13")).Result);

    [Fact]
    public void Window_Open_CaseInsensitive_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(windows: [Window("Login")]), Win("LOGIN")).Result);

    [Fact]
    public void Window_Open_Unreadable_StillCounts_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(windows: [Window("Login", unreadable: true)]), Win("Login")).Result);

    [Fact]
    public void Window_Closed_NotFound_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(), Win("Login", ExpectWindowState.Closed)).Result);

    [Fact]
    public void Window_Closed_Found_Fail() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs(windows: [Window("Login"), Window("Main")]), Win("Login", ExpectWindowState.Closed)).Result);

    // ── exists / absent ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Element_Exists_One_Pass()
    {
        var outcome = Eval(Obs([Element("a1", "Button", "OK")]), Sel("Button", "OK"));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal(["a1"], outcome.ElementIds);
    }

    [Fact]
    public void Element_Exists_Several_Pass()
    {
        var outcome = Eval(Obs([Element("a1", "Button", "OK"), Element("a2", "Button", "OK")]), Sel("Button", "OK"));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal(["a1", "a2"], outcome.ElementIds);
    }

    [Fact]
    public void Element_Exists_NoneComplete_Fail() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs([Element("a1", "Button", "Cancel")]), Sel("Button", "OK")).Result);

    [Fact]
    public void Element_Exists_NoneOmitted_Unknown()
    {
        var outcome = Eval(Obs(omitted: 12, truncated: true), Sel("Button", "OK"));

        Assert.Equal(ExpectResult.Unknown, outcome.Result);
        Assert.Contains("12 more", outcome.Actual);
    }

    [Fact]
    public void Element_Exists_NoneTruncated_Unknown() =>
        Assert.Equal(ExpectResult.Unknown, Eval(Obs(truncated: true), Sel("Button", "OK")).Result);

    [Fact]
    public void Element_Exists_NoneButUnreadableWindow_Unknown()
    {
        var outcome = Eval(Obs(windows: [Window("Dialog", unreadable: true), Window("Main")]), Sel("Button", "OK"));

        Assert.Equal(ExpectResult.Unknown, outcome.Result);
        Assert.Contains("unreadable", outcome.Actual);
    }

    [Fact]
    public void Element_Absent_NoneComplete_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(), Sel("Button", "Logout", ExpectState.Absent)).Result);

    [Fact]
    public void Element_Absent_Found_Fail()
    {
        var outcome = Eval(Obs([Element("a1", "Button", "Logout")]), Sel("Button", "Logout", ExpectState.Absent));

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Equal(["a1"], outcome.ElementIds);
    }

    [Fact]
    public void Element_Absent_SeveralFound_Fail() =>
        Assert.Equal(ExpectResult.Fail,
            Eval(Obs([Element("a1", "Button", "X"), Element("a2", "Button", "X")]), Sel("Button", "X", ExpectState.Absent)).Result);

    [Fact]
    public void Element_Absent_NoneOmitted_Unknown() =>
        Assert.Equal(ExpectResult.Unknown, Eval(Obs(omitted: 3, truncated: true), Sel("Button", "Logout", ExpectState.Absent)).Result);

    [Fact]
    public void Element_Absent_NoneButUnreadableWindow_Unknown() =>
        Assert.Equal(ExpectResult.Unknown,
            Eval(Obs(windows: [Window("Dialog", unreadable: true)]), Sel("Button", "Logout", ExpectState.Absent)).Result);

    // ── enabled / disabled ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, ExpectState.Enabled, ExpectResult.Pass, "enabled")]
    [InlineData(false, ExpectState.Enabled, ExpectResult.Fail, "disabled")]
    [InlineData(false, ExpectState.Disabled, ExpectResult.Pass, "disabled")]
    [InlineData(true, ExpectState.Disabled, ExpectResult.Fail, "enabled")]
    public void Element_EnabledDisabled(bool enabled, ExpectState state, ExpectResult expected, string actual)
    {
        var outcome = Eval(Obs([Element("a1", "Button", "Start", enabled: enabled)]), Sel("Button", "Start", state));

        Assert.Equal(expected, outcome.Result);
        Assert.Equal(actual, outcome.Actual);
    }

    [Fact]
    public void Element_Enabled_NotFoundComplete_Fail()
    {
        var outcome = Eval(Obs(), Sel("Button", "Start", ExpectState.Enabled));

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Contains("no such element", outcome.Actual);
    }

    [Fact]
    public void Element_Enabled_NotFoundCut_Unknown() =>
        Assert.Equal(ExpectResult.Unknown, Eval(Obs(omitted: 1, truncated: true), Sel("Button", "Start", ExpectState.Enabled)).Result);

    [Fact]
    public void Element_Enabled_SeveralMatch_Unknown()
    {
        var outcome = Eval(
            Obs([Element("a1", "Button", "Start"), Element("a2", "Button", "Start", enabled: false)]),
            Sel("Button", "Start", ExpectState.Enabled));

        Assert.Equal(ExpectResult.Unknown, outcome.Result);
        Assert.Contains("2 elements match", outcome.Actual);
        Assert.Equal(["a1", "a2"], outcome.ElementIds);
    }

    [Fact]
    public void SeveralMatch_ListsAtMostFiveIds()
    {
        var elements = Enumerable.Range(0, 7).Select(i => Element($"a{i}", "Button", "X")).ToList();

        var outcome = Eval(Obs(elements), Sel("Button", "X", ExpectState.Enabled));

        Assert.Contains("7 elements match", outcome.Actual);
        Assert.Equal(5, outcome.ElementIds.Count);
    }

    // ── selected ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("RadioButton", true, ExpectState.Selected, ExpectResult.Pass)]
    [InlineData("RadioButton", false, ExpectState.Selected, ExpectResult.Fail)]
    [InlineData("RadioButton", false, ExpectState.NotSelected, ExpectResult.Pass)]
    [InlineData("RadioButton", true, ExpectState.NotSelected, ExpectResult.Fail)]
    [InlineData("TabItem", true, ExpectState.Selected, ExpectResult.Pass)]
    [InlineData("TabItem", true, ExpectState.NotSelected, ExpectResult.Fail)]
    [InlineData("TabItem", false, ExpectState.Selected, ExpectResult.Unknown)]
    [InlineData("TabItem", false, ExpectState.NotSelected, ExpectResult.Unknown)]
    public void Element_Selection(string type, bool selected, ExpectState state, ExpectResult expected)
    {
        var outcome = Eval(Obs([Element("a1", type, "20x", selected: selected)]), Sel(type, "20x", state));

        Assert.Equal(expected, outcome.Result);
    }

    [Fact]
    public void Element_Selected_FalseTabItem_SaysWhy() =>
        Assert.Contains("TabItem does not report its selection reliably",
            Eval(Obs([Element("a1", "TabItem", "Camera")]), Sel("TabItem", "Camera", ExpectState.Selected)).Actual);

    // ── checked / expanded ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("On", ExpectState.Checked, ExpectResult.Pass)]
    [InlineData("Off", ExpectState.Checked, ExpectResult.Fail)]
    [InlineData("Off", ExpectState.Unchecked, ExpectResult.Pass)]
    [InlineData("On", ExpectState.Unchecked, ExpectResult.Fail)]
    [InlineData("Indeterminate", ExpectState.Checked, ExpectResult.Unknown)]
    [InlineData("Indeterminate", ExpectState.Unchecked, ExpectResult.Unknown)]
    [InlineData(null, ExpectState.Checked, ExpectResult.Unknown)]
    [InlineData(null, ExpectState.Unchecked, ExpectResult.Unknown)]
    public void Element_Toggle(string? toggle, ExpectState state, ExpectResult expected) =>
        Assert.Equal(expected, Eval(Obs([Element("a1", "CheckBox", "Live", toggle: toggle)]), Sel("CheckBox", "Live", state)).Result);

    [Theory]
    [InlineData("Expanded", ExpectState.Expanded, ExpectResult.Pass)]
    [InlineData("Collapsed", ExpectState.Expanded, ExpectResult.Fail)]
    [InlineData("Collapsed", ExpectState.Collapsed, ExpectResult.Pass)]
    [InlineData("Expanded", ExpectState.Collapsed, ExpectResult.Fail)]
    [InlineData("LeafNode", ExpectState.Expanded, ExpectResult.Unknown)]
    [InlineData("PartiallyExpanded", ExpectState.Collapsed, ExpectResult.Unknown)]
    [InlineData(null, ExpectState.Expanded, ExpectResult.Unknown)]
    [InlineData(null, ExpectState.Collapsed, ExpectResult.Unknown)]
    public void Element_Expand(string? expand, ExpectState state, ExpectResult expected) =>
        Assert.Equal(expected, Eval(Obs([Element("a1", "TreeItem", "General", expand: expand)]), Sel("TreeItem", "General", state)).Result);

    // ── values ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Element_Value_Equal_Pass()
    {
        var outcome = Eval(Obs([Element("a1", "Edit", "Focus", value: "90,000")]), Sel("Edit", "Focus", value: "90,000"));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal("value is '90,000'", outcome.Actual);
    }

    [Fact]
    public void Element_Value_Differs_Fail()
    {
        var outcome = Eval(Obs([Element("a1", "Edit", "Focus", value: "50,000")]), Sel("Edit", "Focus", value: "90,000"));

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Equal("value is '50,000'", outcome.Actual);
    }

    [Fact]
    public void Element_Value_CaseDiffers_Fail() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs([Element("a1", "Edit", "User", value: "Admin")]), Sel("Edit", "User", value: "admin")).Result);

    [Fact]
    public void Element_Value_EmptyExpectedAndNoValue_Pass()
    {
        var outcome = Eval(Obs([Element("a1", "Edit", "User")]), Sel("Edit", "User", value: ""));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal("value is ''", outcome.Actual);
    }

    [Fact]
    public void Element_Value_NoValueButOneExpected_Fail() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs([Element("a1", "Edit", "User")]), Sel("Edit", "User", value: "admin")).Result);

    [Fact]
    public void Element_Value_Password_Unknown()
    {
        var outcome = Eval(Obs([Element("a1", "Edit", "Password", password: true)]), Sel("Edit", "Password", value: ""));

        Assert.Equal(ExpectResult.Unknown, outcome.Result);
        Assert.Contains("password", outcome.Actual);
    }

    [Theory]
    [InlineData("90", ExpectResult.Pass)]
    [InlineData("91", ExpectResult.Fail)]
    public void Element_ValueContains(string part, ExpectResult expected) =>
        Assert.Equal(expected, Eval(Obs([Element("a1", "Edit", "Focus", value: "90,000")]), Sel("Edit", "Focus", valueContains: part)).Result);

    [Fact]
    public void Element_Value_LongValue_IsComparedInFull_AndShownClipped()
    {
        var value = new string('x', 300) + "end";

        var outcome = Eval(Obs([Element("a1", "Edit", "Log", value: value)]), Sel("Edit", "Log", value: value));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Contains("…(+103 chars)", outcome.Actual);
    }

    [Theory]
    [InlineData(true, "90,000", ExpectResult.Pass)]
    [InlineData(true, "50,000", ExpectResult.Fail)]
    [InlineData(false, "90,000", ExpectResult.Fail)]
    public void Element_StateAndValue_BothMustHold(bool enabled, string value, ExpectResult expected)
    {
        var outcome = Eval(
            Obs([Element("a1", "Edit", "Focus", value: value, enabled: enabled)]),
            Sel("Edit", "Focus", ExpectState.Enabled, value: "90,000"));

        Assert.Equal(expected, outcome.Result);
        Assert.Contains(enabled ? "enabled" : "disabled", outcome.Actual);
        Assert.Contains($"value is '{value}'", outcome.Actual);
    }

    [Fact]
    public void Element_ValueOnSeveralMatches_Unknown() =>
        Assert.Equal(ExpectResult.Unknown,
            Eval(Obs([Element("a1", "Edit", "X", value: "1"), Element("a2", "Edit", "X", value: "1")]), Sel("Edit", "X", value: "1")).Result);

    // ── by id / selector ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Element_ById_Found_Pass()
    {
        var outcome = Eval(Obs([Element("a1", "Button", "OK"), Element("ejsbw", "Button", "Start")]), ById("ejsbw", ExpectState.Enabled));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal(["ejsbw"], outcome.ElementIds);
    }

    [Fact]
    public void Element_ById_MissingComplete_Fail()
    {
        var outcome = Eval(Obs([Element("a1", "Button", "OK")]), ById("zzzzz", ExpectState.Enabled));

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Contains("no element with id 'zzzzz'", outcome.Actual);
    }

    [Fact]
    public void Element_ById_MissingCut_Unknown() =>
        Assert.Equal(ExpectResult.Unknown, Eval(Obs(omitted: 5, truncated: true), ById("zzzzz")).Result);

    [Fact]
    public void Element_Selector_NameMatchesLabel_Pass() =>
        Assert.Equal(ExpectResult.Pass,
            Eval(Obs([Element("a1", "Edit", "", label: "Focus Axis", value: "1")]), Sel("Edit", "focus axis", value: "1")).Result);

    [Fact]
    public void Element_Selector_NameAndLabelSameElement_CountsOnce() =>
        Assert.Equal(ExpectResult.Pass,
            Eval(Obs([Element("a1", "Edit", "Focus", label: "Focus")]), Sel("Edit", "Focus", ExpectState.Enabled)).Result);

    [Fact]
    public void Element_Selector_Panel_Narrows()
    {
        var observation = Obs([
            Element("a1", "ComboBox", "Mode", panel: "Toolbar", value: "Default"),
            Element("a2", "ComboBox", "Mode", panel: "Microscope", value: "IL-BF"),
        ]);

        Assert.Equal(ExpectResult.Unknown, Eval(observation, Sel("ComboBox", "Mode", value: "IL-BF")).Result);
        Assert.Equal(ExpectResult.Pass, Eval(observation, Sel("ComboBox", "Mode", panel: "microscope", value: "IL-BF")).Result);
    }

    [Fact]
    public void Element_Selector_PanelGiven_ElementWithoutPanel_DoesNotMatch() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs([Element("a1", "Button", "OK")]), Sel("Button", "OK", panel: "Toolbar")).Result);

    [Fact]
    public void Element_Selector_InWindow_Dialog()
    {
        var observation = Obs(
            [Element("a1", "Button", "OK", window: "Login"), Element("a2", "Button", "OK")],
            [Window("Login", modal: true), Window("MCS")]);

        var outcome = Eval(observation, Sel("Button", "OK", ExpectState.Enabled, inWindow: "Login"));

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal(["a1"], outcome.ElementIds);
    }

    [Fact]
    public void Element_Selector_InWindow_MainWindowTitle()
    {
        var observation = Obs(
            [Element("a1", "Button", "OK", window: "Login"), Element("a2", "Button", "OK")],
            [Window("Login", modal: true), Window("MCS")]);

        Assert.Equal(["a2"], Eval(observation, Sel("Button", "OK", ExpectState.Enabled, inWindow: "mcs")).ElementIds);
    }

    [Fact]
    public void Element_Selector_TypeOnly()
    {
        var observation = Obs([Element("a1", "ProgressBar", ""), Element("a2", "Button", "OK")]);

        Assert.Equal(["a1"], Eval(observation, Sel("progressbar")).ElementIds);
    }

    [Fact]
    public void Element_Selector_NameOnly_AnyType() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs([Element("a1", "MenuItem", "File")]), Sel(name: "File")).Result);

    [Fact]
    public void Element_Selector_ContainsMatch() =>
        Assert.Equal(ExpectResult.Pass,
            Eval(Obs([Element("a1", "Button", "StartLiveVideo")]), Sel("Button", "livevideo", match: ExpectMatch.Contains)).Result);

    [Fact]
    public void Element_Selector_ExactMatch_DoesNotMatchAPart() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs([Element("a1", "Button", "StartLiveVideo")]), Sel("Button", "Start")).Result);

    // ── text ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_Found_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(texts: ["Ready", "Measurement finished at 10:02"]), new TextExpectation(1, "Measurement finished")).Result);

    [Fact]
    public void Text_CaseInsensitive_Pass() =>
        Assert.Equal(ExpectResult.Pass, Eval(Obs(texts: ["Measurement Finished"]), new TextExpectation(1, "measurement finished")).Result);

    [Fact]
    public void Text_MissingComplete_Fail() =>
        Assert.Equal(ExpectResult.Fail, Eval(Obs(texts: ["Ready"]), new TextExpectation(1, "finished")).Result);

    [Fact]
    public void Text_MissingTruncated_Unknown() =>
        Assert.Equal(ExpectResult.Unknown, Eval(Obs(texts: ["Ready"], truncated: true), new TextExpectation(1, "finished")).Result);

    [Fact]
    public void Text_MissingButUnreadableWindow_Unknown() =>
        Assert.Equal(ExpectResult.Unknown,
            Eval(Obs(windows: [Window("Dialog", unreadable: true)], texts: ["Ready"]), new TextExpectation(1, "finished")).Result);

    // ── several conditions, overall, description ─────────────────────────────────────────────────

    [Fact]
    public void Evaluate_KeepsOrderAndIndexes()
    {
        var outcomes = ExpectationEvaluator.Evaluate(
            Obs([Element("a1", "Button", "OK")]),
            [new WindowExpectation(1, "Main", ExpectWindowState.Open, null, ExpectMatch.Exact), new TextExpectation(2, "x"),
             new ElementExpectation(3, "a1", null, null, null, null, ExpectState.Exists, null, null, ExpectMatch.Exact)]);

        Assert.Equal([1, 2, 3], outcomes.Select(o => o.Index));
        Assert.Equal([ExpectResult.Pass, ExpectResult.Fail, ExpectResult.Pass], outcomes.Select(o => o.Result));
    }

    [Theory]
    [InlineData(new[] { ExpectResult.Pass, ExpectResult.Pass }, ExpectResult.Pass)]
    [InlineData(new[] { ExpectResult.Pass, ExpectResult.Fail, ExpectResult.Unknown }, ExpectResult.Fail)]
    [InlineData(new[] { ExpectResult.Pass, ExpectResult.Unknown }, ExpectResult.Unknown)]
    public void Overall(ExpectResult[] results, ExpectResult expected)
    {
        var outcomes = results.Select((r, i) => new ConditionOutcome(i + 1, r, "c", "a", [])).ToList();

        Assert.Equal(expected, ExpectationEvaluator.Overall(outcomes));
    }

    [Fact]
    public void Describe_Window()
    {
        Assert.Equal("window 'Login' open", ExpectationEvaluator.Describe(Win("Login")));
        Assert.Equal("window 'Login' open, modal", ExpectationEvaluator.Describe(Win("Login", modal: true)));
        Assert.Equal("window 'Login' open, not modal", ExpectationEvaluator.Describe(Win("Login", modal: false)));
        Assert.Equal("window containing 'Opt' closed", ExpectationEvaluator.Describe(Win("Opt", ExpectWindowState.Closed, match: ExpectMatch.Contains)));
    }

    [Fact]
    public void Describe_Element()
    {
        Assert.Equal("Button 'OK' enabled", ExpectationEvaluator.Describe(Sel("Button", "OK", ExpectState.Enabled)));
        Assert.Equal("Button 'OK' exists", ExpectationEvaluator.Describe(Sel("Button", "OK")));
        Assert.Equal("ProgressBar exists", ExpectationEvaluator.Describe(Sel("ProgressBar")));
        Assert.Equal("element 'File' absent", ExpectationEvaluator.Describe(Sel(name: "File", state: ExpectState.Absent)));
        Assert.Equal("TabItem 'Camera' not selected", ExpectationEvaluator.Describe(Sel("TabItem", "Camera", ExpectState.NotSelected)));
        Assert.Equal("Edit 'User' value 'admin'", ExpectationEvaluator.Describe(Sel("Edit", "User", value: "admin")));
        Assert.Equal("Edit 'User' enabled, value contains 'adm'",
            ExpectationEvaluator.Describe(Sel("Edit", "User", ExpectState.Enabled, valueContains: "adm")));
        Assert.Equal("Edit containing 'Focus' @Microscope in 'MCS' value '1'",
            ExpectationEvaluator.Describe(Sel("Edit", "Focus", panel: "Microscope", inWindow: "MCS", value: "1", match: ExpectMatch.Contains)));
        Assert.Equal("element ejsbw value 'x'", ExpectationEvaluator.Describe(ById("ejsbw", value: "x")));
        Assert.Equal("element ejsbw enabled", ExpectationEvaluator.Describe(ById("ejsbw", ExpectState.Enabled)));
    }

    [Fact]
    public void Describe_Text() =>
        Assert.Equal("text 'finished'", ExpectationEvaluator.Describe(new TextExpectation(1, "finished")));

    [Fact]
    public void Outcome_CarriesTheDescription() =>
        Assert.Equal("Button 'OK' enabled", Eval(Obs([Element("a1", "Button", "OK")]), Sel("Button", "OK", ExpectState.Enabled)).Condition);
}
