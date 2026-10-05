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
                 "conditions: 1–20 objects, all must hold; each is ONE of: " +
                 "window — {\"window\":\"Login\",\"state\":\"open\"} (state: open (default) | closed; optional \"modal\":true|false with open); " +
                 "element — by id {\"element\":\"<id from Observe>\",\"state\":\"enabled\"} or by selector {\"type\":\"Button\",\"name\":\"OK\",\"state\":\"enabled\"} " +
                 "(selector: type and/or name — name also matches the element's label —, optional panel, in_window; " +
                 "state: exists (default) | absent | enabled | disabled | selected | not_selected | checked | unchecked | expanded | collapsed; " +
                 "optional \"value\":\"...\" (equals, case-sensitive) or \"value_contains\":\"...\", combinable with a state); " +
                 "text — {\"text\":\"finished\"} (a visible static text contains it). " +
                 "Names, panels and window titles compare case-insensitively; \"match\":\"exact\" (default) | \"contains\" per condition. " +
                 "Each condition is answered pass, fail or unknown. unknown means the observation proves neither: the element list is cut, " +
                 "a window is unreadable, several elements fit the selector (narrow it with panel/in_window or use an id), the control does not " +
                 "report the state (docking TabItems never report 'selected' — check an element inside the panel instead), or it is a password field. " +
                 "Overall: pass when all pass, fail when one fails, otherwise unknown. fail and unknown are results, not errors. " +
                 "timeout_ms > 0 repeats the check (every observation plus 200 ms) until all conditions pass or the time is up. " +
                 "Matched element ids are listed and can be passed to Click/Type. " +
                 "format=json shape: {result:\"pass\"|\"fail\"|\"unknown\", passed:int, total:int, observations:int, elapsed_ms:int, timed_out:bool, " +
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

            var outcome = await RunAsync(
                expectations,
                () => observation.Observe(scope, process, ObserveMaxElements, ct),
                store.Remember,
                ClampTimeout(timeout_ms),
                TimeProvider.System,
                Task.Delay,
                ct);

            return format == OutputFormat.Json
                ? ToolHelpers.JsonResult(ExpectationFormatter.ToJsonEnvelope(outcome))
                : ToolHelpers.TextResult(ExpectationFormatter.ToMarkdown(outcome));
        }
        catch (Exception ex)
        {
            return ToolHelpers.ErrorResult(ex);
        }
    }

    internal static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, 0, MaxTimeoutMs);

    /// <summary>
    /// The wait loop, free of UI Automation and real time: observe, evaluate, and — while not everything
    /// passes and another pause still fits into the time limit — pause and observe again. A fail does
    /// not end the wait (it may become a pass; that is what waiting is for); the result is the last
    /// evaluation. An observation that has started is finished even when it runs past the limit, so
    /// there is always at least one. Every observation is remembered, so the ids in the result resolve.
    /// </summary>
    internal static async Task<ExpectOutcome> RunAsync(
        IReadOnlyList<Expectation> expectations, Func<Observation> observe, Action<Observation> remember, int timeoutMs,
        TimeProvider time, Func<TimeSpan, CancellationToken, Task> pause, CancellationToken ct)
    {
        var start = time.GetTimestamp();
        var observations = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var current = observe();
            observations++;
            remember(current);

            var outcomes = ExpectationEvaluator.Evaluate(current, expectations);
            var result = ExpectationEvaluator.Overall(outcomes);
            var elapsedMs = (long)time.GetElapsedTime(start).TotalMilliseconds;

            if (result == ExpectResult.Pass || elapsedMs + PollPauseMs >= timeoutMs)
                return new ExpectOutcome(result, outcomes, observations, elapsedMs, TimedOut: timeoutMs > 0 && result != ExpectResult.Pass);

            await pause(TimeSpan.FromMilliseconds(PollPauseMs), ct);
        }
    }
}
