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
}
