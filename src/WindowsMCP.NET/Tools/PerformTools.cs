using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class PerformTools
{
    private static readonly HashSet<string> ValidActions = ["click", "type", "shortcut", "scroll", "move", "wait"];

    [McpServerTool(Name = "Perform", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Execute a sequence of UI actions in one call. " +
                 "steps: array of {action, ...params}. " +
                 "Supported actions: click, type, shortcut, scroll, move, wait. " +
                 "click and type steps accept 'element' (an id from Observe, resolved against the live UI when the step runs; click also 'method': auto|pattern|mouse — " +
                 "auto clicks buttons, links and plain menu items with the mouse when that is safe, because an Invoke that opens a modal dialog blocks UI Automation for the application until the dialog is closed). " +
                 "On a type step with 'element', clear=false (the default) appends the text to the field's current value; pass clear=true to set the field to exactly the text. " +
                 "Element steps report their effect (changed, unchanged, value_verified, value_mismatch, not_verified); " +
                 "after 3 consecutive element steps without visible change the chain stops (stop_on_stall). " +
                 "if_exists skips a step whose element id or label is not found. " +
                 "Returns step-by-step results with optional screenshot.")]
    public static async Task<IList<ContentBlock>> Perform(
        UiTreeService uiTreeService,
        ObservationService observationService,
        ObservationStore observationStore,
        ActionExecutor executor,
        ScreenCaptureService captureService,
        [Description("Array of action steps: [{action, ...params}]")] JsonElement steps,
        [Description("Stop executing on first error")] bool stop_on_error = true,
        [Description("Capture screenshot after execution")] bool snapshot_after = true,
        [Description("Milliseconds to wait between steps")] int delay_between_ms = 100,
        [Description("Verify element steps (effect changed/unchanged)")] bool verify = true,
        [Description("Stop after 3 consecutive element steps without visible change")] bool stop_on_stall = true,
        [Description("Milliseconds to wait before verifying an element step (0–2000)")] int settle_ms = 300,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        var parsed = ParseSteps(steps);
        if (parsed.Count == 0)
            return [new TextContentBlock { Text = "[ERROR] No steps provided." }];

        var chain = await RunChain(parsed,
            (step, stepNum) => RunStep(step, stepNum, uiTreeService, observationService, observationStore, executor,
                verify, settle_ms, ct),
            stop_on_error, stop_on_stall, delay_between_ms, progress, ct);

        var text = FormatResults(chain.Results, chain.Errored, chain.Stalled);
        var content = new List<ContentBlock> { new TextContentBlock { Text = text } };

        if (snapshot_after)
        {
            try
            {
                var pngBytes = captureService.CaptureScreen(null);
                var points = uiTreeService.GetAnnotationPoints()
                    .Select(p => (p.X, p.Y, p.Label))
                    .ToList<(int X, int Y, string Label)>();
                pngBytes = captureService.AnnotateScreenshot(pngBytes, points);
                content.Insert(0, ImageContentBlock.FromBytes(pngBytes, "image/png"));
            }
            catch { /* screenshot is best-effort */ }
        }

        return content;
    }

    internal sealed record ChainResult(List<StepResult> Results, bool Errored, bool Stalled);

    /// <summary>The step loop: runs steps in order, reports progress, stops on error (stop_on_error) or after
    /// three consecutive element steps without visible change (stop_on_stall). Only steps carrying an
    /// <see cref="StepResult.Effect"/> (element steps) feed the stall tracker.</summary>
    internal static async Task<ChainResult> RunChain(IReadOnlyList<ParsedStep> parsed,
        Func<ParsedStep, int, Task<StepResult>> runStep, bool stopOnError, bool stopOnStall, int delayBetweenMs,
        IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
    {
        var results = new List<StepResult>();
        var stall = new StallTracker();
        bool errored = false, stalled = false;

        for (int i = 0; i < parsed.Count; i++)
        {
            var stepNum = i + 1;
            var stepResult = await runStep(parsed[i], stepNum);
            results.Add(stepResult);

            // One notification per step (bound to the request's progressToken by the SDK; a
            // client without a token gets a no-op sink) so long chains are visibly progressing.
            progress?.Report(new ProgressNotificationValue
            {
                Progress = stepNum,
                Total = parsed.Count,
                Message = $"Step {stepNum}/{parsed.Count}: {(stepResult.Success ? "OK" : "FAIL")} — {stepResult.Message}",
            });

            if (!stepResult.Success && stopOnError) { errored = true; break; }

            if (stepResult.Success && stepResult.Effect is { } effect && stall.Record(effect) && stopOnStall)
            {
                stalled = true;
                break;
            }

            if (i < parsed.Count - 1 && delayBetweenMs > 0)
                await Task.Delay(delayBetweenMs, ct);
        }

        return new ChainResult(results, errored, stalled);
    }

    private static async Task<StepResult> RunStep(ParsedStep step, int stepNum, UiTreeService uiTreeService,
        ObservationService observationService, ObservationStore observationStore, ActionExecutor executor,
        bool verify, int settleMs, CancellationToken ct)
    {
        if (step.IsUnknown)
            return new StepResult(stepNum, false, $"Unknown action '{step.Action}'");

        try
        {
            ValidateStep(step);

            // if_exists: skip the step when the referenced label is not in the current UI tree
            if (step.GetBool("if_exists") && step.GetString("label") is { } label && uiTreeService.ResolveLabel(label) is null)
                return new StepResult(stepNum, true, $"Skipped — label '{label}' not found (if_exists)");

            var (text, effect) = await ExecuteStep(step, uiTreeService, observationService, observationStore, executor,
                verify, settleMs, ct);
            return new StepResult(stepNum, true, text, effect);
        }
        catch (ElementNotFoundException) when (step.GetBool("if_exists") && step.GetString("element") is not null)
        {
            return new StepResult(stepNum, true, $"Skipped — element '{step.GetString("element")}' not found (if_exists)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client cancelled; abandon the remaining steps
        }
        catch (Exception ex)
        {
            return new StepResult(stepNum, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Rejects a step whose <c>element</c> would be ignored: an agent that names an element expects the
    /// step to act on exactly that element, so running it without (scroll/move/shortcut/wait have no
    /// element support) or falling back to label/loc (when <c>element</c> is not a string) and
    /// reporting OK would be a silent lie. An explicit JSON null counts as "not provided".
    /// </summary>
    internal static void ValidateStep(ParsedStep step)
    {
        if (step.Get("element") is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
            return;

        if (element.ValueKind != JsonValueKind.String)
            throw new ArgumentException("'element' must be a string: an element id from Observe");

        if (step.Action is not ("click" or "type"))
            throw new ArgumentException("'element' is only supported on click and type steps");
    }

    /// <summary>Runs one step; the effect is non-null only for element steps and arrives as the executor's
    /// <see cref="ActionEffect"/> value (never parsed from the text).</summary>
    private static async Task<(string Text, ActionEffect? Effect)> ExecuteStep(ParsedStep step, UiTreeService uiTreeService,
        ObservationService observationService, ObservationStore observationStore, ActionExecutor executor,
        bool verify, int settleMs, CancellationToken ct)
    {
        var element = step.GetString("element");

        if (element is not null && step.Action == "click")
        {
            var (text, outcome) = InputTools.ClickElement(observationService, observationStore, executor, element,
                step.GetEnum("button", MouseButton.Left), step.GetInt("clicks") ?? 1,
                step.GetEnum("method", ActionMethod.Auto), verify, settleMs, ct);
            return (text, outcome.Effect);
        }

        if (element is not null && step.Action == "type")
        {
            var (text, outcome) = InputTools.TypeElement(observationService, observationStore, executor, element,
                step.GetString("text") ?? throw new ArgumentException("'text' required for type action"),
                step.GetBool("clear"), step.GetBool("press_enter"), verify, settleMs, ct);
            return (text, outcome.Effect);
        }

        var plain = step.Action switch
        {
            "click" => InputTools.Click(uiTreeService, observationService, observationStore, executor,
                loc: step.GetIntArray("loc"),
                label: step.GetString("label"),
                button: step.GetEnum("button", MouseButton.Left),
                clicks: step.GetInt("clicks") ?? 1,
                ct: ct),

            "type" => InputTools.Type(uiTreeService, observationService, observationStore, executor,
                text: step.GetString("text") ?? throw new ArgumentException("'text' required for type action"),
                label: step.GetString("label"),
                loc: step.GetIntArray("loc"),
                clear: step.GetBool("clear"),
                press_enter: step.GetBool("press_enter"),
                ct: ct),

            "shortcut" => InputTools.Shortcut(
                shortcut: step.GetString("shortcut") ?? throw new ArgumentException("'shortcut' required")),

            "scroll" => InputTools.Scroll(uiTreeService,
                direction: step.GetEnum("direction", ScrollDirection.Down),
                wheel_times: step.GetInt("wheel_times") ?? 3,
                loc: step.GetIntArray("loc"),
                label: step.GetString("label"),
                type: step.GetEnum("type", ScrollAxis.Vertical)),

            "move" => InputTools.Move(uiTreeService,
                loc: step.GetIntArray("loc"),
                label: step.GetString("label"),
                drag: step.GetBool("drag")),

            "wait" => await InputTools.Wait(
                duration: step.GetInt("duration") ?? 1, ct: ct),

            _ => throw new ArgumentException($"Unknown action: {step.Action}")
        };
        return (plain, null);
    }

    // --- Public helpers for testing ---

    public static List<ParsedStep> ParseSteps(JsonElement stepsElement)
    {
        var result = new List<ParsedStep>();
        if (stepsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in stepsElement.EnumerateArray())
        {
            var action = item.TryGetProperty("action", out var actionProp)
                ? actionProp.GetString()?.ToLowerInvariant()
                : null;
            var isUnknown = action is null || !ValidActions.Contains(action);
            result.Add(new ParsedStep(action ?? "(missing)", item, isUnknown));
        }
        return result;
    }

    public static string FormatResults(List<StepResult> results, bool stoppedEarly) =>
        FormatResults(results, stoppedEarly, stalled: false);

    internal static string FormatResults(List<StepResult> results, bool stoppedEarly, bool stalled)
    {
        var sb = new StringBuilder();
        int succeeded = results.Count(r => r.Success);
        int total = results.Count;

        foreach (var r in results)
        {
            var status = !r.Success ? "FAIL" : r.Message.StartsWith("Skipped") ? "SKIP" : "OK";
            sb.AppendLine($"Step {r.StepNumber}: {status} — {r.Message}");
        }

        sb.AppendLine();
        if (stalled)
            sb.AppendLine($"Stopped after step {results[^1].StepNumber}: no visible change for 3 steps (stall). {succeeded}/{total} succeeded.");
        else if (stoppedEarly)
            sb.AppendLine($"Stopped after step {results[^1].StepNumber} (stop_on_error=true). {succeeded}/{total} succeeded.");
        else
            sb.AppendLine($"Completed. {succeeded}/{total} succeeded.");

        return sb.ToString().TrimEnd();
    }

    // --- Types ---

    public sealed class ParsedStep
    {
        public string Action { get; }
        public bool IsUnknown { get; }
        private readonly JsonElement _raw;

        public ParsedStep(string action, JsonElement raw, bool isUnknown)
        {
            Action = action;
            _raw = raw;
            IsUnknown = isUnknown;
        }

        public string? GetString(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public int? GetInt(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        public bool GetBool(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

        public JsonElement? Get(string prop) =>
            _raw.TryGetProperty(prop, out var v) ? v : null;

        /// <summary>[x, y] style integer arrays; non-numeric entries are skipped.</summary>
        public int[]? GetIntArray(string prop) =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToArray()
                : null;

        /// <summary>Enum values go through the enum's own JSON converter (snake_case, case-insensitive).</summary>
        public TEnum GetEnum<TEnum>(string prop, TEnum fallback) where TEnum : struct, Enum =>
            _raw.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.Deserialize<TEnum>()
                : fallback;
    }

    public sealed record StepResult(int StepNumber, bool Success, string Message, ActionEffect? Effect = null);
}
