using System.Drawing;
using System.Text.Json;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>How a verified action's effect compares the pre/post scope signature (or the read-back
/// value for <see cref="ActionExecutor.Type"/>). Wire values are produced by <see cref="ToWire"/>.</summary>
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
public sealed record ActionOutcome(string Via, ActionEffect Effect);

/// <summary>
/// Seam over a live UI element that <see cref="ActionExecutor"/> acts on: pattern invocations
/// (<c>Try*</c>, each swallowing unsupported-pattern/COM failures into <see langword="false"/>),
/// value read/write, focus and the element's current geometry. <see cref="FlaUiActionTarget"/> is the
/// production implementation; tests use a hand-written fake.
/// </summary>
public interface IActionTarget
{
    string ControlType { get; }
    bool HasChildren { get; }
    Rectangle CurrentRect { get; }
    bool TryInvoke();
    bool TryExpandCollapse();
    bool TryToggle();
    bool TrySelect();
    bool CanSetValue { get; }
    bool TrySetValue(string value);
    string? ReadValue();
    bool TryFocus();
}

/// <summary>Seam over the mouse/keyboard <c>SendInput</c> paths <see cref="ActionExecutor"/> falls back
/// to. <see cref="InputDriver"/> is the production implementation (built on <c>InputFactory</c>,
/// exactly as <c>InputTools.Click</c>/<c>Type</c> do today); tests use a hand-written fake.</summary>
public interface IInputDriver
{
    void LeftClick(Point p);
    void TypeText(string text, bool clear, bool pressEnter);
    void PressEnter();
}

/// <summary>
/// Pattern-first action execution for element-id based Click/Type (Task 7): tries the UIA pattern
/// matching the element's control type, falls back to the mouse/keyboard <see cref="IInputDriver"/>
/// path when no pattern applies or the pattern had no observable effect, and reports which mechanism
/// acted plus the verified <see cref="ActionEffect"/> by comparing a caller-supplied scope signature
/// (or, for <see cref="Type"/>, the element's own read-back value when available) before and after.
/// </summary>
public sealed class ActionExecutor(IInputDriver input)
{
    /// <summary>
    /// Clicks <paramref name="t"/>. <paramref name="method"/> selects the strategy: <c>Auto</c> tries
    /// the pattern for the element's control type first and falls back to a mouse click at the
    /// element's current centre when the pattern is unsupported, fails, or has no observable effect;
    /// <c>Pattern</c> never falls back (throws when no pattern applies or it fails); <c>Mouse</c> skips
    /// the pattern entirely. <paramref name="signature"/> is the scope signature function used for
    /// before/after comparison; a <see langword="null"/> signature disables verification
    /// (<see cref="ActionEffect.NotVerified"/>) without changing the action taken.
    /// </summary>
    public ActionOutcome Click(IActionTarget t, ActionMethod method, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(t);

        var before = signature?.Invoke();

        if (method == ActionMethod.Pattern)
        {
            var (success, via) = TryPattern(t);
            if (!success)
                throw new InvalidOperationException($"no usable UIA pattern for {t.ControlType}");

            Settle(settleMs);
            return new ActionOutcome(via, CompareEffect(signature, before));
        }

        if (method == ActionMethod.Auto)
        {
            var (success, via) = TryPattern(t);
            if (success)
            {
                Settle(settleMs);

                if (signature is null)
                    return new ActionOutcome(via, ActionEffect.NotVerified);

                if (signature() != before)
                    return new ActionOutcome(via, ActionEffect.Changed);

                // Unchanged: only SelectionItem (idempotent Select) may retry with the mouse. A second
                // Invoke/Toggle/ExpandCollapse could repeat the action or undo it.
                if (via != "SelectionItem")
                    return new ActionOutcome(via, ActionEffect.Unchanged);
            }
        }

        input.LeftClick(Centre(t.CurrentRect));
        Settle(settleMs);
        return new ActionOutcome("mouse", CompareEffect(signature, before));
    }

    /// <summary>
    /// Types into <paramref name="t"/>. Prefers <c>ValuePattern.SetValue</c> when the target supports
    /// writing a value; otherwise focuses the element (mouse click on focus failure) and sends the
    /// keyboard path. Verification prefers reading the value back (trimmed equality against the
    /// expected text) and falls back to the scope <paramref name="signature"/> comparison only when no
    /// read-back is available; a <see langword="null"/> signature disables verification.
    /// </summary>
    public ActionOutcome Type(IActionTarget t, string text, bool clear, bool pressEnter, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(text);

        var previous = t.ReadValue();
        var before = signature?.Invoke();
        var expected = clear ? text : (previous ?? "") + text;

        string via;
        if (t.CanSetValue && t.TrySetValue(expected))
        {
            if (pressEnter)
            {
                // SetValue does not move focus; make sure Enter reaches the target.
                if (!t.TryFocus())
                    input.LeftClick(Centre(t.CurrentRect));
                input.PressEnter();
            }
            via = "ValuePattern";
        }
        else
        {
            if (!t.TryFocus())
                input.LeftClick(Centre(t.CurrentRect));
            input.TypeText(text, clear, pressEnter);
            via = "keyboard";
        }

        Settle(settleMs);

        if (signature is null)
            return new ActionOutcome(via, ActionEffect.NotVerified);

        var readBack = t.ReadValue();
        if (readBack is not null)
        {
            var effect = string.Equals(readBack.Trim(), expected.Trim(), StringComparison.Ordinal)
                ? ActionEffect.ValueVerified
                : ActionEffect.ValueMismatch;
            return new ActionOutcome(via, effect);
        }

        return new ActionOutcome(via, CompareEffect(signature, before));
    }

    /// <summary>
    /// Performs a caller-supplied mouse action (e.g. a right/double click) between the before/after
    /// scope signature reads, with the same settle and compare logic as <see cref="Click"/>, and reports
    /// it as <c>mouse</c>. A <see langword="null"/> signature yields <see cref="ActionEffect.NotVerified"/>.
    /// </summary>
    public ActionOutcome MouseAction(Action act, Func<string>? signature, int settleMs)
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = signature?.Invoke();
        act();
        Settle(settleMs);
        return new ActionOutcome("mouse", CompareEffect(signature, before));
    }

    /// <summary>Click pattern table (spec §3): the UIA pattern tried first for a control type, and the
    /// <see cref="ActionOutcome.Via"/> name it reports. Returns <c>(false, "")</c>, without invoking any
    /// pattern, for control types with no click pattern.</summary>
    private static (bool Success, string Via) TryPattern(IActionTarget t) => t.ControlType switch
    {
        "Button" or "SplitButton" or "Hyperlink" => (t.TryInvoke(), "Invoke"),
        "MenuItem" => t.HasChildren ? (t.TryExpandCollapse(), "ExpandCollapse") : (t.TryInvoke(), "Invoke"),
        "ComboBox" => (t.TryExpandCollapse(), "ExpandCollapse"),
        "CheckBox" => (t.TryToggle(), "Toggle"),
        "RadioButton" or "TabItem" or "ListItem" or "TreeItem" or "DataItem" => (t.TrySelect(), "SelectionItem"),
        _ => (false, ""),
    };

    private static ActionEffect CompareEffect(Func<string>? signature, string? before)
    {
        if (signature is null)
            return ActionEffect.NotVerified;

        return signature() == before ? ActionEffect.Unchanged : ActionEffect.Changed;
    }

    private static Point Centre(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static void Settle(int settleMs)
    {
        if (settleMs > 0)
            Thread.Sleep(settleMs);
    }
}
