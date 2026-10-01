using System.Drawing;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// Element-id paths of Click/Type/MultiSelect/MultiEdit. Resolution failures (empty or stale store) happen
/// before any input is sent; success paths run against fakes via the internal post-resolve methods.
/// Every executor built here uses a fake input driver, and the unknown/stale-element tests use empty
/// text and no label/loc, so a regression in the element branch cannot send real input.
/// </summary>
[Collection(McpToolsCollection.Name)]
public class ElementActionToolsTests(McpToolsFixture fixture)
{
    private const string NoSuchProcess = "no_such_process_xyz";
    private static string NotFound(string id) => $"[ERROR] ElementNotFoundException: element {id} no longer present — call Observe";

    private static readonly ObservationService Svc = new(NullLogger<ObservationService>.Instance);

    private const string NoInteractiveDesktop =
        "no interactive desktop (session disconnected or not rendered) — nothing was clicked";

    private static ActionExecutor SafeExecutor(List<string>? log = null) => new(new FakeInputDriver(log ?? [])) { FocusWaitMs = 0 };

    /// <summary>Executor in a disconnected or non-rendered session: nothing can be clicked there.</summary>
    private static ActionExecutor NoDesktopExecutor(List<string> log) =>
        new(new FakeInputDriver(log) { HasInteractiveDesktop = false }) { FocusWaitMs = 0 };

    private static UiTreeService Ui => null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static ResolvedElement Resolved(IActionTarget target, string describe) =>
        new(target, describe, new ElementLocator("p", "c", []), Pid: 4711, WindowHandle: 0x1234);

    /// <summary>Store whose id "e5stl" resolves to a locator naming a process that does not exist, so
    /// <c>store.Get</c> succeeds and <c>FindLive</c> returns null (the realistic stale case).</summary>
    private static ObservationStore StaleStore()
    {
        var store = new ObservationStore();
        var locator = new ElementLocator(NoSuchProcess, "NoClass", [new LocatorStep("Button", "Save", 0)]);
        var element = new ObservedElement("e5stl", "Button", "Save", null, null, null, null, null, false, null, true,
            new Rectangle(0, 0, 10, 10), locator);
        store.Remember(new Observation([], null, [element], [], "sig", false, new ObservationTimings(0, 0, 0)));
        return store;
    }

    // --- unknown ids (empty store) ----------------------------------------------------------------

    [Fact]
    public void Click_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e9zz9"), InputTools.Click(Ui, Svc, new ObservationStore(), SafeExecutor(), element: "e9zz9", ct: Ct));

    [Fact]
    public void Type_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e9zz9"), InputTools.Type(Ui, Svc, new ObservationStore(), SafeExecutor(), "", element: "e9zz9", ct: Ct));

    [Fact]
    public void MultiSelect_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e9zz9"), MultiTools.MultiSelect(Ui, Svc, new ObservationStore(), SafeExecutor(), elements: ["e9zz9"], ct: Ct));

