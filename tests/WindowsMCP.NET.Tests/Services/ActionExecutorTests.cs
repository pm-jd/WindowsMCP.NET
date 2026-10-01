using System.Drawing;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ActionExecutor"/> against hand-written fakes for <see cref="IActionTarget"/> and
/// <see cref="IInputDriver"/> — no FlaUI/UIA dependency, so these run without a desktop session.
/// <paramref name="settleMs"/>-style delays are always 0 in these tests so the suite stays fast.
/// </summary>
public class ActionExecutorTests
{
    // --- Click -----------------------------------------------------------------------------------

    [Fact]
    public void Click_Button_UsesInvoke_Changed()
    {
        var target = new FakeActionTarget { ControlType = "Button" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Invoke"], target.Calls);
        Assert.Empty(input.LeftClicks);
    }

    [Fact]
    public void Click_RadioButton_UsesSelectionItem()
    {
        var target = new FakeActionTarget { ControlType = "RadioButton" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("SelectionItem", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Select"], target.Calls);
    }

    [Fact]
    public void Click_CheckBox_UsesToggle()
    {
        var target = new FakeActionTarget { ControlType = "CheckBox" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("Toggle", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Toggle"], target.Calls);
    }

    [Fact]
    public void Click_MenuItemWithChildren_UsesExpandCollapse()
    {
        var target = new FakeActionTarget { ControlType = "MenuItem", HasChildren = true };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("ExpandCollapse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["ExpandCollapse"], target.Calls);
    }

    [Fact]
    public void Click_MenuItemWithoutChildren_UsesInvoke()
    {
        var target = new FakeActionTarget { ControlType = "MenuItem", HasChildren = false };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(["Invoke"], target.Calls);
    }

    [Fact]
    public void Click_ComboBox_UsesExpandCollapse()
    {
        var target = new FakeActionTarget { ControlType = "ComboBox" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("ExpandCollapse", outcome.Via);
        Assert.Equal(["ExpandCollapse"], target.Calls);
    }

    [Fact]
    public void Click_PatternUnchanged_FallsBackToMouseAtCurrentRectCentre()
    {
        var target = new FakeActionTarget { ControlType = "Button", CurrentRect = new Rectangle(100, 200, 40, 20) };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        // before, after-pattern (unchanged), after-mouse (changed).
        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal([new Point(120, 210)], input.LeftClicks);
    }

    [Fact]
    public void Click_NoPatternForControlType_FallsBackToMouse()
    {
        var target = new FakeActionTarget { ControlType = "Edit", CurrentRect = new Rectangle(0, 0, 10, 10) };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Empty(target.Calls);
        Assert.Single(input.LeftClicks);
    }

    [Fact]
    public void Click_MethodPattern_NoFallback()
    {
        var target = new FakeActionTarget { ControlType = "Button" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Pattern, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Empty(input.LeftClicks);
    }

    [Fact]
    public void Click_MethodPattern_NoUsablePattern_Throws()
    {
        var target = new FakeActionTarget { ControlType = "Edit" };
        var executor = new ActionExecutor(new FakeInputDriver());

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, ActionMethod.Pattern, signature: null, settleMs: 0));

        Assert.Contains("Edit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Click_MethodPattern_PatternFails_Throws()
    {
        var target = new FakeActionTarget { ControlType = "Button", InvokeResult = false };
        var executor = new ActionExecutor(new FakeInputDriver());

        Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, ActionMethod.Pattern, signature: null, settleMs: 0));
    }

    [Fact]
    public void Click_MethodMouse_SkipsPattern()
    {
        var target = new FakeActionTarget { ControlType = "Button", CurrentRect = new Rectangle(0, 0, 10, 10) };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Mouse, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Empty(target.Calls);
        Assert.Single(input.LeftClicks);
    }

    [Fact]
    public void Click_NoSignature_NotVerified()
    {
        var target = new FakeActionTarget { ControlType = "Button" };
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Click(target, ActionMethod.Auto, signature: null, settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(ActionEffect.NotVerified, outcome.Effect);
        Assert.Empty(input.LeftClicks);
    }

    // --- Type ------------------------------------------------------------------------------------

    [Fact]
    public void Type_ValuePattern_Verified()
    {
        var target = new FakeActionTarget { CanSetValue = true };
        target.ReadValues.Enqueue("existing"); // previous, read before acting
        target.ReadValues.Enqueue("existingHello"); // read-back after acting
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Type(target, "Hello", clear: false, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal("ValuePattern", outcome.Via);
        Assert.Equal(ActionEffect.ValueVerified, outcome.Effect);
        Assert.Equal("existingHello", target.LastSetValue);
        Assert.Empty(input.TypedTexts);
    }

    [Fact]
    public void Type_ReadBackMismatch_ReportsMismatch()
    {
        var target = new FakeActionTarget { CanSetValue = true };
        target.ReadValues.Enqueue(""); // previous
        target.ReadValues.Enqueue("unexpected"); // read-back after acting
        var executor = new ActionExecutor(new FakeInputDriver());

        var outcome = executor.Type(target, "Hello", clear: true, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal("ValuePattern", outcome.Via);
        Assert.Equal(ActionEffect.ValueMismatch, outcome.Effect);
        Assert.Equal("Hello", target.LastSetValue);
    }

    [Fact]
    public void Type_NoValuePattern_UsesKeyboard()
    {
        var target = new FakeActionTarget { CanSetValue = false, CurrentRect = new Rectangle(5, 5, 10, 10) };
        target.ReadValues.Enqueue(null); // previous
        target.ReadValues.Enqueue(null); // no read-back available -> falls back to signature compare
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Type(target, "Hello", clear: false, pressEnter: false, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Focus"], target.Calls);
        Assert.Equal([("Hello", false, false)], input.TypedTexts);
        Assert.Empty(input.LeftClicks);
    }

    [Fact]
    public void Type_NoValuePattern_FocusFails_ClicksBeforeTyping()
    {
        var target = new FakeActionTarget { CanSetValue = false, CurrentRect = new Rectangle(5, 5, 10, 10), FocusResult = false };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        var outcome = executor.Type(target, "Hi", clear: false, pressEnter: false, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Equal([new Point(10, 10)], input.LeftClicks);
    }

    [Fact]
    public void Type_NoSignature_NotVerified()
    {
        var target = new FakeActionTarget { CanSetValue = true };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);
        var executor = new ActionExecutor(new FakeInputDriver());

        var outcome = executor.Type(target, "Hi", clear: false, pressEnter: false, signature: null, settleMs: 0);

        Assert.Equal(ActionEffect.NotVerified, outcome.Effect);
    }

    [Fact]
    public void Type_ValuePattern_PressEnter_SendsEnterViaInputDriver()
    {
        var target = new FakeActionTarget { CanSetValue = true };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue("Hi");
        var input = new FakeInputDriver();
        var executor = new ActionExecutor(input);

        executor.Type(target, "Hi", clear: false, pressEnter: true, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(1, input.PressEnterCalls);
    }

    // --- ToWire ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(ActionEffect.Changed, "changed")]
    [InlineData(ActionEffect.Unchanged, "unchanged")]
    [InlineData(ActionEffect.ValueVerified, "value_verified")]
    [InlineData(ActionEffect.ValueMismatch, "value_mismatch")]
    [InlineData(ActionEffect.NotVerified, "not_verified")]
    public void ToWire_Values(ActionEffect effect, string expected)
    {
        Assert.Equal(expected, effect.ToWire());
    }

    // --- Fakes -------------------------------------------------------------------------------------

    private static Func<string> SignatureSequence(params string[] values)
    {
        var queue = new Queue<string>(values);
        return () => queue.Dequeue();
    }

    private sealed class FakeActionTarget : IActionTarget
    {
        public string ControlType { get; init; } = "Edit";
        public bool HasChildren { get; init; }
        public Rectangle CurrentRect { get; init; } = new(0, 0, 10, 10);
        public bool CanSetValue { get; init; }

        public bool InvokeResult { get; init; } = true;
        public bool ExpandCollapseResult { get; init; } = true;
        public bool ToggleResult { get; init; } = true;
        public bool SelectResult { get; init; } = true;
        public bool SetValueResult { get; init; } = true;
        public bool FocusResult { get; init; } = true;

        public Queue<string?> ReadValues { get; } = new();
        public string? LastSetValue { get; private set; }
        public List<string> Calls { get; } = [];

        public bool TryInvoke() { Calls.Add("Invoke"); return InvokeResult; }
        public bool TryExpandCollapse() { Calls.Add("ExpandCollapse"); return ExpandCollapseResult; }
        public bool TryToggle() { Calls.Add("Toggle"); return ToggleResult; }
        public bool TrySelect() { Calls.Add("Select"); return SelectResult; }

        public bool TrySetValue(string value)
        {
            Calls.Add("SetValue");
            LastSetValue = value;
            return SetValueResult;
        }

        public string? ReadValue() => ReadValues.Count > 0 ? ReadValues.Dequeue() : null;

        public bool TryFocus() { Calls.Add("Focus"); return FocusResult; }
    }

    private sealed class FakeInputDriver : IInputDriver
    {
        public List<Point> LeftClicks { get; } = [];
        public List<(string Text, bool Clear, bool PressEnter)> TypedTexts { get; } = [];
        public int PressEnterCalls { get; private set; }

        public void LeftClick(Point p) => LeftClicks.Add(p);
        public void TypeText(string text, bool clear, bool pressEnter) => TypedTexts.Add((text, clear, pressEnter));
        public void PressEnter() => PressEnterCalls++;
    }
}
