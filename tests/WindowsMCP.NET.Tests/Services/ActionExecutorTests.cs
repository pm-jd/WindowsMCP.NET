using System.Drawing;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ActionExecutor"/> against hand-written fakes for <see cref="IActionTarget"/> and
/// <see cref="IInputDriver"/> — no FlaUI/UIA dependency, so these run without a desktop session.
/// Both fakes append to one shared ordered call log; settle delays are always 0.
/// </summary>
public class ActionExecutorTests
{
    private const string NoInteractiveDesktop =
        "no interactive desktop (session disconnected or not rendered) — nothing was clicked";

    // --- Click -----------------------------------------------------------------------------------

    // --- auto on Invoke-type controls: the mouse first when it can click safely (AF1) ----------------
    // An Invoke that opens a modal dialog does not return until the dialog is closed, and while it is
    // pending the application answers no UI Automation request. A mouse click has no such after-effect.

    [Theory]
    [InlineData("Button")]
    [InlineData("SplitButton")]
    [InlineData("Hyperlink")]
    [InlineData("MenuItem")] // a MenuItem WITHOUT children; one with children is expanded (below)
    public void Click_Auto_InvokeType_SafePoint_ClicksOnceWithTheMouse_NoPatternCall(string controlType)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = controlType, CurrentRect = new Rectangle(100, 200, 40, 20) };

        var outcome = Executor(log).Click(target, ActionMethod.Auto, LoggedSignature(log, "A", "B"), settleMs: 0);

        Assert.Equal(new ActionOutcome("mouse", ActionEffect.Changed), outcome);
        Assert.Equal(["sig", "LeftClick:120,210", "sig"], log); // exactly one click, no Invoke
    }

    [Fact]
    public void Click_Auto_InvokeType_MouseClickChangedNothing_IsNotFollowedByInvoke()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button" };

        var outcome = Executor(log).Click(target, ActionMethod.Auto, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal(new ActionOutcome("mouse", ActionEffect.Unchanged), outcome);
        Assert.Equal(["LeftClick:5,5"], log); // a second action could press the button twice
    }

    [Theory]
    [InlineData(true, false, false, true)]    // another window lies over the click point
    [InlineData(false, true, false, true)]    // the element has no area
    [InlineData(false, false, true, true)]    // the element's rectangle cannot be read
    [InlineData(false, false, false, false)]  // no interactive desktop: the pattern is all that works there
    public void Click_Auto_InvokeType_NoSafePoint_InvokesOnce_NoMouseClick(
        bool covered, bool emptyRect, bool rectGone, bool interactiveDesktop)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", Covered = covered, RectGone = rectGone };
        if (emptyRect)
            target.CurrentRect = Rectangle.Empty;
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = interactiveDesktop });

        var outcome = executor.Click(target, ActionMethod.Auto, LoggedSignature(log, "A", "B"), settleMs: 0);

        Assert.Equal(new ActionOutcome("Invoke", ActionEffect.Changed), outcome);
        Assert.Equal(["sig", "Invoke", "sig"], log); // exactly one Invoke, no click
    }

    [Fact]
    public void Click_Auto_InvokeType_FallbackInvokeChangedNothing_IsNeverRepeatedWithTheMouse()
    {
        // The point is covered before the Invoke and free afterwards: even then the dispatched Invoke
        // stands — a click on top of it could press the button a second time.
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", Covered = true };
        var reads = 0;
        string Signature()
        {
            if (++reads == 2)
                target.Covered = false;
            return "A";
        }

        var outcome = Executor(log).Click(target, ActionMethod.Auto, Signature, settleMs: 0);

        Assert.Equal(new ActionOutcome("Invoke", ActionEffect.Unchanged), outcome);
        Assert.Equal(["Invoke"], log);
    }

    [Fact]
    public void Click_Auto_InvokeType_NoSafePoint_PatternUnsupported_RefusesWithTheReasonTheMouseCannotBeUsed()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", InvokeResult = false };
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = false });

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, ActionMethod.Auto, signature: null, settleMs: 0));

        Assert.Equal(NoInteractiveDesktop, ex.Message);
        Assert.Equal(["Invoke"], log); // the (unsupported) pattern lookup, and no click
    }

    [Theory]
    [InlineData("CheckBox", "Toggle")]
    [InlineData("RadioButton", "Select")]
    [InlineData("TabItem", "Select")]
    [InlineData("ComboBox", "ExpandCollapse")]
    public void Click_Auto_OtherPatternTypes_StayPatternFirst_AlsoWithASafePoint(string controlType, string expectedCall)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = controlType };

        Executor(log).Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal([expectedCall], log);
    }

    [Fact]
    public void Click_RadioButton_UsesSelectionItem()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "RadioButton" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("SelectionItem", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Select"], log);
    }

    [Fact]
    public void Click_CheckBox_UsesToggle()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("Toggle", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Toggle"], log);
    }

    [Fact]
    public void Click_MenuItemWithChildren_UsesExpandCollapse()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "MenuItem", HasChildren = true };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("ExpandCollapse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["ExpandCollapse"], log);
    }

    [Fact]
    public void Click_MenuItemWithoutChildren_MethodPattern_UsesInvoke()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "MenuItem", HasChildren = false };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Pattern, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(["Invoke"], log);
    }

    [Fact]
    public void Click_ComboBox_UsesExpandCollapse()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "ComboBox" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("ExpandCollapse", outcome.Via);
        Assert.Equal(["ExpandCollapse"], log);
    }

    [Fact]
    public void Click_PatternUnchanged_FallsBackToMouseAtCurrentRectCentre()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "TabItem", CurrentRect = new Rectangle(100, 200, 40, 20) };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        // before, after-pattern (unchanged), after-mouse (changed).
        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Select", "LeftClick:120,210"], log);
    }

    [Fact]
    public void Click_ToggleUnchanged_DoesNotFallBack()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("Toggle", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Equal(["Toggle"], log);
    }

    [Fact]
    public void Click_ExpandCollapseUnchanged_DoesNotFallBack()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "ComboBox" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("ExpandCollapse", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Equal(["ExpandCollapse"], log);
    }

    [Fact]
    public void Click_PatternUnsupported_Auto_FallsBackToMouse()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox", ToggleResult = false, CurrentRect = new Rectangle(0, 0, 20, 10) };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Toggle", "LeftClick:10,5"], log);
    }

    [Fact]
    public void Click_NoPatternForControlType_FallsBackToMouse()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Edit", CurrentRect = new Rectangle(0, 0, 10, 10) };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["LeftClick:5,5"], log);
    }

    [Fact]
    public void Click_Fallback_UsesRectAtClickTime()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log)
        {
            ControlType = "TabItem",
            CurrentRect = new Rectangle(0, 0, 10, 10),
            RectAfterPattern = new Rectangle(300, 400, 20, 20),
        };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A", "B"), settleMs: 0);

        Assert.Equal(["Select", "LeftClick:310,410"], log);
    }

    [Fact]
    public void Click_MethodPattern_NoFallback()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Pattern, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("Invoke", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Equal(["Invoke"], log);
    }

    [Fact]
    public void Click_MethodPattern_NoUsablePattern_Throws()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Edit" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, ActionMethod.Pattern, signature: null, settleMs: 0));

        Assert.Contains("Edit", ex.Message, StringComparison.Ordinal);
        Assert.Empty(log);
    }

    [Fact]
    public void Click_MethodPattern_PatternUnsupported_Throws()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", InvokeResult = false };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, ActionMethod.Pattern, signature: null, settleMs: 0));
        Assert.Equal(["Invoke"], log);
    }

    [Fact]
    public void Click_MethodMouse_SkipsPattern()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", CurrentRect = new Rectangle(0, 0, 10, 10) };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Mouse, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["LeftClick:5,5"], log);
    }

    [Fact]
    public void Click_Mouse_Unchanged()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Mouse, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
    }

    [Fact]
    public void Click_Mouse_NoSignature_NotVerified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Mouse, signature: null, settleMs: 0);

        Assert.Equal("mouse", outcome.Via);
        Assert.Equal(ActionEffect.NotVerified, outcome.Effect);
        Assert.Equal(["LeftClick:5,5"], log);
    }

    [Fact]
    public void Click_NoSignature_NotVerified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "CheckBox" };
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Click(target, ActionMethod.Auto, signature: null, settleMs: 0);

        Assert.Equal("Toggle", outcome.Via);
        Assert.Equal(ActionEffect.NotVerified, outcome.Effect);
        Assert.Equal(["Toggle"], log);
    }

    // --- Type ------------------------------------------------------------------------------------

    [Fact]
    public void Type_ValuePattern_Verified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue("existing"); // previous, read before acting
        target.ReadValues.Enqueue("existingHello"); // read-back after acting
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hello", clear: false, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal("ValuePattern", outcome.Via);
        Assert.Equal(ActionEffect.ValueVerified, outcome.Effect);
        Assert.Equal("existingHello", target.LastSetValue);
        Assert.Equal(["SetValue"], log);
    }

    [Fact]
    public void Type_ReadBackMismatch_ReportsMismatch()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue(""); // previous
        target.ReadValues.Enqueue("unexpected"); // read-back after acting
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hello", clear: true, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal("ValuePattern", outcome.Via);
        Assert.Equal(ActionEffect.ValueMismatch, outcome.Effect);
        Assert.Equal("Hello", target.LastSetValue);
    }

    [Fact]
    public void Type_ReadBack_TrimmedEquality()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(" abc \r\n");
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "abc", clear: true, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(ActionEffect.ValueVerified, outcome.Effect);
    }

    [Fact]
    public void Type_SetValueUnsupported_UsesKeyboard()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, SetValueResult = false };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hello", clear: false, pressEnter: false, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(["SetValue", "Focus", "TypeText:Hello|False|False"], log);
    }

    [Fact]
    public void Type_NoValuePattern_UsesKeyboard()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false };
        target.ReadValues.Enqueue(null); // previous
        target.ReadValues.Enqueue(null); // no read-back available -> falls back to signature compare
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hello", clear: false, pressEnter: false, SignatureSequence("A", "B"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(ActionEffect.Changed, outcome.Effect);
        Assert.Equal(["Focus", "TypeText:Hello|False|False"], log);
    }

    [Fact]
    public void Type_NoValuePattern_FocusFails_ClicksBeforeTyping()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false, CurrentRect = new Rectangle(5, 5, 10, 10), FocusResult = false };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hi", clear: false, pressEnter: false, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(ActionEffect.Unchanged, outcome.Effect);
        Assert.Equal(["Focus", "LeftClick:10,10", "TypeText:Hi|False|False"], log);
    }

    [Fact]
    public void Type_Keyboard_WithReadBack_VerifiesValue()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false };
        target.ReadValues.Enqueue("x");
        target.ReadValues.Enqueue("xabc");
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "abc", clear: false, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(ActionEffect.ValueVerified, outcome.Effect);
    }

    [Fact]
    public void Type_Keyboard_WithReadBack_Mismatch()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false };
        target.ReadValues.Enqueue("x");
        target.ReadValues.Enqueue("xab");
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "abc", clear: false, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(ActionEffect.ValueMismatch, outcome.Effect);
    }

    [Fact]
    public void Type_NoSignature_NotVerified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var outcome = executor.Type(target, "Hi", clear: false, pressEnter: false, signature: null, settleMs: 0);

        Assert.Equal("ValuePattern", outcome.Via);
        Assert.Equal(ActionEffect.NotVerified, outcome.Effect);
        Assert.Equal("Hi", target.LastSetValue);
        Assert.Equal(["SetValue"], log);
    }

    [Fact]
    public void Type_ValuePattern_PressEnter_FocusesTargetFirst()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue("Hi");
        var executor = new ActionExecutor(new FakeInputDriver(log));

        executor.Type(target, "Hi", clear: false, pressEnter: true, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(["SetValue", "Focus", "PressEnter"], log);
    }

    [Fact]
    public void Type_ValuePattern_PressEnter_FocusFails_ClicksFirst()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, FocusResult = false, CurrentRect = new Rectangle(0, 0, 30, 10) };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue("Hi");
        var executor = new ActionExecutor(new FakeInputDriver(log));

        executor.Type(target, "Hi", clear: false, pressEnter: true, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(["SetValue", "Focus", "LeftClick:15,5", "PressEnter"], log);
    }

    // --- Type takes the before-signature only when the read-back may not decide (F11) ----------------

    [Fact]
    public void Type_ReadableValue_WithoutEnter_NeverWalksTheUiForASignature()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue("x");     // previous: the value is readable
        target.ReadValues.Enqueue("xabc");  // read-back decides

        var outcome = Executor(log).Type(target, "abc", clear: false, pressEnter: false, LoggedSignature(log), settleMs: 0);

        Assert.Equal(new ActionOutcome("ValuePattern", ActionEffect.ValueVerified), outcome);
        Assert.Equal(["SetValue"], log); // no "sig": one full UI walk saved per field
    }

    [Fact]
    public void Type_UnreadableValue_TakesTheSignatureBeforeAndAfter()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false };
        target.ReadValues.Enqueue(null);
        target.ReadValues.Enqueue(null);

        var outcome = Executor(log).Type(target, "abc", clear: false, pressEnter: false, LoggedSignature(log, "A", "B"), settleMs: 0);

        Assert.Equal(new ActionOutcome("keyboard", ActionEffect.Changed), outcome);
        Assert.Equal(["sig", "Focus", "TypeText:abc|False|False", "sig"], log);
    }

    [Fact]
    public void Type_WithEnter_KeepsTheBaseline_BecauseEnterMayTakeTheElementAway()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue("");    // readable before …
        target.ReadValues.Enqueue(null);  // … gone after Enter closed the dialog

        var outcome = Executor(log).Type(target, "abc", clear: true, pressEnter: true, LoggedSignature(log, "A", "B"), settleMs: 0);

        Assert.Equal(new ActionOutcome("ValuePattern", ActionEffect.Changed), outcome);
        Assert.Equal(["sig", "SetValue", "Focus", "PressEnter", "sig"], log);
    }

    [Fact]
    public void Type_ReadableBefore_UnreadableAfter_WithoutBaseline_IsNotVerified()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true };
        target.ReadValues.Enqueue("x");
        target.ReadValues.Enqueue(null);

        var outcome = Executor(log).Type(target, "abc", clear: false, pressEnter: false, LoggedSignature(log), settleMs: 0);

        Assert.Equal(new ActionOutcome("ValuePattern", ActionEffect.NotVerified), outcome);
        Assert.Equal(["SetValue"], log);
    }

    // --- Guards: disabled targets (F1) -------------------------------------------------------------

    [Theory]
    [InlineData(ActionMethod.Auto)]
    [InlineData(ActionMethod.Pattern)]
    [InlineData(ActionMethod.Mouse)]
    public void Click_DisabledTarget_Throws_BeforeAnything(ActionMethod method)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", IsEnabled = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Click(target, method, LoggedSignature(log, "A", "B"), settleMs: 0));

        Assert.Equal("Button is disabled — nothing was done", ex.Message);
        Assert.Empty(log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Type_DisabledTarget_Throws_BeforeAnything(bool canSetValue)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { IsEnabled = false, CanSetValue = canSetValue };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Type(target, "Hello", clear: true, pressEnter: true, LoggedSignature(log, "A", "B"), settleMs: 0));

        Assert.Equal("Edit is disabled — nothing was done", ex.Message);
        Assert.Empty(log);
    }

    // --- Guards: keyboard focus must be confirmed before a key is sent (F1) --------------------------

    [Fact]
    public void Type_Keyboard_FocusNotConfirmed_Throws_BeforeAnyKey()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false, KeyboardFocus = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Type(target, "Hello", clear: true, pressEnter: true, signature: null, settleMs: 0));

        Assert.Equal("could not give keyboard focus to the element — nothing was typed", ex.Message);
        // SetFocus, then the one click-to-focus attempt — and not a single key.
        Assert.Equal(["Focus", "LeftClick:5,5"], log);
    }

    [Fact]
    public void Type_Keyboard_FocusClaimedButNotArrived_ClicksToFocus_ThenTypes()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false, FocusResult = true, FocusArrivesWithClick = true };

        var outcome = Executor(log).Type(target, "Hi", clear: false, pressEnter: false, signature: null, settleMs: 0);

        Assert.Equal("keyboard", outcome.Via);
        Assert.Equal(["Focus", "LeftClick:5,5", "TypeText:Hi|False|False"], log);
    }

    [Fact]
    public void Type_Keyboard_FocusFails_ElementCovered_Throws_NothingClickedOrTyped()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false, FocusResult = false, Covered = true };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Type(target, "Hello", clear: false, pressEnter: false, signature: null, settleMs: 0));

        Assert.Equal("the element is covered by another window — nothing was clicked", ex.Message);
        Assert.Equal(["Focus"], log);
    }

    [Fact]
    public void Type_ValuePattern_PressEnter_FocusNotConfirmed_Throws_EnterNotSent()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, KeyboardFocus = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Type(target, "Hi", clear: true, pressEnter: true, signature: null, settleMs: 0));

        Assert.Equal("value was set, but the element could not be focused — Enter was not sent", ex.Message);
        Assert.Equal(["SetValue", "Focus", "LeftClick:5,5"], log);
    }

    [Theory]
    [InlineData(true, false)]   // another window covers the element
    [InlineData(false, true)]   // the element vanished after SetValue
    public void Type_ValuePattern_PressEnter_CannotClickToFocus_Throws_EnterNotSent(bool covered, bool rectGone)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, FocusResult = false, Covered = covered, RectGone = rectGone };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Type(target, "Hi", clear: true, pressEnter: true, signature: null, settleMs: 0));

        // The value WAS set, so this must not read like "nothing happened".
        Assert.Equal("value was set, but the element could not be focused — Enter was not sent", ex.Message);
        Assert.Equal(["SetValue", "Focus"], log);
    }

    [Fact]
    public void Type_ValuePattern_WithoutEnter_NeedsNoKeyboardFocus()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, KeyboardFocus = false };
        target.ReadValues.Enqueue("");
        target.ReadValues.Enqueue("Hi");

        var outcome = Executor(log).Type(target, "Hi", clear: true, pressEnter: false, SignatureSequence("A"), settleMs: 0);

        Assert.Equal(new ActionOutcome("ValuePattern", ActionEffect.ValueVerified), outcome);
        Assert.Equal(["SetValue"], log);
    }

    // --- Guards: the mouse never clicks a point another window owns (F8) -----------------------------

    [Fact]
    public void Click_MethodMouse_ElementCovered_Throws_NothingClicked()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", Covered = true };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Click(target, ActionMethod.Mouse, signature: null, settleMs: 0));

        Assert.Equal("the element is covered by another window — nothing was clicked", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void Click_Auto_NoPatternForControlType_ElementCovered_Throws_NothingClicked()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Edit", Covered = true };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Click(target, ActionMethod.Auto, signature: null, settleMs: 0));

        Assert.Equal("the element is covered by another window — nothing was clicked", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void Click_Auto_PatternUnsupported_ElementCovered_Throws_NothingClicked()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", InvokeResult = false, Covered = true };

        Assert.Throws<InvalidOperationException>(
            () => Executor(log).Click(target, ActionMethod.Auto, signature: null, settleMs: 0));

        Assert.Equal(["Invoke"], log); // the (unsupported) pattern lookup, and no click
    }

    // --- Guards: no mouse input without an interactive desktop (AF1) ----------------------------------

    [Theory]
    [InlineData("Button", ActionMethod.Mouse)]  // explicit mouse
    [InlineData("Edit", ActionMethod.Auto)]     // a control type with no click pattern
    public void Click_MousePath_NoInteractiveDesktop_Throws_BeforeAnythingIsSent(string controlType, ActionMethod method)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = controlType };
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = false });

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Click(target, method, signature: null, settleMs: 0));

        Assert.Equal(NoInteractiveDesktop, ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void Click_SelectUnchanged_NoInteractiveDesktop_ReportsUnchanged_NoClick()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "TabItem" };
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = false });

        var outcome = executor.Click(target, ActionMethod.Auto, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal(new ActionOutcome("SelectionItem", ActionEffect.Unchanged), outcome);
        Assert.Equal(["Select"], log);
    }

    [Fact]
    public void Type_Keyboard_FocusFails_NoInteractiveDesktop_Throws_NothingClickedOrTyped()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = false, FocusResult = false };
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = false }) { FocusWaitMs = 0 };

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Type(target, "Hello", clear: false, pressEnter: false, signature: null, settleMs: 0));

        Assert.Equal(NoInteractiveDesktop, ex.Message);
        Assert.Equal(["Focus"], log);
    }

    [Fact]
    public void Type_ValuePattern_PressEnter_NoInteractiveDesktop_Throws_EnterNotSent()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { CanSetValue = true, FocusResult = false };
        var executor = new ActionExecutor(new FakeInputDriver(log) { HasInteractiveDesktop = false }) { FocusWaitMs = 0 };

        var ex = Assert.Throws<InvalidOperationException>(
            () => executor.Type(target, "Hi", clear: true, pressEnter: true, signature: null, settleMs: 0));

        Assert.Equal("value was set, but the element could not be focused — Enter was not sent", ex.Message);
        Assert.Equal(["SetValue", "Focus"], log);
    }

    [Fact]
    public void Click_Mouse_EmptyRect_Throws_NothingClicked()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", CurrentRect = Rectangle.Empty };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Executor(log).Click(target, ActionMethod.Mouse, signature: null, settleMs: 0));

        Assert.Equal("the element has no visible area — nothing was clicked", ex.Message);
        Assert.Empty(log);
    }

    [Fact]
    public void Click_Mouse_ElementGone_ThrowsElementNotFound_NothingClicked()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "Button", RectGone = true };

        Assert.Throws<ElementNotFoundException>(
            () => Executor(log).Click(target, ActionMethod.Mouse, signature: null, settleMs: 0));

        Assert.Empty(log);
    }

    // --- An executed pattern action never becomes an error or a second action (F3, F8, F10) ---------

    [Theory]
    [InlineData(true, false)]   // covered by another window by the time of the fallback
    [InlineData(false, true)]   // element vanished after Select
    public void Click_SelectUnchanged_FallbackNotSafelyPossible_ReportsUnchanged_NoClick(bool covered, bool gone)
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "TabItem", CoveredAfterPattern = covered, RectGoneAfterPattern = gone };

        var outcome = Executor(log).Click(target, ActionMethod.Auto, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal(new ActionOutcome("SelectionItem", ActionEffect.Unchanged), outcome);
        Assert.Equal(["Select"], log);
    }

    [Fact]
    public void Click_MethodPattern_SelectUnchanged_NeverUsesMouse()
    {
        var log = new List<string>();
        var target = new FakeActionTarget(log) { ControlType = "TabItem" };

        var outcome = Executor(log).Click(target, ActionMethod.Pattern, SignatureSequence("A", "A"), settleMs: 0);

        Assert.Equal(new ActionOutcome("SelectionItem", ActionEffect.Unchanged), outcome);
        Assert.Equal(["Select"], log);
    }

    // --- MouseAction -----------------------------------------------------------------------------

    [Fact]
    public void MouseAction_RunsActionAndReportsChanged()
    {
        var log = new List<string>();
        var executor = new ActionExecutor(new FakeInputDriver(log));

        var sigs = new Queue<string>(["A", "B"]);
        var outcome = executor.MouseAction(() => log.Add("act"), () => { log.Add("sig"); return sigs.Dequeue(); }, settleMs: 0);

        Assert.Equal(new ActionOutcome("mouse", ActionEffect.Changed), outcome);
        Assert.Equal(["sig", "act", "sig"], log);
    }

    [Fact]
    public void MouseAction_SameSignature_Unchanged_NullSignature_NotVerified()
    {
        var executor = new ActionExecutor(new FakeInputDriver([]));

        Assert.Equal(ActionEffect.Unchanged, executor.MouseAction(() => { }, SignatureSequence("A", "A"), 0).Effect);
        Assert.Equal(ActionEffect.NotVerified, executor.MouseAction(() => { }, null, 0).Effect);
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

    /// <summary>Signature source that logs every read, so "nothing happened" includes "no UI walk".</summary>
    private static Func<string> LoggedSignature(List<string> log, params string[] values)
    {
        var queue = new Queue<string>(values);
        return () => { log.Add("sig"); return queue.Dequeue(); };
    }

    /// <summary>Executor for the guard tests: fake input driver (no real input) and no waiting for a
    /// focus that the fake will never report.</summary>
    private static ActionExecutor Executor(List<string> log) => new(new FakeInputDriver(log)) { FocusWaitMs = 0 };
}
