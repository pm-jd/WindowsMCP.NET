using System.Text.Json;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class PerformToolsTests
{
    [Fact]
    public void ParseSteps_ValidActions()
    {
        var json = JsonDocument.Parse("""
            [
                {"action": "click", "label": "3"},
                {"action": "type", "text": "hello"},
                {"action": "shortcut", "shortcut": "ctrl+s"},
                {"action": "wait", "duration": 1},
                {"action": "scroll", "direction": "down"},
                {"action": "move", "loc": [100, 200]}
            ]
        """).RootElement;

        var steps = PerformTools.ParseSteps(json);
        Assert.Equal(6, steps.Count);
        Assert.Equal("click", steps[0].Action);
        Assert.Equal("type", steps[1].Action);
        Assert.Equal("shortcut", steps[2].Action);
        Assert.Equal("wait", steps[3].Action);
        Assert.Equal("scroll", steps[4].Action);
        Assert.Equal("move", steps[5].Action);
    }

    [Fact]
    public void ParseSteps_UnknownAction_Rejected()
    {
        var json = JsonDocument.Parse("""[{"action": "explode"}]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Single(steps);
        Assert.Equal("explode", steps[0].Action);
        Assert.True(steps[0].IsUnknown);
    }

    [Fact]
    public void ParseSteps_EmptyArray()
    {
        var json = JsonDocument.Parse("""[]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Empty(steps);
    }

    [Fact]
    public void ParseSteps_MissingAction_Rejected()
    {
        var json = JsonDocument.Parse("""[{"text": "no action field"}]""").RootElement;
        var steps = PerformTools.ParseSteps(json);
        Assert.Single(steps);
        Assert.True(steps[0].IsUnknown);
    }

    [Fact]
    public void FormatResults_AllSuccess()
    {
        var results = new List<PerformTools.StepResult>
        {
            new(1, true, "Clicked left at (100,200)"),
            new(2, true, "Typed 5 character(s)"),
        };
        var text = PerformTools.FormatResults(results, false);
        Assert.Contains("Step 1: OK", text);
        Assert.Contains("Step 2: OK", text);
        Assert.Contains("2/2 succeeded", text);
    }

    [Fact]
    public void FormatResults_WithFailure_StopOnError()
    {
        var results = new List<PerformTools.StepResult>
        {
            new(1, true, "Clicked"),
            new(2, false, "Label '99' not found"),
        };
        var text = PerformTools.FormatResults(results, true);
        Assert.Contains("Step 2: FAIL", text);
        Assert.Contains("1/2 succeeded", text);
        Assert.Contains("stop_on_error", text);
    }
}
