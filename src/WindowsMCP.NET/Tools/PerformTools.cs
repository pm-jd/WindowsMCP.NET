using System.ComponentModel;
using System.Text;
using System.Text.Json;
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
                 "Returns step-by-step results with optional screenshot.")]
    public static async Task<IList<ContentBlock>> Perform(
        UiTreeService uiTreeService,
        ScreenCaptureService captureService,
        [Description("Array of action steps: [{action, ...params}]")] JsonElement steps,
        [Description("Stop executing on first error")] bool stop_on_error = true,
        [Description("Capture screenshot after execution")] bool snapshot_after = true,
        [Description("Milliseconds to wait between steps")] int delay_between_ms = 100)
    {
        var parsed = ParseSteps(steps);
        if (parsed.Count == 0)
            return [new TextContentBlock { Text = "[ERROR] No steps provided." }];

        var results = new List<StepResult>();
        bool stopped = false;

        for (int i = 0; i < parsed.Count; i++)
        {
            var step = parsed[i];
            var stepNum = i + 1;

            if (step.IsUnknown)
            {
                results.Add(new StepResult(stepNum, false, $"Unknown action '{step.Action}'"));
                if (stop_on_error) { stopped = true; break; }
                continue;
            }

            try
            {
                // Check if_exists: skip step if label not found
                if (step.GetBool("if_exists"))
                {
                    var label = step.GetString("label");
                    if (label is not null && uiTreeService.ResolveLabel(label) is null)
                    {
                        results.Add(new StepResult(stepNum, true, $"Skipped — label '{label}' not found (if_exists)"));
                        continue;
                    }
                }

                var msg = await ExecuteStep(step, uiTreeService);
                results.Add(new StepResult(stepNum, true, msg));
            }
            catch (Exception ex)
            {
                results.Add(new StepResult(stepNum, false, $"{ex.GetType().Name}: {ex.Message}"));
                if (stop_on_error) { stopped = true; break; }
            }

            if (i < parsed.Count - 1 && delay_between_ms > 0)
                await Task.Delay(delay_between_ms);
        }

        var text = FormatResults(results, stopped);
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

    private static async Task<string> ExecuteStep(ParsedStep step, UiTreeService uiTreeService)
    {
        return step.Action switch
        {
            "click" => InputTools.Click(uiTreeService,
                loc: step.Get("loc"),
                label: step.GetString("label"),
                button: step.GetString("button") ?? "left",
                clicks: step.GetInt("clicks") ?? 1),

            "type" => InputTools.Type(uiTreeService,
                text: step.GetString("text") ?? throw new ArgumentException("'text' required for type action"),
                label: step.GetString("label"),
                loc: step.Get("loc"),
                clear: step.GetBool("clear"),
                press_enter: step.GetBool("press_enter")),

            "shortcut" => InputTools.Shortcut(
                shortcut: step.GetString("shortcut") ?? throw new ArgumentException("'shortcut' required")),

            "scroll" => InputTools.Scroll(uiTreeService,
                direction: step.GetString("direction") ?? "down",
                wheel_times: step.GetInt("wheel_times") ?? 3,
                loc: step.Get("loc"),
                label: step.GetString("label")),

            "move" => InputTools.Move(uiTreeService,
                loc: step.Get("loc"),
                label: step.GetString("label"),
                drag: step.GetBool("drag")),

            "wait" => await InputTools.Wait(
                duration: step.GetInt("duration") ?? 1),

            _ => throw new ArgumentException($"Unknown action: {step.Action}")
        };
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

    public static string FormatResults(List<StepResult> results, bool stoppedEarly)
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
        if (stoppedEarly)
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
    }

    public sealed record StepResult(int StepNumber, bool Success, string Message);
}
