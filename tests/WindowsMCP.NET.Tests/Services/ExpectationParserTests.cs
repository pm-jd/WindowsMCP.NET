using System.Text.Json;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ExpectationParser"/> turns the raw <c>conditions</c> JSON of the Expect tool into typed
/// conditions and rejects everything it does not understand — with the 1-based index of the condition,
/// so a caller with twenty conditions knows which one to fix.
/// </summary>
public class ExpectationParserTests
{
    private static IReadOnlyList<Expectation> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ExpectationParser.Parse(document.RootElement);
    }

    [Fact]
    public void Window_Defaults()
    {
        var parsed = Assert.Single(Parse("""[{"window":"Login"}]"""));

        Assert.Equal(new WindowExpectation(1, "Login", ExpectWindowState.Open, null, ExpectMatch.Exact), parsed);
    }

    [Fact]
    public void Window_ClosedContains()
    {
        var parsed = Assert.Single(Parse("""[{"window":"Opt","state":"closed","match":"contains"}]"""));

        Assert.Equal(new WindowExpectation(1, "Opt", ExpectWindowState.Closed, null, ExpectMatch.Contains), parsed);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Window_Modal(string json, bool expected)
    {
        var parsed = Assert.IsType<WindowExpectation>(Assert.Single(Parse($$"""[{"window":"Login","modal":{{json}}}]""")));

        Assert.Equal(expected, parsed.Modal);
    }

    [Fact]
    public void Element_ById()
    {
        var parsed = Assert.Single(Parse("""[{"element":"ejsbw","state":"enabled"}]"""));

        Assert.Equal(
            new ElementExpectation(1, "ejsbw", null, null, null, null, ExpectState.Enabled, null, null, ExpectMatch.Exact),
            parsed);
    }

    [Fact]
    public void Element_Selector_Defaults()
    {
        var parsed = Assert.Single(Parse("""[{"type":"Button","name":"OK"}]"""));

        Assert.Equal(
            new ElementExpectation(1, null, "Button", "OK", null, null, ExpectState.Exists, null, null, ExpectMatch.Exact),
            parsed);
    }

    [Fact]
    public void Element_AllSelectorFields()
    {
        var parsed = Assert.Single(Parse(
            """[{"type":"RadioButton","name":"20x","panel":"Nosepiece","in_window":"MCS","state":"not_selected","match":"contains"}]"""));

        Assert.Equal(
            new ElementExpectation(1, null, "RadioButton", "20x", "Nosepiece", "MCS", ExpectState.NotSelected, null, null,
                ExpectMatch.Contains),
            parsed);
    }

    [Fact]
    public void Element_TypeOnly()
    {
        var parsed = Assert.IsType<ElementExpectation>(Assert.Single(Parse("""[{"type":"ProgressBar"}]""")));

        Assert.Equal("ProgressBar", parsed.Type);
        Assert.Null(parsed.Name);
    }

    [Fact]
    public void Element_PanelOnly()
    {
        // "Is the Errors panel shown" = something in that panel is there (a docking tab cannot tell).
        var parsed = Assert.IsType<ElementExpectation>(Assert.Single(Parse("""[{"panel":"Errors"}]""")));

        Assert.Equal("Errors", parsed.Panel);
        Assert.Null(parsed.Type);
        Assert.Null(parsed.Name);
        Assert.Equal(ExpectState.Exists, parsed.State);
    }

    [Fact]
    public void Element_Value()
    {
        var parsed = Assert.IsType<ElementExpectation>(Assert.Single(Parse("""[{"name":"Focus","value":"90,000"}]""")));

        Assert.Equal("90,000", parsed.Value);
        Assert.Null(parsed.ValueContains);
        Assert.Equal(ExpectState.Exists, parsed.State);
    }

    [Fact]
    public void Element_ValueContains()
    {
        var parsed = Assert.IsType<ElementExpectation>(
            Assert.Single(Parse("""[{"element":"abc12","state":"enabled","value_contains":"90"}]""")));

        Assert.Equal("90", parsed.ValueContains);
        Assert.Null(parsed.Value);
    }

    [Fact]
    public void Element_EmptyValueIsAllowed()
    {
        var parsed = Assert.IsType<ElementExpectation>(Assert.Single(Parse("""[{"name":"User","value":""}]""")));

        Assert.Equal("", parsed.Value);
    }

    [Fact]
    public void Text()
    {
        var parsed = Assert.Single(Parse("""[{"text":"finished"}]"""));

        Assert.Equal(new TextExpectation(1, "finished"), parsed);
    }

    [Fact]
    public void Indexes_AreOneBased()
    {
        var parsed = Parse("""[{"window":"A"},{"text":"b"},{"name":"c"}]""");

        Assert.Equal([1, 2, 3], parsed.Select(p => p.Index));
    }

    [Fact]
    public void States_AreCaseInsensitive()
    {
        var parsed = Assert.IsType<ElementExpectation>(Assert.Single(Parse("""[{"name":"A","state":"Not_Selected"}]""")));

        Assert.Equal(ExpectState.NotSelected, parsed.State);
    }

    [Theory]
    [InlineData("""[]""", "conditions: at least one")]
    [InlineData("""{}""", "conditions: must be an array")]
    [InlineData("""[5]""", "condition 1: must be an object")]
    [InlineData("""[{"state":"open"}]""", "condition 1: needs one of window, element, type, name, panel, text")]
    [InlineData("""[{"window":"A","text":"b"}]""", "condition 1: only one of")]
    [InlineData("""[{"window":"A","name":"b"}]""", "condition 1: only one of")]
    [InlineData("""[{"text":"A","type":"Button"}]""", "condition 1: only one of")]
    [InlineData("""[{"window":"A","colour":"red"}]""", "condition 1: unknown key 'colour'")]
    [InlineData("""[{"text":"A","state":"exists"}]""", "condition 1: unknown key 'state'")]
    [InlineData("""[{"window":"A","value":"x"}]""", "condition 1: unknown key 'value'")]
    [InlineData("""[{"window":"A","state":"visible"}]""", "condition 1: unknown state 'visible' (allowed: open, closed)")]
    [InlineData("""[{"name":"A","state":"visible"}]""",
        "condition 1: unknown state 'visible' (allowed: exists, absent, enabled, disabled, selected, not_selected, checked, unchecked, expanded, collapsed)")]
    [InlineData("""[{"name":"A","match":"regex"}]""", "condition 1: unknown match 'regex' (allowed: exact, contains)")]
    [InlineData("""[{"window":"A","state":"closed","modal":true}]""", "condition 1: modal needs state=open")]
    [InlineData("""[{"name":"A","state":"absent","value":"x"}]""", "condition 1: value cannot be combined with state=absent")]
    [InlineData("""[{"name":"A","state":"absent","value_contains":"x"}]""",
        "condition 1: value_contains cannot be combined with state=absent")]
    [InlineData("""[{"name":"A","value":"x","value_contains":"y"}]""", "condition 1: value and value_contains")]
    [InlineData("""[{"element":"abc","name":"A"}]""", "condition 1: element cannot be combined with name")]
    [InlineData("""[{"element":"abc","match":"contains"}]""", "condition 1: element cannot be combined with match")]
    [InlineData("""[{"in_window":"A"}]""", "condition 1: needs one of window, element, type, name, panel, text")]
    [InlineData("""[{"window":"A","panel":"b"}]""", "condition 1: only one of")]
    [InlineData("""[{"window":5}]""", "condition 1: window must be a string")]
    [InlineData("""[{"name":"A","state":5}]""", "condition 1: state must be a string")]
    [InlineData("""[{"name":"A","value":5}]""", "condition 1: value must be a string")]
    [InlineData("""[{"window":"A","modal":"yes"}]""", "condition 1: modal must be true or false")]
    [InlineData("""[{"window":""}]""", "condition 1: window must not be empty")]
    [InlineData("""[{"name":""}]""", "condition 1: name must not be empty")]
    [InlineData("""[{"text":""}]""", "condition 1: text must not be empty")]
    [InlineData("""[{"element":""}]""", "condition 1: element must not be empty")]
    [InlineData("""[{"name":"A","value_contains":""}]""", "condition 1: value_contains must not be empty")]
    [InlineData("""[{"window":"A"},{"text":""}]""", "condition 2: text must not be empty")]
    public void Invalid(string json, string expectedFragment)
    {
        var ex = Assert.Throws<ArgumentException>(() => Parse(json));

        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void Invalid_TooManyConditions()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("""{"text":"a"}""", 21)) + "]";

        var ex = Assert.Throws<ArgumentException>(() => Parse(json));

        Assert.Contains("conditions: at most 20", ex.Message);
    }

    [Fact]
    public void TwentyConditions_AreAllowed()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("""{"text":"a"}""", 20)) + "]";

        Assert.Equal(20, Parse(json).Count);
    }
}
