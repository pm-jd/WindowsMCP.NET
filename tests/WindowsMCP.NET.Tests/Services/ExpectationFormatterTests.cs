using System.Text.Json;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>The two output shapes of the Expect tool (design spec §1.4).</summary>
public class ExpectationFormatterTests
{
    private static readonly ExpectOutcome FailOutcome = new(
        ExpectResult.Fail,
        [
            new ConditionOutcome(1, ExpectResult.Pass, "window 'Login' open", "open, modal", []),
            new ConditionOutcome(2, ExpectResult.Pass, "Button 'OK' enabled", "enabled", ["k3f9a"]),
            new ConditionOutcome(3, ExpectResult.Fail, "Edit 'User' value 'admin'", "value is ''", ["p01zq"]),
        ],
        Observations: 1, ElapsedMs: 1240, TimedOut: false);

    private static JsonElement JsonOf(ExpectOutcome outcome) =>
        JsonSerializer.SerializeToElement(ExpectationFormatter.ToJsonEnvelope(outcome), ToolHelpers.JsonOptions);

    [Fact]
    public void Markdown_Fail_MatchesSpecExample()
    {
        const string expected =
            "Expect: FAIL (2 of 3 passed) — 1 observation, 1240 ms\n" +
            "pass     1 window 'Login' open — open, modal\n" +
            "pass     2 Button 'OK' enabled — enabled (id k3f9a)\n" +
            "fail     3 Edit 'User' value 'admin' — value is '' (id p01zq)";

        Assert.Equal(expected, ExpectationFormatter.ToMarkdown(FailOutcome));
    }

    [Fact]
    public void Markdown_Unknown_Timeout_Header()
    {
        var outcome = new ExpectOutcome(
            ExpectResult.Unknown,
            [
                new ConditionOutcome(1, ExpectResult.Pass, "window 'MCS' open", "open", []),
                new ConditionOutcome(2, ExpectResult.Unknown, "TabItem 'Camera' selected",
                    "a TabItem does not report its selection reliably", ["ejsbw"]),
            ],
            Observations: 4, ElapsedMs: 3050, TimedOut: true);

        var lines = ExpectationFormatter.ToMarkdown(outcome).Split('\n');

        Assert.Equal("Expect: UNKNOWN (1 of 2 passed) — 4 observations, 3050 ms (timeout)", lines[0]);
        Assert.Equal("unknown  2 TabItem 'Camera' selected — a TabItem does not report its selection reliably (id ejsbw)", lines[2]);
    }

    [Fact]
    public void Markdown_Pass_Header()
    {
        var outcome = new ExpectOutcome(
            ExpectResult.Pass, [new ConditionOutcome(1, ExpectResult.Pass, "text 'x'", "found 'x'", [])], 2, 480, false);

        Assert.StartsWith("Expect: PASS (1 of 1 passed) — 2 observations, 480 ms\n", ExpectationFormatter.ToMarkdown(outcome));
    }

    [Fact]
    public void Markdown_SeveralIds()
    {
        var outcome = new ExpectOutcome(
            ExpectResult.Unknown,
            [new ConditionOutcome(1, ExpectResult.Unknown, "Button 'X' enabled", "2 elements match", ["a1", "b2"])], 1, 5, false);

        Assert.EndsWith("— 2 elements match (ids a1, b2)", ExpectationFormatter.ToMarkdown(outcome));
    }

    [Fact]
    public void Markdown_TwoDigitIndexes_StayAligned()
    {
        var conditions = Enumerable.Range(1, 10)
            .Select(i => new ConditionOutcome(i, ExpectResult.Pass, "text 'x'", "found 'x'", [])).ToList();

        var lines = ExpectationFormatter.ToMarkdown(new ExpectOutcome(ExpectResult.Pass, conditions, 1, 5, false)).Split('\n');

        Assert.Equal("pass     9 text 'x' — found 'x'", lines[9]);
        Assert.Equal("pass    10 text 'x' — found 'x'", lines[10]);
    }

    [Fact]
    public void Json_Shape()
    {
        var json = JsonOf(FailOutcome);

        Assert.Equal("fail", json.GetProperty("result").GetString());
        Assert.Equal(2, json.GetProperty("passed").GetInt32());
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Equal(1, json.GetProperty("observations").GetInt32());
        Assert.Equal(1240, json.GetProperty("elapsed_ms").GetInt64());
        Assert.False(json.GetProperty("timed_out").GetBoolean());

        var third = json.GetProperty("conditions")[2];
        Assert.Equal(3, third.GetProperty("index").GetInt32());
        Assert.Equal("fail", third.GetProperty("result").GetString());
        Assert.Equal("Edit 'User' value 'admin'", third.GetProperty("condition").GetString());
        Assert.Equal("value is ''", third.GetProperty("actual").GetString());
        Assert.Equal(["p01zq"], third.GetProperty("elements").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Json_ElementsOmittedWhenEmpty() =>
        Assert.False(JsonOf(FailOutcome).GetProperty("conditions")[0].TryGetProperty("elements", out _));

    [Fact]
    public void Json_UnknownAndTimedOut()
    {
        var json = JsonOf(new ExpectOutcome(
            ExpectResult.Unknown, [new ConditionOutcome(1, ExpectResult.Unknown, "c", "a", [])], 3, 900, true));

        Assert.Equal("unknown", json.GetProperty("result").GetString());
        Assert.True(json.GetProperty("timed_out").GetBoolean());
        Assert.Equal(0, json.GetProperty("passed").GetInt32());
    }
}
