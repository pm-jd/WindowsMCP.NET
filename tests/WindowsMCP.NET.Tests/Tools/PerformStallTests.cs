using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// Perform with element steps: stall detection, element-id resolution failures and the schema.
/// Executors use a fake input driver and only unknown element ids / non-input steps are run,
/// so no test can send real mouse or keyboard input.
/// </summary>
[Collection(McpToolsCollection.Name)]
public class PerformStallTests(McpToolsFixture fixture)
{
    private static readonly ObservationService Svc = new(NullLogger<ObservationService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static async Task<string> RunPerform(string stepsJson, bool stopOnError = true)
    {
        var executor = new ActionExecutor(new FakeInputDriver([]));
        var result = await PerformTools.Perform(null!, Svc, new ObservationStore(), executor, null!, Json(stepsJson),
            stop_on_error: stopOnError, snapshot_after: false, delay_between_ms: 0, ct: Ct);
        return result.OfType<TextContentBlock>().Single().Text;
    }

    // --- StallTracker -----------------------------------------------------------------------------

    [Fact]
    public void StallTracker_ThreeUnchanged_Stops()
    {
        var tracker = new StallTracker();

        Assert.False(tracker.Record(ActionEffect.Unchanged));
        Assert.False(tracker.Record(ActionEffect.Unchanged));
        Assert.True(tracker.Record(ActionEffect.Unchanged));
    }

    [Theory]
    [InlineData(ActionEffect.Changed)]
    [InlineData(ActionEffect.ValueVerified)]
    [InlineData(ActionEffect.ValueMismatch)]
    public void StallTracker_VerifiedEffectResets(ActionEffect reset)
    {
        var tracker = new StallTracker();
        tracker.Record(ActionEffect.Unchanged);
        tracker.Record(ActionEffect.Unchanged);

        Assert.False(tracker.Record(reset));
        Assert.False(tracker.Record(ActionEffect.Unchanged));
        Assert.False(tracker.Record(ActionEffect.Unchanged));
        Assert.True(tracker.Record(ActionEffect.Unchanged));
    }

    [Fact]
    public void StallTracker_NotVerifiedIgnored()
    {
        var tracker = new StallTracker();
        tracker.Record(ActionEffect.Unchanged);
        tracker.Record(ActionEffect.Unchanged);

        Assert.False(tracker.Record(ActionEffect.NotVerified));
        Assert.True(tracker.Record(ActionEffect.Unchanged));
    }

    [Fact]
    public void StallTracker_CustomLimit()
    {
        var tracker = new StallTracker(2);

        Assert.False(tracker.Record(ActionEffect.Unchanged));
        Assert.True(tracker.Record(ActionEffect.Unchanged));
    }

    // --- element steps ----------------------------------------------------------------------------

    [Fact]
    public async Task Perform_ElementStep_UnknownId_FailsStep()
    {
        var text = await RunPerform("""[{"action":"click","element":"e0000"},{"action":"wait","duration":0}]""");

        Assert.StartsWith("Step 1: FAIL — ElementNotFoundException: element e0000 no longer present — call Observe", text);
        Assert.DoesNotContain("Step 2", text);
        Assert.Contains("Stopped after step 1 (stop_on_error=true). 0/1 succeeded.", text);
    }

    [Fact]
    public async Task Perform_TypeElementStep_UnknownId_FailsStep()
    {
        var text = await RunPerform("""[{"action":"type","text":"","element":"e0000"}]""");

        Assert.StartsWith("Step 1: FAIL — ElementNotFoundException: element e0000 no longer present — call Observe", text);
    }

    [Fact]
    public async Task Perform_ElementStep_IfExists_SkipsUnknownId()
    {
        var text = await RunPerform("""[{"action":"click","element":"e0000","if_exists":true},{"action":"wait","duration":0}]""");

        Assert.Contains("Step 1: SKIP — Skipped — element 'e0000' not found (if_exists)", text);
        Assert.Contains("Step 2: OK", text);
        Assert.Contains("Completed. 2/2 succeeded.", text);
    }

    // --- 'element' on steps that cannot use it (F9) -------------------------------------------------
    // Only the wait action is run end to end here: before the fix a scroll/move/shortcut step would
    // really send input. Those actions are covered through the pure ValidateStep check below.

    [Fact]
    public async Task Perform_WaitStepCarryingElement_FailsInsteadOfSilentlyIgnoringIt()
    {
        var text = await RunPerform("""[{"action":"wait","duration":0,"element":"e0000"},{"action":"wait","duration":0}]""");

        Assert.StartsWith("Step 1: FAIL — ArgumentException: 'element' is only supported on click and type steps", text);
        Assert.DoesNotContain("Step 2", text);
        Assert.Contains("Stopped after step 1 (stop_on_error=true). 0/1 succeeded.", text);
    }

    [Fact]
    public async Task Perform_WaitStepCarryingElement_IsNotSkippedByIfExists()
    {
        var text = await RunPerform("""[{"action":"wait","duration":0,"element":"e0000","if_exists":true}]""");

        Assert.StartsWith("Step 1: FAIL — ArgumentException: 'element' is only supported on click and type steps", text);
    }

    private static PerformTools.ParsedStep Step(string json) => PerformTools.ParseSteps(Json($"[{json}]")).Single();

    [Theory]
    [InlineData("""{"action":"scroll","element":"e7q2k"}""")]
    [InlineData("""{"action":"move","loc":[10,10],"element":"e7q2k"}""")]
    [InlineData("""{"action":"shortcut","shortcut":"ctrl+s","element":"e7q2k"}""")]
    [InlineData("""{"action":"wait","duration":1,"element":"e7q2k"}""")]
    public void ValidateStep_ElementOnAStepThatCannotUseIt_Throws(string step)
    {
        var ex = Assert.Throws<ArgumentException>(() => PerformTools.ValidateStep(Step(step)));

        Assert.Equal("'element' is only supported on click and type steps", ex.Message);
    }

    [Theory]
    [InlineData("""{"action":"click","element":"e7q2k"}""")]
    [InlineData("""{"action":"type","text":"x","element":"e7q2k"}""")]
    [InlineData("""{"action":"scroll","direction":"down"}""")]
    [InlineData("""{"action":"move","loc":[10,10]}""")]
    [InlineData("""{"action":"shortcut","shortcut":"ctrl+s"}""")]
    [InlineData("""{"action":"wait","duration":1}""")]
    [InlineData("""{"action":"wait","duration":1,"element":null}""")] // explicit null = not provided
    [InlineData("""{"action":"click","loc":[1,2],"element":null}""")]
    public void ValidateStep_AcceptsElementOnClickAndType_AndStepsWithoutElement(string step) =>
        PerformTools.ValidateStep(Step(step));

    [Theory]
    [InlineData("""{"action":"click","loc":[1,2],"element":5}""")]
    [InlineData("""{"action":"type","text":"x","element":["e7q2k"]}""")]
    [InlineData("""{"action":"scroll","element":true}""")]
    public void ValidateStep_ElementThatIsNotAString_Throws_InsteadOfFallingBackToLabelOrLoc(string step)
    {
        var ex = Assert.Throws<ArgumentException>(() => PerformTools.ValidateStep(Step(step)));

        Assert.Equal("'element' must be a string: an element id from Observe", ex.Message);
    }

    // --- stall loop (driven through the seam, no desktop needed) -----------------------------------

    private static List<PerformTools.ParsedStep> Steps(int n) =>
        PerformTools.ParseSteps(Json("[" + string.Join(",", Enumerable.Repeat("""{"action":"wait","duration":0}""", n)) + "]"));

    private static Func<PerformTools.ParsedStep, int, Task<PerformTools.StepResult>> Fake(List<int> executed, ActionEffect effect) =>
        (_, num) =>
        {
            executed.Add(num);
            return Task.FromResult(new PerformTools.StepResult(num, true, $"Clicked x via Invoke — effect: {effect}", effect));
        };

    [Fact]
    public async Task Perform_StallStops_AfterThreeUnchangedElementSteps()
    {
        var executed = new List<int>();

        var chain = await PerformTools.RunChain(Steps(4), Fake(executed, ActionEffect.Unchanged),
            stopOnError: true, stopOnStall: true, delayBetweenMs: 0, progress: null, ct: Ct);
        var text = PerformTools.FormatResults(chain.Results, chain.Errored, chain.Stalled);

        Assert.Equal([1, 2, 3], executed);
        Assert.True(chain.Stalled);
        Assert.False(chain.Errored);
        Assert.Contains("Stopped after step 3: no visible change for 3 steps (stall). 3/3 succeeded.", text);
        Assert.DoesNotContain("stop_on_error", text);
        Assert.DoesNotContain("Step 4", text);
    }

    [Fact]
    public async Task Perform_StopOnStallFalse_ContinuesAfterThreeUnchanged()
    {
        var executed = new List<int>();

        var chain = await PerformTools.RunChain(Steps(4), Fake(executed, ActionEffect.Unchanged),
            stopOnError: true, stopOnStall: false, delayBetweenMs: 0, progress: null, ct: Ct);
        var text = PerformTools.FormatResults(chain.Results, chain.Errored, chain.Stalled);

        Assert.Equal([1, 2, 3, 4], executed);
        Assert.False(chain.Stalled);
        Assert.False(chain.Errored);
        Assert.Contains("Completed. 4/4 succeeded.", text);
    }

    [Fact]
    public async Task Perform_StepsWithoutEffect_DoNotTouchStallTracker()
    {
        var executed = new List<int>();

        var chain = await PerformTools.RunChain(Steps(5),
            (_, num) =>
            {
                executed.Add(num);
                return Task.FromResult(new PerformTools.StepResult(num, true, "Waited 0s"));
            },
            stopOnError: true, stopOnStall: true, delayBetweenMs: 0, progress: null, ct: Ct);

        Assert.Equal(5, executed.Count);
        Assert.False(chain.Stalled);
        Assert.False(chain.Errored);
    }

    [Fact]
    public async Task Perform_ErrorTakesPrecedence_AndKeepsStopOnErrorMessage()
    {
        var chain = await PerformTools.RunChain(Steps(3),
            (_, num) => Task.FromResult(new PerformTools.StepResult(num, num != 2, "x")),
            stopOnError: true, stopOnStall: true, delayBetweenMs: 0, progress: null, ct: Ct);

        Assert.True(chain.Errored);
        Assert.False(chain.Stalled);
        Assert.Contains("Stopped after step 2 (stop_on_error=true). 1/2 succeeded.",
            PerformTools.FormatResults(chain.Results, chain.Errored, chain.Stalled));
    }

    // --- schema -----------------------------------------------------------------------------------

    [Fact]
    public void Perform_Schema_HasVerifyStallSettle()
    {
        var props = fixture.Tools["Perform"].ProtocolTool.InputSchema.GetProperty("properties");

        Assert.Contains("boolean", props.GetProperty("verify").GetProperty("type").ToString());
        Assert.Contains("boolean", props.GetProperty("stop_on_stall").GetProperty("type").ToString());
        Assert.Contains("integer", props.GetProperty("settle_ms").GetProperty("type").ToString());
        Assert.False(props.TryGetProperty("executor", out _));
        Assert.False(props.TryGetProperty("observationStore", out _));
    }
}
