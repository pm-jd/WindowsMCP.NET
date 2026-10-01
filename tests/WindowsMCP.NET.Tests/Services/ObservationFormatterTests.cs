using System.Drawing;
using System.Text.Json;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ObservationFormatter"/> is a pure function over synthetic <see cref="Observation"/>
/// instances — no UIA involved — so the markdown/JSON shapes from the design spec and task brief are
/// exercised here without a desktop session.
/// </summary>
public class ObservationFormatterTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 10, HitMs: 5, TotalMs: 15);
    private static readonly ElementLocator Locator = new("app.exe", "MainWin", []);

    private static ObservedWindow Window(
        string title, string className = "MainWin", string process = "app.exe", int pid = 100,
        bool foreground = false, bool modal = false, int x = 0, int y = 0, int w = 800, int h = 600) =>
        new(1, title, className, process, pid, foreground, modal, new Rectangle(x, y, w, h));

    private static ObservedElement Element(
        string id, string type, string name, string? label = null, string? panel = null,
        string? window = null, string? value = null, string? toggle = null, bool selected = false,
        string? expand = null, bool enabled = true, int x = 0, int y = 0, int w = 10, int h = 10) =>
        new(id, type, name, label, panel, window, value, toggle, selected, expand, enabled,
            new Rectangle(x, y, w, h), Locator);

    private static Observation MakeObservation(
        IReadOnlyList<ObservedWindow> windows, IReadOnlyList<ObservedElement> elements,
        string? focusId = null, IReadOnlyList<string>? texts = null, string signature = "abcd1234",
        bool truncated = false) =>
        new(windows, focusId, elements, texts ?? [], signature, truncated, Timings);

    [Fact]
    public void Markdown_ElementLine_Format()
    {
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e7q2k", "RadioButton", "20x", panel: "Nosepiece") };
        var observation = MakeObservation(windows, elements);

        var markdown = ObservationFormatter.ToMarkdown(observation);

        Assert.Contains("e7q2k  RadioButton '20x'  @Nosepiece", markdown.Split('\n'));
    }

    [Fact]
    public void Markdown_DisabledAndValue()
    {
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e1a2b", "Edit", "Focus Axis", value: "90,000", enabled: false) };
        var observation = MakeObservation(windows, elements);

        var markdown = ObservationFormatter.ToMarkdown(observation);

        Assert.Contains("value=90,000", markdown);
        Assert.Contains("disabled", markdown);
    }

    [Fact]
    public void Markdown_NonAsciiNames()
    {
        var windows = new[] { Window("Main") };
        var elements = new[]
        {
            Element("e1", "Button", "Schließen"),
            Element("e2", "Button", ""),
            Element("e3", "Button", "Scale (µm/px)"),
        };
        var observation = MakeObservation(windows, elements);

        var markdown = ObservationFormatter.ToMarkdown(observation);

        Assert.Contains("Schließen", markdown);
        Assert.Contains("", markdown);
        Assert.Contains("Scale (µm/px)", markdown);
    }

    [Fact]
    public void Markdown_Footer_Truncated()
    {
        var windows = new[] { Window("Main") };
        var observation = MakeObservation(windows, [], truncated: true);

        var markdown = ObservationFormatter.ToMarkdown(observation);

        Assert.EndsWith("· truncated", markdown);
    }

    [Fact]
    public void Json_OmitsDefaults()
    {
        var windows = new[] { Window("Main") };
        var elements = new[]
        {
            Element("e1", "Button", "Enabled", x: 1, y: 2, w: 3, h: 4),
            Element("e2", "Button", "Disabled", enabled: false),
        };
        var observation = MakeObservation(windows, elements);

        var envelope = ObservationFormatter.ToJsonEnvelope(observation);
        var json = ToolHelpers.JsonResult(envelope).StructuredContent!.Value;
        var elementsJson = json.GetProperty("elements");

        Assert.False(elementsJson[0].TryGetProperty("enabled", out _));
        Assert.True(elementsJson[1].TryGetProperty("enabled", out var enabledProp));
        Assert.False(enabledProp.GetBoolean());

        var rect = elementsJson[0].GetProperty("rect");
        Assert.Equal(JsonValueKind.Array, rect.ValueKind);
        Assert.Equal(4, rect.GetArrayLength());
        Assert.Equal(1, rect[0].GetInt32());
        Assert.Equal(2, rect[1].GetInt32());
        Assert.Equal(3, rect[2].GetInt32());
        Assert.Equal(4, rect[3].GetInt32());
    }

    [Fact]
    public void Markdown_ExtendedId_KeepsSeparator()
    {
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e1a2b3c", "RadioButton", "20x") };
        var observation = MakeObservation(windows, elements);

        var markdown = ObservationFormatter.ToMarkdown(observation);
        var line = Assert.Single(markdown.Split('\n'), l => l.Contains("RadioButton '20x'", StringComparison.Ordinal));

        Assert.StartsWith("e1a2b3c  RadioButton", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_EmptyValue_Rendered()
    {
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e1", "Edit", "Field", value: "") };
        var observation = MakeObservation(windows, elements);

        var markdown = ObservationFormatter.ToMarkdown(observation);
        Assert.Contains("value=", markdown);

        var envelope = ObservationFormatter.ToJsonEnvelope(observation);
        var json = ToolHelpers.JsonResult(envelope).StructuredContent!.Value;
        var elementJson = json.GetProperty("elements")[0];

        Assert.True(elementJson.TryGetProperty("value", out var valueProp));
        Assert.Equal("", valueProp.GetString());
    }

    // --- display limits and escaping (F6, F7) ------------------------------------------------------

    private static JsonElement JsonOf(Observation observation) =>
        ToolHelpers.JsonResult(ObservationFormatter.ToJsonEnvelope(observation)).StructuredContent!.Value;

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]
    [InlineData("exactly10!+", 10, "exactly10!…(+1 chars)")]
    [InlineData("", 10, "")]
    public void Clip_KeepsUpToMax_ThenAppendsTheRemainderCount(string input, int max, string expected) =>
        Assert.Equal(expected, ObservationFormatter.Clip(input, max));

    [Fact]
    public void Clip_DoesNotSplitASurrogatePair()
    {
        var input = "ab😀cd"; // the emoji is two UTF-16 chars at index 2 and 3

        Assert.Equal("ab…(+4 chars)", ObservationFormatter.Clip(input, 3));
        Assert.Equal("ab😀…(+2 chars)", ObservationFormatter.Clip(input, 4));
    }

    [Theory]
    [InlineData("a\nb", @"a\nb")]
    [InlineData("a\r\nb", @"a\r\nb")]
    [InlineData("a\tb", @"a\tb")]
    [InlineData("a\u2028b\u0085c\u0007d", @"a\u2028b\u0085c\u0007d")]
    [InlineData(@"C:\new\table", @"C:\new\table")] // ordinary text, backslashes included, is left alone
    public void EscapeControl_RendersControlCharactersAsEscapes(string input, string expected) =>
        Assert.Equal(expected, ObservationFormatter.EscapeControl(input));

    [Fact]
    public void Markdown_ControlCharactersAnywhere_NeverBreakTheOneLinePerElementLayout()
    {
        var windows = new[] { Window("Main\nwindow") };
        var elements = new[]
        {
            Element("e1aaa", "Edit", "na\nme", label: "la\tbel", panel: "pa\r\nnel", value: "line 1\r\nline 2\tend"),
            Element("e2bbb", "Button", "OK"),
        };
        var observation = MakeObservation(windows, elements, texts: ["first\nsecond", "plain"]);

        var lines = ObservationFormatter.ToMarkdown(observation).Split('\n');

        // heading, blank, two element lines, blank, texts, footer — nothing else.
        Assert.Equal(7, lines.Length);
        Assert.Equal(@"## Main\nwindow  (app.exe, pid 100)", lines[0]);
        Assert.Equal(@"e1aaa  Edit 'na\nme'  label='la\tbel'  @pa\r\nnel  value=line 1\r\nline 2\tend", lines[2]);
        Assert.Equal("e2bbb  Button 'OK'", lines[3]);
        Assert.Equal(@"texts: first\nsecond · plain", lines[5]);
        Assert.DoesNotContain(lines, l => l.Contains('\r') || l.Contains('\t'));
    }

    [Fact]
    public void Json_KeepsRealControlCharacters()
    {
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e1", "Edit", "na\nme", label: "la\tbel", value: "line 1\r\nline 2") };
        var json = JsonOf(MakeObservation(windows, elements, texts: ["first\nsecond"]));

        var element = json.GetProperty("elements")[0];
        Assert.Equal("na\nme", element.GetProperty("name").GetString());
        Assert.Equal("la\tbel", element.GetProperty("label").GetString());
        Assert.Equal("line 1\r\nline 2", element.GetProperty("value").GetString());
        Assert.Equal("first\nsecond", json.GetProperty("texts")[0].GetString());
    }

    [Fact]
    public void Value_LongerThan200_IsTruncatedInMarkdownAndJson()
    {
        var value = new string('a', 200) + new string('b', 4800);
        var windows = new[] { Window("Main") };
        var observation = MakeObservation(windows, [Element("e1", "Document", "Editor", value: value)]);
        var expected = new string('a', 200) + "…(+4800 chars)";

        var markdown = ObservationFormatter.ToMarkdown(observation);
        var line = Assert.Single(markdown.Split('\n'), l => l.StartsWith("e1 ", StringComparison.Ordinal));
        Assert.Equal($"e1     Document 'Editor'  value={expected}", line);

        Assert.Equal(expected, JsonOf(observation).GetProperty("elements")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Value_OfExactly200_IsNotTruncated()
    {
        var value = new string('a', 200);
        var observation = MakeObservation([Window("Main")], [Element("e1", "Edit", "Field", value: value)]);

        Assert.Contains($"value={value}", ObservationFormatter.ToMarkdown(observation).Split('\n')[2], StringComparison.Ordinal);
        Assert.DoesNotContain("…", ObservationFormatter.ToMarkdown(observation), StringComparison.Ordinal);
        Assert.Equal(value, JsonOf(observation).GetProperty("elements")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void NamesLabelsPanelsAndTexts_LongerThan120_AreTruncatedInMarkdownAndJson()
    {
        string Long(char c) => new string(c, 120) + "tail";
        string Clipped(char c) => new string(c, 120) + "…(+4 chars)";
        var windows = new[] { Window("Main") };
        var elements = new[] { Element("e1", "Edit", Long('n'), label: Long('l'), panel: Long('p')) };
        var observation = MakeObservation(windows, elements, texts: [Long('t'), "short"]);

        var lines = ObservationFormatter.ToMarkdown(observation).Split('\n');
        Assert.Equal($"e1     Edit '{Clipped('n')}'  label='{Clipped('l')}'  @{Clipped('p')}", lines[2]);
        Assert.Equal($"texts: {Clipped('t')} · short", lines[4]);

        var json = JsonOf(observation);
        var element = json.GetProperty("elements")[0];
        Assert.Equal(Clipped('n'), element.GetProperty("name").GetString());
        Assert.Equal(Clipped('l'), element.GetProperty("label").GetString());
        Assert.Equal(Clipped('p'), element.GetProperty("panel").GetString());
        Assert.Equal(Clipped('t'), json.GetProperty("texts")[0].GetString());
        Assert.Equal("short", json.GetProperty("texts")[1].GetString());
    }

    [Fact]
    public void Markdown_EmptyName_IsRenderedAsEmptyQuotes()
    {
        var observation = MakeObservation([Window("Main")], [Element("e1abc", "Edit", "", label: "Serial number")]);

        var markdown = ObservationFormatter.ToMarkdown(observation);

        Assert.Contains("e1abc  Edit ''  label='Serial number'", markdown.Split('\n'));
    }

    [Fact]
    public void Markdown_GroupsByWindow_EachElementOnce()
    {
        // Two windows with the same (empty) title, as the spike saw for untitled popups: an
        // element whose Window matches must land in the FIRST such window, never rendered twice.
        var windows = new[]
        {
            Window("FileDropDown"),
            Window(""),
            Window(""),
            Window("MCS - service", foreground: true)
        };
        var elements = new[]
        {
            Element("e1", "MenuItem", "Save", window: "FileDropDown"),
            Element("e2", "MenuItem", "Untitled", window: ""),
            Element("e3", "Button", "Open") // Window == null -> last window
        };
        var observation = MakeObservation(windows, elements, focusId: "e3");

        var markdown = ObservationFormatter.ToMarkdown(observation);
        var lines = markdown.Split('\n');
        var headingIndices = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].StartsWith("## ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, headingIndices.Count);

        var saveIndex = Array.FindIndex(lines, l => l.Contains("Save", StringComparison.Ordinal));
        var untitledIndex = Assert.Single(lines, l => l.Contains("Untitled", StringComparison.Ordinal));
        var openIndex = Array.FindIndex(lines, l => l.Contains("Open", StringComparison.Ordinal));

        Assert.Equal(headingIndices[0], NearestHeadingIndex(saveIndex));
        Assert.Equal(headingIndices[1], NearestHeadingIndex(Array.IndexOf(lines, untitledIndex)));
        Assert.Equal(headingIndices[3], NearestHeadingIndex(openIndex));

        Assert.Equal("focus: e3", lines[headingIndices[0] + 1]);
        Assert.Equal(1, lines.Count(l => l.StartsWith("focus:", StringComparison.Ordinal)));
        return;

        int NearestHeadingIndex(int lineIndex) => headingIndices.Last(h => h <= lineIndex);
    }
}
