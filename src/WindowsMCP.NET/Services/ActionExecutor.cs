using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>How a verified action's effect compares the pre/post signature of the element's process (or
/// the read-back value for <see cref="ActionExecutor.Type"/>). Wire values are produced by <see cref="ActionEffectExtensions.ToWire"/>.</summary>
public enum ActionEffect { Changed, Unchanged, ValueVerified, ValueMismatch, NotVerified }

/// <summary>Serialises an <see cref="ActionEffect"/> the same way <see cref="SnakeCaseEnumConverter{TEnum}"/>
/// would (lower snake_case), for embedding directly in a tool's result text rather than JSON.</summary>
public static class ActionEffectExtensions
{
    public static string ToWire(this ActionEffect effect) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(effect.ToString());
}

/// <summary>Result of a verified action: which mechanism actually performed it (a UIA pattern name,
/// <c>"mouse"</c> or <c>"keyboard"</c>) and the observed <see cref="ActionEffect"/>.</summary>
public sealed record ActionOutcome(string Via, ActionEffect Effect)
{
    /// <summary>The UIA pattern call that performed the action was dispatched but had not returned
    /// when its time limit ran out (<see cref="PatternCallResult.StillRunning"/>). It is neither
    /// repeated nor followed by a mouse click; the result text says so (<see cref="EffectText"/>).</summary>
    public bool CallPending { get; init; }

    /// <summary>The effect as a tool result states it: the wire value, followed — for a pattern call
    /// that has not returned — by what that means for the caller.</summary>
    public string EffectText() => CallPending
        ? $"{Effect.ToWire()} — the call has not returned after {PatternCall.DefaultLimit.TotalSeconds:0} s: " +
          "the application may be showing a modal dialog and cannot be observed until it is closed " +
          "(Screenshot and keyboard still work)"
        : Effect.ToWire();
}

/// <summary>
/// Seam over a live UI element that <see cref="ActionExecutor"/> acts on. <see cref="FlaUiActionTarget"/>
/// is the production implementation; tests use a hand-written fake.
/// <para>
/// Contract for implementations: nothing that can be read AFTER an action was sent may throw — a
/// vanished element must not turn an executed action into an error. <see cref="IsEnabled"/> (only ever
/// read before acting) and <see cref="CurrentRect"/> throw <see cref="ElementNotFoundException"/> when
/// the element is gone; the executor guards every <see cref="CurrentRect"/> read that follows an action.
/// </para>
/// </summary>
public interface IActionTarget
{
    string ControlType { get; }

    /// <summary>False only when the element reports itself disabled (an element that cannot report it
    /// counts as enabled).</summary>
    bool IsEnabled { get; }

    bool HasChildren { get; }
    Rectangle CurrentRect { get; }

    /// <summary>True when the element itself or one of its descendants holds the keyboard focus (a
    /// ComboBox's inner Edit counts); false when that cannot be determined.</summary>
    bool HasKeyboardFocus { get; }

    /// <summary>True when the top-level window at screen point <paramref name="p"/> is the window this
    /// element was resolved in — i.e. a click there reaches that window and nothing lying on top of it:
    /// no window of another application, and no popup, menu or dialog of the same one. False when that
    /// cannot be determined.</summary>
    bool OwnsPoint(Point p);

    /// <summary>Pattern calls: <see cref="PatternCallResult.NotSupported"/> ONLY when the element does
    /// not support the pattern (nothing was sent). Anything else means the call was dispatched — even
    /// when it threw or did not return in time, because the action may have run; the caller verifies.</summary>
    PatternCallResult TryInvoke();

    /// <inheritdoc cref="TryInvoke"/>
    PatternCallResult TryExpandCollapse();

    /// <inheritdoc cref="TryInvoke"/>
    PatternCallResult TryToggle();

    /// <inheritdoc cref="TryInvoke"/>
    PatternCallResult TrySelect();

    bool CanSetValue { get; }

    /// <summary><see cref="PatternCallResult.NotSupported"/> only when the value cannot be written (no
    /// ValuePattern, or read-only); otherwise the write was dispatched and the read-back decides.</summary>
    PatternCallResult TrySetValue(string value);

    string? ReadValue();
    bool TryFocus();
}

/// <summary>Seam over the mouse/keyboard <c>SendInput</c> paths <see cref="ActionExecutor"/> uses.
/// <see cref="InputDriver"/> is the production implementation (built on <c>InputFactory</c>,
/// exactly as <c>InputTools.Click</c>/<c>Type</c> do today); tests use a hand-written fake.</summary>
public interface IInputDriver
{
    /// <summary>False when this process cannot inject input at all: the session is disconnected or
    /// not rendered. Only UIA patterns work there; every mouse path refuses.</summary>
    bool HasInteractiveDesktop { get; }

    void LeftClick(Point p);
    void TypeText(string text, bool clear, bool pressEnter);
    void PressEnter();
}

/// <summary>
/// The checks every element action passes before real input is sent — shared by <see cref="ActionExecutor"/>
/// and, through <see cref="ActionExecutor.ClickPoint"/>, the element paths of the tools (non-left/multi
/// clicks, MultiSelect) so there is exactly one definition of "where to click" and "may we click
/// there". The rule behind them: input on the element path never reaches anything but the resolved
/// target; when in doubt the action is refused and nothing is done.
/// </summary>
internal static class ActionGuards
{
    /// <summary>Refuses a disabled target: a pattern call would be ignored or act on stale state, and
    /// mouse/keyboard input on a disabled control is delivered to whatever is behind it.</summary>
    public static void EnsureEnabled(IActionTarget t)
    {
        if (!t.IsEnabled)
            throw new InvalidOperationException($"{t.ControlType} is disabled — nothing was done");
    }

    /// <summary>
    /// The screen point a mouse click on <paramref name="t"/> goes to: the centre of its CURRENT
    /// rectangle — verified to lie in the window the element was resolved in, so neither a window of
    /// another application nor a popup, menu or dialog of the same one lying on top is ever clicked.
    /// Throws (and nothing is clicked) when there is no desktop to click on, the element is gone
    /// (<see cref="ElementNotFoundException"/> from the target), has no area, or is covered.
    /// </summary>
    public static Point ClickPoint(IActionTarget t, IInputDriver input)
    {
        if (!input.HasInteractiveDesktop)
            throw new InvalidOperationException(
                "no interactive desktop (session disconnected or not rendered) — nothing was clicked");

        var rect = t.CurrentRect;
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("the element has no visible area — nothing was clicked");

        var centre = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        if (!t.OwnsPoint(centre))
            throw new InvalidOperationException("the element is covered by another window — nothing was clicked");

        return centre;
    }
}

/// <summary>
/// Verified action execution for element-id based Click/Type: acts through the UIA pattern matching
/// the element's control type or through the mouse/keyboard <see cref="IInputDriver"/> path (see
/// <see cref="Click"/> and <see cref="Type"/> for which comes first), and reports which mechanism acted
/// plus the verified <see cref="ActionEffect"/> by comparing a caller-supplied signature (or, for
/// <see cref="Type"/>, the element's own read-back value when available) before and after.
/// <para>
/// Two rules hold on every path: input never reaches anything but the resolved target (disabled
/// targets, covered click points and unconfirmed keyboard focus are refused before anything is sent),
/// and no action is performed twice (a dispatched pattern call is never repeated with the mouse, except
/// the idempotent SelectionItem select that visibly changed nothing; a mouse click is never followed
/// by a pattern call).
/// </para>
/// </summary>
public sealed class ActionExecutor(IInputDriver input)
{
    private const int FocusPollMs = 25;
    private const string Invoke = "Invoke";
    private const string SelectionItem = "SelectionItem";

    /// <summary>How long to wait for the keyboard focus to arrive on the target after SetFocus or the
    /// click-to-focus (both are asynchronous for the target application). Tests set 0.</summary>
    internal int FocusWaitMs { get; init; } = 500;

    /// <summary>
    /// Clicks <paramref name="t"/>. <paramref name="method"/> selects the strategy:
    /// <list type="bullet">
    /// <item><c>Auto</c>, control types clicked through <c>Invoke</c> (Button, SplitButton, Hyperlink,
    /// MenuItem without children): the MOUSE first when it can click safely — an <c>Invoke</c> whose
    /// handler opens a modal dialog does not return before the dialog is closed, and until then the
    /// application answers no UI Automation request (it cannot be observed, its dialog cannot be
    /// operated by id). <c>Invoke</c> only when there is no safe click point (no interactive desktop,
    /// no visible area, covered).</item>
    /// <item><c>Auto</c>, other control types: the pattern for the control type first; a mouse click at
    /// the element's current centre when the element does not support that pattern, or when a
    /// SelectionItem select had no observable effect.</item>
    /// <item><c>Pattern</c> never uses the mouse (throws when no pattern applies); <c>Mouse</c> skips
    /// the pattern entirely.</item>
    /// </list>
    /// A dispatched pattern call is never repeated with the mouse (apart from that Select) — not even
    /// when it threw or timed out, because it may have run — and a mouse click is never followed by a
    /// pattern call. <paramref name="signature"/> is the signature function used for before/after
    /// comparison; a <see langword="null"/> signature disables verification
    /// (<see cref="ActionEffect.NotVerified"/>) without changing the action taken. Throws before
    /// anything is done when the target is disabled, and before any mouse input when the click point is
    /// not safe (see <see cref="ActionGuards.ClickPoint"/>).
    /// </summary>
    public ActionOutcome Click(IActionTarget t, ActionMethod method, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(t);
        ActionGuards.EnsureEnabled(t);

        var before = signature?.Invoke();

        if (method != ActionMethod.Mouse)
        {
            var pattern = ClickPattern(t);

            if (method == ActionMethod.Auto && pattern == Invoke && TryClickPoint(t) is { } safePoint)
            {
                input.LeftClick(safePoint);
                Settle(settleMs);
                return new ActionOutcome("mouse", CompareEffect(signature, before));
            }

            if (pattern is not null && CallPattern(t, pattern) is var call && call != PatternCallResult.NotSupported)
            {
                // A call that has not returned is still running in the application: it is reported,
                // never repeated and never followed by a click.
                var pending = call == PatternCallResult.StillRunning;
                Settle(settleMs);

                if (signature is null)
                    return new ActionOutcome(pattern, ActionEffect.NotVerified) { CallPending = pending };

                if (signature() != before)
                    return new ActionOutcome(pattern, ActionEffect.Changed) { CallPending = pending };

                // Unchanged: only a SelectionItem select that RETURNED (idempotent) may be retried with
                // the mouse. A second Invoke/Toggle/ExpandCollapse could repeat the action or undo it.
                if (method == ActionMethod.Pattern || pattern != SelectionItem || pending)
                    return new ActionOutcome(pattern, ActionEffect.Unchanged) { CallPending = pending };

                // The Select has been sent, so from here on nothing may turn into an error: when the
                // element can no longer be clicked safely (gone, covered, no desktop), the Select stands.
                if (TryClickPoint(t) is not { } retryPoint)
                    return new ActionOutcome(pattern, ActionEffect.Unchanged);

                input.LeftClick(retryPoint);
                Settle(settleMs);
                return new ActionOutcome("mouse", CompareEffect(signature, before));
            }

            if (method == ActionMethod.Pattern)
                throw new InvalidOperationException($"no usable UIA pattern for {t.ControlType}");
        }

        input.LeftClick(ClickPoint(t));
        Settle(settleMs);
        return new ActionOutcome("mouse", CompareEffect(signature, before));
    }

    /// <summary><see cref="ActionGuards.ClickPoint"/> with this executor's input driver — for the
    /// element paths of the tools that send their own mouse input (non-left/multi clicks, MultiSelect).</summary>
    internal Point ClickPoint(IActionTarget t) => ActionGuards.ClickPoint(t, input);

    /// <summary>
    /// Types into <paramref name="t"/>. Prefers <c>ValuePattern.SetValue</c> when the target supports
    /// writing a value; otherwise gives the element the keyboard focus (SetFocus, one mouse click when
    /// that does not bring the focus) and sends the keyboard path. Keys — the text, and Enter after a
    /// SetValue — are only ever sent once the target (or one of its descendants) is confirmed to hold
    /// the keyboard focus; otherwise this throws and no key is sent. Verification prefers reading the
    /// value back (trimmed equality against the expected text) and falls back to the
    /// <paramref name="signature"/> comparison only when no read-back is available — the baseline for
    /// that comparison is taken only when the value is not readable up front or Enter follows; a
    /// <see langword="null"/> signature disables verification.
    /// </summary>
    public ActionOutcome Type(IActionTarget t, string text, bool clear, bool pressEnter, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(text);
        ActionGuards.EnsureEnabled(t);

        var previous = t.ReadValue();
        var expected = clear ? text : (previous ?? "") + text;

        // The baseline signature costs a full UI walk and is only the fallback for a read-back that
        // cannot decide. A readable value will decide — unless Enter follows, which may take the
        // element away (submit, close the dialog). So it is taken only in those two cases.
        var needsBaseline = signature is not null && (previous is null || pressEnter);
        var before = needsBaseline ? signature!() : null;

        string via;
        var pending = false;
        if (t.CanSetValue && t.TrySetValue(expected) is var call && call != PatternCallResult.NotSupported)
        {
            via = "ValuePattern";
            pending = call == PatternCallResult.StillRunning;
            if (pressEnter)
            {
                // SetValue does not move the focus. The value is already set, so a click point that is
                // gone or covered is not an error of its own here — it just means "no focus".
                if (!GiveFocus(t, TryClickPoint))
                    throw new InvalidOperationException("value was set, but the element could not be focused — Enter was not sent");

                input.PressEnter();
            }
        }
        else
        {
            // Nothing has been sent yet: a covered or vanished element refuses with its own error.
            if (!GiveFocus(t, target => ClickPoint(target)))
                throw new InvalidOperationException("could not give keyboard focus to the element — nothing was typed");

            input.TypeText(text, clear, pressEnter);
            via = "keyboard";
        }

        Settle(settleMs);

        if (signature is null)
            return new ActionOutcome(via, ActionEffect.NotVerified) { CallPending = pending };

        var readBack = t.ReadValue();
        if (readBack is not null)
        {
            var effect = string.Equals(readBack.Trim(), expected.Trim(), StringComparison.Ordinal)
                ? ActionEffect.ValueVerified
                : ActionEffect.ValueMismatch;
            return new ActionOutcome(via, effect) { CallPending = pending };
        }

        // The value was readable before and is not any more, and no baseline was taken: nothing to
        // compare against.
        return new ActionOutcome(via, needsBaseline ? CompareEffect(signature, before) : ActionEffect.NotVerified)
        {
            CallPending = pending,
        };
    }

    /// <summary>
    /// Performs a caller-supplied mouse action (e.g. a right/double click) between the before/after
    /// signature reads, with the same settle and compare logic as <see cref="Click"/>, and reports
    /// it as <c>mouse</c>. A <see langword="null"/> signature yields <see cref="ActionEffect.NotVerified"/>.
    /// The caller's action is responsible for <see cref="ActionGuards.ClickPoint"/>.
    /// </summary>
    public ActionOutcome MouseAction(Action act, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = signature?.Invoke();
        act();
        Settle(settleMs);
        return new ActionOutcome("mouse", CompareEffect(signature, before));
    }

    /// <summary>Click pattern table (spec §3): the UIA pattern that clicks a control type — also the
    /// <see cref="ActionOutcome.Via"/> name it reports — or null for control types with no click pattern.</summary>
    private static string? ClickPattern(IActionTarget t) => t.ControlType switch
    {
        "Button" or "SplitButton" or "Hyperlink" => Invoke,
        "MenuItem" => t.HasChildren ? "ExpandCollapse" : Invoke,
        "ComboBox" => "ExpandCollapse",
        "CheckBox" => "Toggle",
        "RadioButton" or "TabItem" or "ListItem" or "TreeItem" or "DataItem" => SelectionItem,
        _ => null,
    };

    /// <summary>Calls a pattern from <see cref="ClickPattern"/>.</summary>
    private static PatternCallResult CallPattern(IActionTarget t, string pattern) => pattern switch
    {
        Invoke => t.TryInvoke(),
        "ExpandCollapse" => t.TryExpandCollapse(),
        "Toggle" => t.TryToggle(),
        _ => t.TrySelect(),
    };

    /// <summary>
    /// Brings the keyboard focus to <paramref name="t"/>: SetFocus first; when that fails or the focus
    /// does not arrive, one click at <paramref name="clickPoint"/> (no click when it yields null).
    /// True only once the target is confirmed to hold the keyboard focus — the precondition for any key.
    /// </summary>
    private bool GiveFocus(IActionTarget t, Func<IActionTarget, Point?> clickPoint)
    {
        if (t.TryFocus() && WaitForFocus(t))
            return true;

        if (clickPoint(t) is not { } point)
            return false;

        input.LeftClick(point);
        return WaitForFocus(t);
    }

    private bool WaitForFocus(IActionTarget t)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (t.HasKeyboardFocus)
                return true;
            if (stopwatch.ElapsedMilliseconds >= FocusWaitMs)
                return false;

            Thread.Sleep(FocusPollMs);
        }
    }

    /// <summary><see cref="ActionGuards.ClickPoint"/> where "no" is an answer, not an error: for the
    /// choice between mouse and pattern, and for the moments AFTER an action was sent. Null when there
    /// is no safe click point (no interactive desktop, element gone, no area, covered).</summary>
    private Point? TryClickPoint(IActionTarget t)
    {
        try
        {
            return ClickPoint(t);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotFoundException)
        {
            return null;
        }
    }

    private static ActionEffect CompareEffect(Func<string>? signature, string? before)
    {
        if (signature is null)
            return ActionEffect.NotVerified;

        return signature() == before ? ActionEffect.Unchanged : ActionEffect.Changed;
    }

    private static void Settle(int settleMs)
    {
        if (settleMs > 0)
            Thread.Sleep(settleMs);
    }
}
