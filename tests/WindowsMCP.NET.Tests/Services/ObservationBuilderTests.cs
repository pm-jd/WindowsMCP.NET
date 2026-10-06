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
        bool selected = false, string? expand = null, bool? hitVisible = true, bool password = false,
        bool inOtherWindow = false) =>
        new(index, parent, depth, window, controlType, name, automationId,
            new Rectangle(x, y, w, h), enabled, focused, value, toggle, selected, expand, hitVisible)
        {
            Password = password,
            InOtherWindow = inOtherWindow,
        };

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
    public void TabItemOfVisibleTab_DrawnInAnotherTopLevelWindow_IsNotListed()
    {
        // The exemption above is for UIA hit-tests that miss docking tabs. It must not rescue a tab item
        // whose centre lies in another top-level window (the Win32 pre-check): that one is a copy of
        // something drawn in a popup or dialog, listed there, and not clickable here.
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Tab", "MainTab", hitVisible: true),
            Node(2, 1, 2, 0, "TabItem", "General", hitVisible: false),
            Node(3, 1, 2, 0, "TabItem", "Options", hitVisible: false, inOtherWindow: true),
            Node(4, 3, 3, 0, "Button", "Apply", hitVisible: null),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Contains(observation.Elements, e => e.Name == "General");       // UIA missed it: still listed
        Assert.DoesNotContain(observation.Elements, e => e.Name == "Options"); // drawn elsewhere: not listed
        Assert.DoesNotContain(observation.Elements, e => e.Name == "Apply");   // ... and neither is its subtree
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
    public void DropDownItemListedUnderTheMainWindowToo_OnlyTheCopyInThePopupsOwnWindowStays()
    {
        // WinForms exposes an open drop-down as children of its menu item as well. The collector marks
        // the main-window copy as not hit-visible (its centre lies in the popup's window, BF1); the
        // popup's own window is not hit-tested at all.
        var windows = new[] { Window(0x11, "FileDropDown", className: "Popup"), Window(0x22, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "FileDropDown", hitVisible: null),
            Node(1, 0, 1, 0, "MenuItem", "ChangeUser", y: 40, hitVisible: null),
            Node(2, null, 0, 1, "Window", "Main", hitVisible: null),
            Node(3, 2, 1, 1, "MenuItem", "File", y: 10, hitVisible: true),
            Node(4, 3, 2, 1, "Menu", "FileDropDown", y: 30, hitVisible: null),
            Node(5, 4, 3, 1, "MenuItem", "ChangeUser", y: 40, hitVisible: false),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var item = Assert.Single(observation.Elements, e => e.Name == "ChangeUser");
        Assert.Equal("FileDropDown", item.Window);
        Assert.Equal((nint)0x11, item.WindowHandle);
        Assert.Contains(observation.Elements, e => e.Name == "File");
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

    // --- window identity (F4) ---------------------------------------------------------------------

    [Fact]
    public void IdenticalLocatorsInTwoWindows_GetDistinctHwndDerivedIds()
    {
        // Two windows of the same process and class (two Notepad windows, two MCS main windows):
        // the locators are identical, so the ids must be told apart by the window handle.
        var windows = new[] { Window(0x1000, "Doc 1"), Window(0x2000, "Doc 2") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Doc 1", hitVisible: null),
            Node(1, 0, 1, 0, "Button", "Open", hitVisible: null),
            Node(2, null, 0, 1, "Window", "Doc 2", hitVisible: null),
            Node(3, 2, 1, 1, "Button", "Open"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(2, observation.Elements.Count);
        var (first, second) = (observation.Elements[0], observation.Elements[1]);
        Assert.Equal(first.Locator, second.Locator);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Locator.Id(6, 0x1000), first.Id);
        Assert.Equal(second.Locator.Id(6, 0x2000), second.Id);
    }

    [Fact]
    public void UniqueLocator_KeepsShortId_NextToHwndDerivedDuplicates()
    {
        var windows = new[] { Window(0x1000, "Doc 1"), Window(0x2000, "Doc 2") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Doc 1", hitVisible: null),
            Node(1, 0, 1, 0, "Button", "Open", hitVisible: null),
            Node(2, 0, 1, 0, "Button", "OnlyHere", y: 50, hitVisible: null),
            Node(3, null, 0, 1, "Window", "Doc 2", hitVisible: null),
            Node(4, 3, 1, 1, "Button", "Open"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var unique = Assert.Single(observation.Elements, e => e.Name == "OnlyHere");
        Assert.Equal(unique.Locator.Id(4), unique.Id);
        Assert.All(observation.Elements.Where(e => e.Name == "Open"), e => Assert.Equal(7, e.Id.Length));
        Assert.Equal(3, observation.Elements.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void PrefixCollision_OfDifferentLocators_KeepsSixCharExtension()
    {
        // Find two different button names whose locators share the 4-char id (deterministic hash, so
        // the search result — and this test — is stable).
        ElementLocator LocatorFor(string name) => new("app.exe", "MainWin", [new LocatorStep("Button", name, 0)]);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        (string A, string B)? pair = null;
        for (var i = 0; i < 200_000 && pair is null; i++)
        {
            var name = $"Btn{i}";
            var id = LocatorFor(name).Id(4);
            if (seen.TryGetValue(id, out var other))
                pair = (other, name);
            else
                seen[id] = name;
        }

        Assert.NotNull(pair);
        var windows = new[] { Window(0x1000, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Button", pair.Value.A, y: 10),
            Node(2, 0, 1, 0, "Button", pair.Value.B, y: 20),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(2, observation.Elements.Count);
        Assert.All(observation.Elements, e => Assert.Equal(e.Locator.Id(6), e.Id));
        Assert.NotEqual(observation.Elements[0].Id, observation.Elements[1].Id);
    }

    [Fact]
    public void Elements_CarryWindowHandle_AndTransientForWindowsAboveTheMainWindow()
    {
        var windows = new[] { Window(0x11, "FileDropDown", className: "Popup"), Window(0x22, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "FileDropDown", hitVisible: null),
            Node(1, 0, 1, 0, "MenuItem", "Save", hitVisible: null),
            Node(2, null, 0, 1, "Window", "Main", hitVisible: null),
            Node(3, 2, 1, 1, "Button", "Open"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var popupItem = Assert.Single(observation.Elements, e => e.Name == "Save");
        Assert.Equal((nint)0x11, popupItem.WindowHandle);
        Assert.True(popupItem.Transient);

        var mainButton = Assert.Single(observation.Elements, e => e.Name == "Open");
        Assert.Equal((nint)0x22, mainButton.WindowHandle);
        Assert.False(mainButton.Transient);
    }

    [Fact]
    public void Transient_IsJudgedPerProcess()
    {
        // Desktop scope: the bottom-most window of EACH process is its main window, not only the
        // last window of the whole list.
        var windows = new[]
        {
            Window(0x11, "A dialog", className: "Dlg", process: "a.exe", pid: 1),
            Window(0x12, "A main", className: "MainA", process: "a.exe", pid: 1),
            Window(0x21, "B main", className: "MainB", process: "b.exe", pid: 2),
        };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "A dialog", hitVisible: null),
            Node(1, 0, 1, 0, "Button", "OK", hitVisible: null),
            Node(2, null, 0, 1, "Window", "A main", hitVisible: null),
            Node(3, 2, 1, 1, "Button", "Open"),
            Node(4, null, 0, 2, "Window", "B main", hitVisible: null),
            Node(5, 4, 1, 2, "Button", "Run"),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.True(Assert.Single(observation.Elements, e => e.Name == "OK").Transient);
        Assert.False(Assert.Single(observation.Elements, e => e.Name == "Open").Transient);
        Assert.False(Assert.Single(observation.Elements, e => e.Name == "Run").Transient);
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
        Assert.Equal(450, observation.Omitted); // 600 actionable elements, 150 listed (AF5)
    }

    [Fact]
    public void Omitted_CountsOnlyElementsCutByMaxElements()
    {
        ObservedNode[] Buttons(int count) =>
        [
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            .. Enumerable.Range(0, count).Select(i => Node(i + 1, 0, 1, 0, "Button", $"B{i}", y: i)),
        ];
        var windows = new[] { Window(1, "Main") };

        Assert.Equal(0, ObservationBuilder.Build(windows, Buttons(150), 150, Timings, budgetExceeded: false).Omitted);
        Assert.Equal(1, ObservationBuilder.Build(windows, Buttons(151), 150, Timings, budgetExceeded: false).Omitted);

        // Truncated for another reason (time budget): nothing is known to be missing, so no count.
        var budget = ObservationBuilder.Build(windows, Buttons(5), 150, Timings, budgetExceeded: true);
        Assert.True(budget.Truncated);
        Assert.Equal(0, budget.Omitted);
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

    // --- unreadable windows (AF2): an observation is never silently empty ----------------------------

    [Fact]
    public void UnreadableWindow_IsPartOfTheSignature_AndNeverEqualsTheEmptySignature()
    {
        string Signature(IReadOnlyList<ObservedWindow> windows, IReadOnlyList<ObservedNode> nodes) =>
            ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false).Signature;

        ObservedNode[] rootOnly = [Node(0, null, 0, 0, "Window", "Main", hitVisible: null)];
        ObservedNode[] withButton = [.. rootOnly, Node(1, 0, 1, 0, "Button", "Open")];

        var nothing = Signature([], []);
        var unreadable = Signature([Window(1, "Main") with { Unreadable = true }], []);

        Assert.Equal("e3b0c442", nothing); // the hash of nothing that the acceptance saw
        Assert.NotEqual(nothing, unreadable);
        // readable -> unreadable is a change: with elements, and also for a window that had none.
        Assert.NotEqual(Signature([Window(1, "Main")], withButton), unreadable);
        Assert.NotEqual(Signature([Window(1, "Main")], rootOnly), unreadable);
    }

    [Fact]
    public void UnreadableWindow_HasNoNodes_AndTheReadableWindowsAreBuiltAsUsual()
    {
        // A window that still answers lies above the one that does not.
        var windows = new[]
        {
            Window(0x11, "Tool", className: "Popup"),
            Window(0x22, "Main") with { Unreadable = true },
        };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Tool", hitVisible: null),
            Node(1, 0, 1, 0, "Button", "Close", hitVisible: null),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var button = Assert.Single(observation.Elements);
        Assert.Equal("Tool", button.Window);
        Assert.True(button.Transient);
        Assert.Equal([false, true], observation.Windows.Select(w => w.Unreadable));
    }

    // --- observation content (F5, F6, F7) ----------------------------------------------------------

    [Theory]
    [InlineData("Edit")]
    [InlineData("ComboBox")]
    [InlineData("Spinner")]
    [InlineData("Slider")]
    [InlineData("Document")]
    public void EmptyUnnamedInput_IsAlwaysEmitted(string inputType)
    {
        // An empty unnamed input field is exactly what an agent wants to type into; it needs an id.
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, inputType, "", value: null),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var element = Assert.Single(observation.Elements);
        Assert.Equal(inputType, element.Type);
        Assert.Equal("", element.Name);
        Assert.Null(element.Value);
        Assert.False(string.IsNullOrEmpty(element.Id));
    }

    [Fact]
    public void EmptyUnnamedEdit_StillGetsItsLabelFromThePrecedingText()
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, "Text", "Serial number", y: 10),
            Node(2, 0, 1, 0, "Edit", "", y: 20, value: null),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        var edit = Assert.Single(observation.Elements);
        Assert.Equal("Serial number", edit.Label);
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("MenuItem")]
    [InlineData("ListItem")]
    public void EmptyUnnamedNonInput_IsStillSkipped(string type)
    {
        var windows = new[] { Window(1, "Main") };
        var nodes = new[]
        {
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            Node(1, 0, 1, 0, type, "", value: null),
        };

        var observation = ObservationBuilder.Build(windows, nodes, 150, Timings, budgetExceeded: false);

        Assert.Empty(observation.Elements);
    }

    [Fact]
    public void PasswordNode_ValueIsNeverMapped_AndNotPartOfTheSignature()
    {
        Observation Build(string? value) => ObservationBuilder.Build(
            [Window(1, "Login")],
            [
                Node(0, null, 0, 0, "Window", "Login", hitVisible: null),
                Node(1, 0, 1, 0, "Edit", "Password", value: value, password: true),
            ],
            150, Timings, budgetExceeded: false);

        var withSecret = Build("hunter2");

        var element = Assert.Single(withSecret.Elements);
        Assert.Null(element.Value);
        Assert.True(element.Password); // a missing value says nothing about this field (Expect: unknown)
        Assert.Equal(Build("another secret").Signature, withSecret.Signature);
        Assert.Equal(Build(null).Signature, withSecret.Signature);
    }

    [Fact]
    public void Texts_AreCappedAt80_AndTheCutSetsTruncated()
    {
        ObservedNode[] TextNodes(int count) =>
        [
            Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
            .. Enumerable.Range(0, count).Select(i => Node(i + 1, 0, 1, 0, "Text", $"Text {i:000}", y: i)),
        ];

        var windows = new[] { Window(1, "Main") };

        var atLimit = ObservationBuilder.Build(windows, TextNodes(80), 150, Timings, budgetExceeded: false);
        Assert.Equal(80, atLimit.Texts.Count);
        Assert.False(atLimit.Truncated);

        var beyond = ObservationBuilder.Build(windows, TextNodes(100), 150, Timings, budgetExceeded: false);
        Assert.Equal(80, beyond.Texts.Count);
        Assert.Equal("Text 000", beyond.Texts[0]);
        Assert.Equal("Text 079", beyond.Texts[^1]);
        Assert.True(beyond.Truncated);
    }

    [Fact]
    public void Texts_DuplicatesDoNotCountTowardsTheCap()
    {
        var nodes = new List<ObservedNode> { Node(0, null, 0, 0, "Window", "Main", hitVisible: null) };
        for (var i = 0; i < 200; i++)
            nodes.Add(Node(nodes.Count, 0, 1, 0, "Text", $"Text {i % 80:000}", y: i));

        var observation = ObservationBuilder.Build([Window(1, "Main")], nodes, 150, Timings, budgetExceeded: false);

        Assert.Equal(80, observation.Texts.Count);
        Assert.False(observation.Truncated);
    }

    [Fact]
    public void Signature_UsesTheFullValue_AlsoBeyondTheDisplayLimit()
    {
        string Signature(string value) => ObservationBuilder.Build(
            [Window(1, "Main")],
            [
                Node(0, null, 0, 0, "Window", "Main", hitVisible: null),
                Node(1, 0, 1, 0, "Document", "Editor", value: value),
            ],
            150, Timings, budgetExceeded: false).Signature;

        var longText = new string('x', 5000);

        Assert.NotEqual(Signature(longText + "a"), Signature(longText + "b"));
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
