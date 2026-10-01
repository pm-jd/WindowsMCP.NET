using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ObservationBuilder"/> is a pure function over synthetic <see cref="ObservedWindow"/>/
/// <see cref="ObservedNode"/> lists — no UIA involved — so every rule from the design spec (visibility
/// cascade, actionable filtering, label/panel derivation, ordering, truncation, ids, signature) is
/// exercised here without a desktop session.
/// </summary>
public class ObservationBuilderTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 1, HitMs: 1, TotalMs: 2);

    private static ObservedWindow Window(
        nint handle, string title, string className = "MainWin", string process = "app.exe",
        int pid = 100, bool foreground = true, bool modal = false,
        int x = 0, int y = 0, int w = 800, int h = 600) =>
        new(handle, title, className, process, pid, foreground, modal, new Rectangle(x, y, w, h));

    private static ObservedNode Node(
        int index, int? parent, int depth, int window, string controlType, string name,
        string automationId = "", int x = 0, int y = 0, int w = 10, int h = 10,
        bool enabled = true, bool focused = false, string? value = null, string? toggle = null,
        bool selected = false, string? expand = null, bool? hitVisible = true) =>
        new(index, parent, depth, window, controlType, name, automationId,
            new Rectangle(x, y, w, h), enabled, focused, value, toggle, selected, expand, hitVisible);

    [Fact]
    public void DropsHitInvisibleSubtree()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Pane", "DockPanel", hitVisible: false),
            Node(2, 1, 2, 0, "Button", "Hidden", hitVisible: true),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.DoesNotContain(observation.Elements, e => e.Name == "Hidden");
    }

    [Theory]
    [InlineData("", "Save", "Save")]
    [InlineData("btnSave", "Save", "btnSave")]
    [InlineData(null, null, "")]
    public void LocatorKey_PrefersAutomationIdElseName(string? automationId, string? name, string expected) =>
        Assert.Equal(expected, ObservationBuilder.LocatorKey(automationId, name));

    [Fact]
    public void DocumentControl_IsActionable()
    {
        var windows = new[] { Window(1, "Notepad") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Notepad", hitVisible: null),
            Node(1, 0, 1, 0, "Document", "Text editor", hitVisible: true),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Contains(observation.Elements, e => e.Type == "Document");
    }

    [Fact]
    public void KeepsTabItemOfVisibleTab()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Tab", "MainTab", hitVisible: true),
            Node(2, 1, 2, 0, "TabItem", "General", hitVisible: false),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Contains(observation.Elements, e => e.Name == "General" && e.Type == "TabItem");
    }

    [Fact]
    public void NodesWithoutHitTest_AreKept()
    {
        var windows = new[] { Window(1, "FileDropDown"), Window(2, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "FileDropDown", hitVisible: null),
            Node(1, 0, 1, 0, "MenuItem", "Open recent", hitVisible: null),
            Node(2, null, 0, 1, "Window", "Main", hitVisible: true),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Contains(observation.Elements, e => e.Name == "Open recent");
    }

    [Fact]
    public void PopupElementsComeFirst()
    {
        var windows = new[] { Window(1, "FileDropDown"), Window(2, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "FileDropDown", hitVisible: null),
            Node(1, 0, 1, 0, "MenuItem", "Save", y: 10, hitVisible: null),
            Node(2, null, 0, 1, "Window", "Main", hitVisible: true),
            Node(3, 2, 1, 1, "Button", "Open", y: 10, hitVisible: true),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(2, observation.Elements.Count);
        Assert.Equal("Save", observation.Elements[0].Name);
        Assert.Equal("FileDropDown", observation.Elements[0].Window);
        Assert.Equal("Open", observation.Elements[1].Name);
        Assert.Null(observation.Elements[1].Window);
    }

    [Fact]
    public void UnnamedEdit_GetsPrecedingTextLabel()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Text", "Focus Axis", y: 10),
            Node(2, 0, 1, 0, "Edit", "", y: 20, value: "90,000"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var edit = Assert.Single(observation.Elements, e => e.Type == "Edit");
        Assert.Equal("Focus Axis", edit.Label);
    }

    [Fact]
    public void Panel_UsesNamedAncestor_AndToolbarFallback()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "ToolBar", ""),
            Node(2, 1, 2, 0, "Button", "SnapImage"),
            Node(3, 0, 1, 0, "Group", "Nosepiece"),
            Node(4, 3, 2, 0, "RadioButton", "20x"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var button = Assert.Single(observation.Elements, e => e.Name == "SnapImage");
        Assert.Equal("Toolbar", button.Panel);

        var radio = Assert.Single(observation.Elements, e => e.Name == "20x");
        Assert.Equal("Nosepiece", radio.Panel);
    }

    [Fact]
    public void DuplicateSiblings_GetDistinctIds()
    {
        (string First, string Second) BuildTwoIds()
        {
            var windows = new[] { Window(1, "Main") };
            var nodes = new[]
            {
                Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
                Node(1, 0, 1, 0, "Button", "Open"),
                Node(2, 0, 1, 0, "Button", "Open"),
            };

            var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);
            Assert.Equal(2, observation.Elements.Count);
            return (observation.Elements[0].Id, observation.Elements[1].Id);
        }

        var first = BuildTwoIds();
        var second = BuildTwoIds();

        Assert.NotEqual(first.First, first.Second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Truncates_AtMaxElements()
    {
        var windows = new[] { Window(1, "Popup"), Window(2, "Main") };
        var nodes = new List<ObservedNode>
        {
            Node(0, null, 0, 0, "Window", "Popup", hitVisible: null),
            Node(1, null, 0, 1, "Window", "Main", hitVisible: true),
        };

        for (var i = 0; i < 10; i++)
            nodes.Add(Node(nodes.Count, 0, 1, 0, "Button", $"PopupBtn{i}", y: i, hitVisible: null));
        for (var i = 0; i < 590; i++)
            nodes.Add(Node(nodes.Count, 1, 1, 1, "Button", $"MainBtn{i}", y: i, hitVisible: true));

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(150, observation.Elements.Count);
        Assert.True(observation.Truncated);
        Assert.Equal(10, observation.Elements.Count(e => e.Window == "Popup"));
    }

    [Fact]
    public void BudgetExceeded_SetsTruncated()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Button", "Open"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: true);

        Assert.True(observation.Truncated);
    }

    [Fact]
    public void Signature_ChangesOnValueToggleSelectedEnabled_NotOnRect()
    {
        ObservedNode[] MakeNodes(string? value = null, string? toggle = null, bool selected = false,
            bool enabled = true, int x = 0) =>
        [
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "CheckBox", "Auto", x: x, value: value, toggle: toggle, selected: selected, enabled: enabled),
        ];

        var windows = new[] { Window(1, "Main") };

        string Signature(ObservedNode[] nodes) =>
            ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false).Signature;

        var baseline = Signature(MakeNodes());
        var rectOnly = Signature(MakeNodes(x: 99));
        var valueChanged = Signature(MakeNodes(value: "on"));
        var toggleChanged = Signature(MakeNodes(toggle: "On"));
        var selectedChanged = Signature(MakeNodes(selected: true));
        var enabledChanged = Signature(MakeNodes(enabled: false));

        Assert.Equal(baseline, rectOnly);
        Assert.NotEqual(baseline, valueChanged);
        Assert.NotEqual(baseline, toggleChanged);
        Assert.NotEqual(baseline, selectedChanged);
        Assert.NotEqual(baseline, enabledChanged);
    }

    [Fact]
    public void Texts_AreDistinctAndNotActionable()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Group", "Nosepiece", y: 5),
            Node(2, 0, 1, 0, "Text", "Nosepiece", y: 10),
            Node(3, 0, 1, 0, "Header", "Illumination", y: 20),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(["Nosepiece", "Illumination"], observation.Texts);
        Assert.Empty(observation.Elements);
    }
}