    [Fact]
    public void MultiEdit_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e9zz9"), MultiTools.MultiEdit(Ui, Svc, new ObservationStore(), SafeExecutor(),
            elements: Json("""[["e9zz9",""]]"""), ct: Ct));

    // --- stale ids (store knows the id, FindLive returns null) -------------------------------------

    [Fact]
    public void Click_StaleElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e5stl"), InputTools.Click(Ui, Svc, StaleStore(), SafeExecutor(), element: "e5stl", ct: Ct));

    [Fact]
    public void Type_StaleElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e5stl"), InputTools.Type(Ui, Svc, StaleStore(), SafeExecutor(), "", element: "e5stl", ct: Ct));

    [Fact]
    public void MultiSelect_StaleElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e5stl"), MultiTools.MultiSelect(Ui, Svc, StaleStore(), SafeExecutor(), elements: ["e5stl"], ct: Ct));

    [Fact]
    public void MultiEdit_StaleElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound("e5stl"), MultiTools.MultiEdit(Ui, Svc, StaleStore(), SafeExecutor(),
            elements: Json("""[["e5stl",""]]"""), ct: Ct));

    // --- MultiEdit elements shape / null ----------------------------------------------------------

    [Theory]
    [InlineData("""[["e1"]]""")]
    [InlineData("""[["e1","a","b"]]""")]
    [InlineData("""[["e1",5]]""")]
    [InlineData("""["e1"]""")]
    [InlineData("""{"a":1}""")]
    public void MultiEdit_ElementsShape_Validated(string json)
    {
        var result = MultiTools.MultiEdit(Ui, Svc, new ObservationStore(), SafeExecutor(), elements: Json(json), ct: Ct);

        Assert.StartsWith("[ERROR] ArgumentException", result);
    }

    [Fact]
    public void MultiEdit_ExplicitNullElements_IsTreatedAsNotProvided()
    {
        // Same result as omitting elements entirely: the labels/locs path reports "no fields".
        var result = MultiTools.MultiEdit(Ui, Svc, new ObservationStore(), SafeExecutor(), elements: Json("null"), ct: Ct);

        Assert.Equal("[ERROR] ArgumentException: No fields specified. Provide 'locs' or 'labels'.", result);
    }

    [Fact]
    public void MultiSelect_WithoutLabelsLocsOrElements_KeepsLegacyError() =>
        Assert.Equal("[ERROR] ArgumentException: No targets specified. Provide 'labels' or 'locs'.",
            MultiTools.MultiSelect(Ui, Svc, new ObservationStore(), SafeExecutor(), ct: Ct));

    [Fact]
    public void MultiSelect_EmptyElements_FallsThroughToTheLegacyError() =>
        Assert.Equal("[ERROR] ArgumentException: No targets specified. Provide 'labels' or 'locs'.",
            MultiTools.MultiSelect(Ui, Svc, new ObservationStore(), SafeExecutor(), elements: [], ct: Ct));

    [Fact]
    public void Click_WithoutLabelOrLocOrElement_KeepsLegacyError() =>
        Assert.Equal("[ERROR] ArgumentException: Either 'label' or 'loc' ([x, y]) must be provided.",
            InputTools.Click(Ui, Svc, new ObservationStore(), SafeExecutor(), ct: Ct));

    // --- Click success paths ------------------------------------------------------------------------

    private static Func<string> Sig(List<string> log, params string[] values)
    {
        var queue = new Queue<string>(values);
        return () => { log.Add("sig"); return queue.Dequeue(); };
    }

    [Fact]
    public void ClickResolved_SingleLeftClick_VerifyOn_Format()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox" };

        var result = InputTools.ClickResolved(Resolved(target, "e7q2k (CheckBox 'Auto')"), SafeExecutor(log),
            MouseButton.Left, 1, ActionMethod.Auto, Sig(log, "A", "B"), 0, (_, _, _, _) => log.Add("mouse")).Text;

        Assert.Equal("Clicked e7q2k (CheckBox 'Auto') via Toggle — effect: changed", result);
        Assert.Equal(["sig", "Toggle", "sig"], log);
    }

    [Fact]
    public void ClickResolved_SingleLeftClick_OnAButton_GoesThroughTheExecutorsMouseFirstPath()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", CurrentRect = new Rectangle(100, 200, 40, 20) };

        var result = InputTools.ClickResolved(Resolved(target, "e7q2k (Button 'Save')"), SafeExecutor(log),
            MouseButton.Left, 1, ActionMethod.Auto, Sig(log, "A", "B"), 0, (_, _, _, _) => log.Add("mouse")).Text;

        Assert.Equal("Clicked e7q2k (Button 'Save') via mouse — effect: changed", result);
        Assert.Equal(["sig", "LeftClick:120,210", "sig"], log);
    }

    [Fact]
    public void ClickResolved_VerifyOff_NullSignature_NotVerified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox" };

        var result = InputTools.ClickResolved(Resolved(target, "e7q2k (CheckBox 'Auto')"), SafeExecutor(log),
            MouseButton.Left, 1, ActionMethod.Auto, null, 0, (_, _, _, _) => { }).Text;

        Assert.Equal("Clicked e7q2k (CheckBox 'Auto') via Toggle — effect: not_verified", result);
    }

    [Theory]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    public void ClickResolved_NonSingleLeft_UsesMouseAtRectCentre_Verified(MouseButton button, int clicks)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", CurrentRect = new Rectangle(100, 200, 40, 20) };

        var result = InputTools.ClickResolved(Resolved(target, "e1 (Button 'X')"), SafeExecutor(log),
            button, clicks, ActionMethod.Auto, Sig(log, "A", "B"), 0,
            (x, y, b, c) => log.Add($"mouse:{x},{y},{b},{c}")).Text;

        Assert.Equal("Clicked e1 (Button 'X') via mouse — effect: changed", result);
        Assert.Equal(["sig", $"mouse:120,210,{button},{clicks}", "sig"], log);
    }

    [Theory]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    public void ClickResolved_PatternWithNonSingleLeft_Throws(MouseButton button, int clicks)
    {
        var ex = Assert.Throws<ArgumentException>(() => InputTools.ClickResolved(
            Resolved(new FakeActionTarget([]), "e1"), SafeExecutor(), button, clicks, ActionMethod.Pattern, null, 0,
            (_, _, _, _) => { }));

        Assert.Equal("method=pattern supports only a single left click", ex.Message);
    }

    [Theory]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    public void ClickResolved_NonSingleLeft_ElementCovered_Throws_NothingClicked(MouseButton button, int clicks)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", Covered = true };

        var ex = Assert.Throws<InvalidOperationException>(() => InputTools.ClickResolved(
            Resolved(target, "e1 (Button 'X')"), SafeExecutor(log), button, clicks, ActionMethod.Auto, null, 0,
            (x, y, b, c) => log.Add($"mouse:{x},{y},{b},{c}")));

        Assert.Equal("the element is covered by another window — nothing was clicked", ex.Message);
        Assert.Empty(log);
    }

    [Theory]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    public void ClickResolved_NonSingleLeft_DisabledTarget_Throws_NothingClicked(MouseButton button, int clicks)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", IsEnabled = false };

        var ex = Assert.Throws<InvalidOperationException>(() => InputTools.ClickResolved(
            Resolved(target, "e1 (Button 'X')"), SafeExecutor(log), button, clicks, ActionMethod.Auto, Sig(log, "A", "B"), 0,
            (x, y, b, c) => log.Add($"mouse:{x},{y},{b},{c}")));

        Assert.Equal("Button is disabled — nothing was done", ex.Message);
        Assert.Empty(log);
    }

    [Theory]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    public void ClickResolved_NonSingleLeft_NoInteractiveDesktop_Throws_NothingClicked(MouseButton button, int clicks)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button" };

        var ex = Assert.Throws<InvalidOperationException>(() => InputTools.ClickResolved(
            Resolved(target, "e1 (Button 'X')"), NoDesktopExecutor(log), button, clicks, ActionMethod.Auto, null, 0,
            (x, y, b, c) => log.Add($"mouse:{x},{y},{b},{c}")));

        Assert.Equal(NoInteractiveDesktop, ex.Message);
        Assert.Empty(log);
    }

    // --- Type success path --------------------------------------------------------------------------

    [Fact]
    public void TypeResolved_ValuePattern_Format()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue("");        // previous
        target.ReadValues.Enqueue("12.50");   // read-back

        var result = InputTools.TypeResolved(Resolved(target, "e9k1a (Edit 'Focus Axis')"), SafeExecutor(log),
            "12.50", clear: true, pressEnter: false, Sig(log, "A"), 0).Text;

        Assert.Equal("Typed 5 chars into e9k1a (Edit 'Focus Axis') via ValuePattern — effect: value_verified", result);
    }

    // --- MultiEdit ------------------------------------------------------------------------------------

    private static FakeActionTarget EditTarget(List<string> log, string readBack)
    {
        var t = new FakeActionTarget(log) { CanSetValue = true };
        t.ReadValues.Enqueue("");
        t.ReadValues.Enqueue(readBack);
        return t;
    }

    [Fact]
    public void EditElements_ListsDescribeAndEffectPerField()
    {
        var log = new List<string>();
        var targets = new Dictionary<string, ResolvedElement>
        {
            ["e1"] = Resolved(EditTarget(log, "John"), "e1 (Edit 'First')"),
            ["e2"] = Resolved(EditTarget(log, "wrong"), "e2 (Edit 'Last')"),
        };

        var result = MultiTools.EditElements(Json("""[["e1","John"],["e2","Doe"]]"""), id => targets[id],
            SafeExecutor(log), _ => () => "s");

        Assert.Equal("Edited 2 field(s): e1 (Edit 'First'): value_verified, e2 (Edit 'Last'): value_mismatch", result);
    }

    [Fact]
    public void EditElements_NullSignature_ReportsNotVerified()
    {
        var log = new List<string>();
        var targets = new Dictionary<string, ResolvedElement>
        {
            ["e1"] = Resolved(EditTarget(log, "John"), "e1 (Edit 'First')"),
        };

        var result = MultiTools.EditElements(Json("""[["e1","John"]]"""), id => targets[id], SafeExecutor(log), _ => null);

        Assert.Equal("Edited 1 field(s): e1 (Edit 'First'): not_verified", result);
    }

    [Fact]
    public void EditElements_ResolvesAllBeforeFirstEdit()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { CanSetValue = true };
        var second = new FakeActionTarget(log) { CanSetValue = true };
        var targets = new Dictionary<string, ResolvedElement>
        {
            ["e1"] = Resolved(first, "e1"),
            ["e2"] = Resolved(second, "e2"),
        };

        ResolvedElement Resolve(string id) => targets.TryGetValue(id, out var r) ? r : throw new ElementNotFoundException(id);

        var ex = Assert.Throws<ElementNotFoundException>(() => MultiTools.EditElements(
            Json("""[["e1","a"],["e2","b"],["e3","c"]]"""), Resolve, SafeExecutor(log), _ => null));

        Assert.Equal("element e3 no longer present — call Observe", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void EditElements_FailureMidway_ListsAlreadyEdited()
    {
        var log = new List<string>();
        var targets = new Dictionary<string, ResolvedElement>
        {
            ["e1"] = Resolved(EditTarget(log, "a"), "e1 (Edit 'A')"),
            ["e2"] = Resolved(new FakeActionTarget(log) { ReadThrows = true }, "e2 (Edit 'B')"),
        };

        var result = MultiTools.EditElements(Json("""[["e1","a"],["e2","b"]]"""), id => targets[id],
            SafeExecutor(log), _ => null);

        Assert.Equal("[ERROR] InvalidOperationException: read failed (already edited: e1 (Edit 'A'): not_verified)", result);
    }

    // --- MultiSelect (element path, driven through the seam: no real input) ----------------------------

    private static string RunSelect(List<string> log, bool pressCtrl, params ResolvedElement[] resolved) =>
        RunSelect(log, SafeExecutor(log), pressCtrl, resolved);

    private static string RunSelect(List<string> log, ActionExecutor executor, bool pressCtrl, params ResolvedElement[] resolved) =>
        MultiTools.SelectElements(resolved, pressCtrl, executor,
            down => log.Add(down ? "ctrl:down" : "ctrl:up"),
            p => log.Add($"click:{p.X},{p.Y}"));

    [Fact]
    public void SelectElements_ClicksEachCentre_WhileHoldingCtrl()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { ControlType = "ListItem", CurrentRect = new Rectangle(0, 0, 10, 10) };
        var second = new FakeActionTarget(log) { ControlType = "ListItem", CurrentRect = new Rectangle(20, 0, 10, 10) };

        var result = RunSelect(log, pressCtrl: true, Resolved(first, "e1 (ListItem 'A')"), Resolved(second, "e2 (ListItem 'B')"));

        Assert.Equal("Multi-selected 2 element(s): e1 (ListItem 'A'), e2 (ListItem 'B')", result);
        Assert.Equal(["ctrl:down", "click:5,5", "click:25,5", "ctrl:up"], log);
    }

    [Fact]
    public void SelectElements_WithoutCtrl_OnlyClicks()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "ListItem" };

        RunSelect(log, pressCtrl: false, Resolved(target, "e1"));

        Assert.Equal(["click:5,5"], log);
    }

    [Fact]
    public void SelectElements_OneTargetCovered_Throws_BeforeCtrlAndBeforeAnyClick()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { ControlType = "ListItem" };
        var second = new FakeActionTarget(log) { ControlType = "ListItem", Covered = true };

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunSelect(log, pressCtrl: true, Resolved(first, "e1"), Resolved(second, "e2")));

        Assert.Equal("the element is covered by another window — nothing was clicked", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void SelectElements_OneTargetDisabled_Throws_BeforeCtrlAndBeforeAnyClick()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { ControlType = "ListItem" };
        var second = new FakeActionTarget(log) { ControlType = "ListItem", IsEnabled = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunSelect(log, pressCtrl: true, Resolved(first, "e1"), Resolved(second, "e2")));

        Assert.Equal("ListItem is disabled — nothing was done", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void SelectElements_NoInteractiveDesktop_Throws_BeforeCtrlAndBeforeAnyClick()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { ControlType = "ListItem" };
        var second = new FakeActionTarget(log) { ControlType = "ListItem" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunSelect(log, NoDesktopExecutor(log), pressCtrl: true, Resolved(first, "e1"), Resolved(second, "e2")));

        Assert.Equal(NoInteractiveDesktop, ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void SelectElements_TargetCoveredAfterAnEarlierClick_StopsReleasesCtrl_AndListsWhatWasClicked()
    {
        var log = new List<string>();
        var first = new FakeActionTarget(log) { ControlType = "ListItem" };
        var second = new FakeActionTarget(log) { ControlType = "ListItem", CurrentRect = new Rectangle(20, 0, 10, 10) };

        // The first click opens something (of another application) on top of the second target.
        var result = MultiTools.SelectElements([Resolved(first, "e1 (ListItem 'A')"), Resolved(second, "e2 (ListItem 'B')")],
            pressCtrl: true, SafeExecutor(log),
            down => log.Add(down ? "ctrl:down" : "ctrl:up"),
            p => { log.Add($"click:{p.X},{p.Y}"); second.Covered = true; });

        Assert.Equal("[ERROR] InvalidOperationException: the element is covered by another window — nothing was clicked " +
                     "(already clicked: e1 (ListItem 'A'))", result);
        Assert.Equal(["ctrl:down", "click:5,5", "ctrl:up"], log);
    }

    // --- modal dialogs (F2) ---------------------------------------------------------------------------

    [Fact]
    public void EnsureWindowNotBlocked_DisabledWindow_Throws_NamingTheElementAndTheWayOut()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ElementTargets.EnsureWindowNotBlocked("e7q2k", windowEnabled: false));

        Assert.Equal("element e7q2k is in a window blocked by a modal dialog — call Observe and handle the dialog first", ex.Message);
    }

    [Fact]
    public void EnsureWindowNotBlocked_EnabledWindow_Passes() =>
        ElementTargets.EnsureWindowNotBlocked("e7q2k", windowEnabled: true);

    // --- helpers ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(300, 300)]
    [InlineData(99999, 2000)]
    public void ClampSettle_ClampsTo0_2000(int input, int expected) =>
        Assert.Equal(expected, ElementTargets.ClampSettle(input));

    [Fact]
    public void GuardSignature_MapsFailuresToDash_ButPropagatesCancellation()
    {
        Assert.Equal("-", ElementTargets.GuardSignature(() => throw new InvalidOperationException())());
        Assert.Equal("-", ElementTargets.GuardSignature(() => throw new IOException())());
        Assert.Equal("sig", ElementTargets.GuardSignature(() => "sig")());
        Assert.Throws<OperationCanceledException>(() => ElementTargets.GuardSignature(() => throw new OperationCanceledException())());
    }

    // --- schema ----------------------------------------------------------------------------------------

    [Fact]
    public void Click_Schema_HasElementMethodVerifySettle()
    {
        var parameters = typeof(InputTools).GetMethod(nameof(InputTools.Click))!.GetParameters()
            .ToDictionary(p => p.Name!);

        Assert.Contains("element", parameters);
        Assert.Contains("method", parameters);
        Assert.Contains("verify", parameters);
        Assert.Contains("settle_ms", parameters);
        Assert.Equal(ActionMethod.Auto, parameters["method"].DefaultValue);
        Assert.Equal(300, parameters["settle_ms"].DefaultValue);
    }

    [Theory]
    [InlineData("Click", "element", "method", "verify", "settle_ms")]
    [InlineData("Type", "element", "verify", "settle_ms")]
    [InlineData("MultiSelect", "elements")]
    [InlineData("MultiEdit", "elements")]
    public void InputSchema_HasElementParameters_AndNoServices(string tool, params string[] expected)
    {
        var names = fixture.Tools[tool].ProtocolTool.InputSchema.GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToList();

        foreach (var name in expected)
            Assert.Contains(name, names);

        Assert.DoesNotContain("ct", names);
        Assert.DoesNotContain(names, n => n.EndsWith("Service", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("store", names);
        Assert.DoesNotContain("executor", names);
        Assert.DoesNotContain(names, n => n.Contains("observation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("MultiSelect")]
    [InlineData("MultiEdit")]
    public void MultiTools_Description_MentionsObserveElementIds(string tool)
    {
        var description = fixture.Tools[tool].ProtocolTool.Description!;

        Assert.Contains("Observe", description);
        Assert.Contains("elements", description);
    }
}
