using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ExpectTools
{
    public const int MaxTimeoutMs = 30_000;

    /// <summary>Pause between two observations while waiting.</summary>
    public const int PollPauseMs = 200;

    /// <summary>Expect observes with Observe's maximum, so that "not found" is proven as often as possible.</summary>
    public const int ObserveMaxElements = 500;

    [McpServerTool(Name = "Expect", ReadOnly = true, Idempotent = true)]
    [Description("Check conditions on the current UI state with fixed rules (no model) — for 'is the dialog open', 'is the button enabled', " +
                 "'does the field show X', and for waiting until that is the case. Observes like Observe (scope/process) and sends no input. " +
                 "It only sees the windows in scope: with scope=foreground those of the application in front (the result names it), " +
                 "with scope=process those of that process — use scope=process to wait for an application to start or to close. " +
                 "conditions: 1–20 objects, all must hold; each is ONE of: " +
                 "window — {\"window\":\"Login\",\"state\":\"open\"} (state: open (default) | closed; optional \"modal\":true|false with open); " +
                 "element — by id {\"element\":\"<id from Observe>\",\"state\":\"enabled\"} or by selector {\"type\":\"Button\",\"name\":\"OK\",\"state\":\"enabled\"} " +
                 "(selector: type and/or name and/or panel — name also matches the element's label; panel alone asks for anything in that panel —, optional in_window; " +
                 "type is one of Button, SplitButton, CheckBox, RadioButton, ComboBox, Edit, Document, Spinner, Slider, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, DataItem, HeaderItem " +
                 "— the types Observe lists; elements without a name and value are not observed; " +
                 "state: exists (default) | absent | enabled | disabled | selected | not_selected | checked | unchecked | expanded | collapsed " +
                 "(check boxes: checked/unchecked; radio buttons, list and tree items: selected/not_selected); " +
                 "optional \"value\":\"...\" (equals, case-sensitive) or \"value_contains\":\"...\" (case-sensitive), combinable with every state except absent); " +
                 "text — {\"text\":\"finished\"} (a static text — label, group or header caption, not a button caption or field value — contains it, ignoring case). " +
                 "Names, panels and window titles compare case-insensitively; \"match\":\"exact\" (default) | \"contains\" per condition. " +
                 "Each condition is answered pass, fail or unknown. unknown means the observation proves neither: it ran out of time, the element list is cut, " +
                 "a window is unreadable, several elements fit the selector or a second match cannot be ruled out (narrow it with panel/in_window or use an id), " +
                 "the control does not report the state (docking TabItems never report 'selected' — check an element inside the panel instead), " +
                 "it is a password field, the id is unknown or belongs to an application outside the scope, or another application took the foreground during the wait. " +
                 "Overall: pass when all pass, fail when one fails, otherwise unknown. fail and unknown are results, not errors. " +
                 "timeout_ms > 0 repeats the check (200 ms after each observation) until all conditions pass or the time is up. " +
                 "Matched element ids are listed and can be passed to Click/Type. " +
                 "format=json shape: {result:\"pass\"|\"fail\"|\"unknown\", passed:int, total:int, observed:[{process, pid}], observations:int, elapsed_ms:int, timed_out:bool, " +
                 "conditions:[{index:int, result, condition:string, actual:string, elements?:[string]}]}.")]
    public static async Task<CallToolResult> Expect(
        ObservationService observation,
        ObservationStore store,
        [Description("Array of condition objects, e.g. [{\"window\":\"Login\"},{\"type\":\"Button\",\"name\":\"OK\",\"state\":\"enabled\"}]")] JsonElement conditions,
        [Description("0 = check once; otherwise wait up to this many milliseconds for all conditions to pass (clamped 0-30000)")] int timeout_ms = 0,
        [Description("Which windows to observe")] ObserveScope scope = ObserveScope.Foreground,
        [Description("Process name, required when scope=process (e.g. 'notepad')")] string? process = null,
        [Description("Output format")] OutputFormat format = OutputFormat.Markdown,
        CancellationToken ct = default)
    {
        try
        {
            var expectations = ExpectationParser.Parse(conditions);
            var guard = new ScopeGuard(scope);

            var outcome = await RunAsync(
                expectations,
                () =>
                {
                    // "Nothing there (yet / any more)" is a state Expect is asked about, not a failure.
                    var availability = ObservationService.CheckScope(scope, process);
                    return availability == ObservationService.ScopeAvailability.Available
                        ? guard.Check(observation.Observe(scope, process, ObserveMaxElements, ct))
                        : ScopeGuard.Unavailable(availability, process);
                },
                store.Remember,
                store.Find,
                ClampTimeout(timeout_ms),
                TimeProvider.System,
                Task.Delay,
                ct);

            return format == OutputFormat.Json
                ? ToolHelpers.JsonResult(ExpectationFormatter.ToJsonEnvelope(outcome))
                : ToolHelpers.TextResult(ExpectationFormatter.ToMarkdown(outcome));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return ToolHelpers.ErrorResult(ex);
        }
    }

    internal static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, 0, MaxTimeoutMs);

    /// <summary>
    /// The wait loop, free of UI Automation and real time: observe, evaluate, and — while not everything
    /// passes and the time limit is not reached — pause (200 ms, at most the time left) and observe
    /// again, so the last look happens at the limit. A fail does not end the wait (it may become a
    /// pass; that is what waiting is for); the result is the last evaluation. An observation that has
    /// started is finished even when it runs past the limit, so there is always at least one. Only the
    /// observation the result is about is remembered — each remembered one pushes an older one out of
    /// the store, and a long wait must not cost the caller the ids it observed before.
    /// </summary>
    internal static async Task<ExpectOutcome> RunAsync(
        IReadOnlyList<Expectation> expectations, Func<Observation> observe, Action<Observation> remember,
        Func<string, StoredElement?>? resolveId, int timeoutMs, TimeProvider time,
        Func<TimeSpan, CancellationToken, Task> pause, CancellationToken ct)
    {
        var start = time.GetTimestamp();
        var observations = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var current = observe();
            observations++;

            var outcomes = ExpectationEvaluator.Evaluate(current, expectations, resolveId);
            var result = ExpectationEvaluator.Overall(outcomes);
            var elapsedMs = (long)time.GetElapsedTime(start).TotalMilliseconds;

            if (result == ExpectResult.Pass || elapsedMs >= timeoutMs)
            {
                if (current.Windows.Count > 0)
                    remember(current);

                return new ExpectOutcome(result, outcomes, observations, elapsedMs, TimedOut: timeoutMs > 0 && result != ExpectResult.Pass)
                {
                    Observed = current.Windows.Select(w => new ObservedProcess(w.Process, w.Pid)).Distinct().ToList(),
                };
            }

            await pause(TimeSpan.FromMilliseconds(Math.Min(PollPauseMs, timeoutMs - elapsedMs)), ct);
        }
    }
}

