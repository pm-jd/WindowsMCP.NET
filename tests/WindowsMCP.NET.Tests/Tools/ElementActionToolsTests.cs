using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// Element-id paths of Click/Type/MultiSelect/MultiEdit. An empty <see cref="ObservationStore"/> makes
/// every element call fail at <c>store.Get</c> before any input is sent, so these are safe on a desktop.
/// </summary>
[Collection(McpToolsCollection.Name)]
public class ElementActionToolsTests(McpToolsFixture fixture)
{
    private const string NotFound = "[ERROR] ElementNotFoundException: element e9zz9 no longer present — call Observe";

    private static readonly ObservationService Svc = new(NullLogger<ObservationService>.Instance);
    private static readonly ActionExecutor Executor = new(new InputDriver());

    private static UiTreeService Ui => null!;

    [Fact]
    public void Click_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound, InputTools.Click(Ui, Svc, new ObservationStore(), Executor, element: "e9zz9", ct: TestContext.Current.CancellationToken));

    [Fact]
    public void Type_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound, InputTools.Type(Ui, Svc, new ObservationStore(), Executor, "hi", element: "e9zz9", ct: TestContext.Current.CancellationToken));

    [Fact]
    public void MultiSelect_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound, MultiTools.MultiSelect(Ui, Svc, new ObservationStore(), elements: ["e9zz9"]));

    [Fact]
    public void MultiEdit_UnknownElement_ReturnsElementNotFound() =>
        Assert.Equal(NotFound, MultiTools.MultiEdit(Ui, Svc, new ObservationStore(), Executor,
            elements: JsonDocument.Parse("""[["e9zz9","x"]]""").RootElement));

    [Theory]
    [InlineData("""[["e1"]]""")]
    [InlineData("""[["e1","a","b"]]""")]
    [InlineData("""[["e1",5]]""")]
    [InlineData("""["e1"]""")]
    public void MultiEdit_ElementsShape_Validated(string json)
    {
        var result = MultiTools.MultiEdit(Ui, Svc, new ObservationStore(), Executor,
            elements: JsonDocument.Parse(json).RootElement);

        Assert.StartsWith("[ERROR] ArgumentException", result);
    }

    [Fact]
    public void Click_WithoutLabelOrLocOrElement_KeepsLegacyError() =>
        Assert.Equal("[ERROR] ArgumentException: Either 'label' or 'loc' ([x, y]) must be provided.",
            InputTools.Click(Ui, Svc, new ObservationStore(), Executor, ct: TestContext.Current.CancellationToken));

    [Fact]
    public void Click_Schema_HasElementMethodVerifySettle()
    {
        var parameters = typeof(InputTools).GetMethod(nameof(InputTools.Click))!.GetParameters()
            .ToDictionary(p => p.Name!);

        Assert.Contains("element", parameters);
        Assert.Contains("method", parameters);
        Assert.Contains("verify", parameters);
        Assert.Contains("settle_ms", parameters);
        Assert.Equal(ActionMethod.Auto, parameters["method"].DefaultValue);
        Assert.Equal(300, parameters["settle_ms"].DefaultValue);
    }

    [Theory]
    [InlineData("Click", "element", "method", "verify", "settle_ms")]
    [InlineData("Type", "element", "verify", "settle_ms")]
    [InlineData("MultiSelect", "elements")]
    [InlineData("MultiEdit", "elements")]
    public void InputSchema_HasElementParameters_AndNoServices(string tool, params string[] expected)
    {
        var names = fixture.Tools[tool].ProtocolTool.InputSchema.GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToList();

        foreach (var name in expected)
            Assert.Contains(name, names);

        Assert.DoesNotContain("ct", names);
        Assert.DoesNotContain(names, n => n.EndsWith("Service", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("store", names);
        Assert.DoesNotContain("executor", names);
        Assert.DoesNotContain(names, n => n.Contains("observation", StringComparison.OrdinalIgnoreCase));
    }
}