/// <summary>
/// Keeps an Expect call on what it was asked about. scope=foreground is resolved anew for every
/// observation; without this guard a wait for "window closed" or "element absent" would pass as soon
/// as some other application came to the front. The first application seen is pinned: an observation
/// of another one is marked <see cref="Observation.Unobserved"/> (every condition unknown) and the
/// wait goes on — the first application may come back. An observation without any window proves
/// nothing either.
/// </summary>
internal sealed class ScopeGuard(ObserveScope scope)
{
    private static readonly ObservationTimings NoTimings = new(0, 0, 0);

    private ObservedProcess? _pinned;

    public Observation Check(Observation observation)
    {
        // Every window gone or zero-sized between the scope check and the walk.
        if (observation.Windows.Count == 0)
            return observation with { Unobserved = "no window was observed" };
        if (scope != ObserveScope.Foreground)
            return observation;

        var current = new ObservedProcess(observation.Windows[0].Process, observation.Windows[0].Pid);
        _pinned ??= current;
        return current.Pid == _pinned.Pid
            ? observation
            : observation with { Unobserved = $"the foreground application changed during the wait (was {_pinned}, now {current})" };
    }

    /// <summary>An observation of nothing, for a scope that has nothing to look at: every condition unknown.</summary>
    public static Observation Nothing(string why) =>
        new([], null, [], [], "", false, NoTimings) { Unobserved = why };

    /// <summary>
    /// What Expect evaluates when <see cref="ObservationService.CheckScope"/> says there is nothing to
    /// observe. No foreground application: nothing is known. A process without a visible window is
    /// different — scope=process asks about exactly that process's windows, and it has none: the empty
    /// observation is complete, so "closed"/"absent" pass and "open"/"exists" fail.
    /// </summary>
    public static Observation Unavailable(ObservationService.ScopeAvailability availability, string? process) => availability switch
    {
        ObservationService.ScopeAvailability.ProcessHasNoWindow => new([], null, [], [], "", false, NoTimings),
        ObservationService.ScopeAvailability.NoForeground => Nothing("there is no foreground application window"),
        _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, $"scope is available (process '{process}')"),
    };
}
