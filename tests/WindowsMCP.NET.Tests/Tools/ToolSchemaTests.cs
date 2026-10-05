using System.Text.Json;
using WindowsMcpNet.Server;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// The input schema is the contract the LLM reasons about. Enumerated parameters must expose
/// their allowed values, coordinates must be typed arrays, and infrastructure parameters
/// (services, CancellationToken) must not leak into it.
/// </summary>
[Collection(McpToolsCollection.Name)]
public class ToolSchemaTests(McpToolsFixture fixture)
{
    private JsonElement Schema(string tool) => fixture.Tools[tool].ProtocolTool.InputSchema;

    private JsonElement Prop(string tool, string name) =>
        Schema(tool).GetProperty("properties").GetProperty(name);

    private static string[] EnumValues(JsonElement prop) =>
        prop.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Theory]
    [InlineData("FileSystem", "mode", "read", "write", "read_base64", "write_base64", "copy", "move", "delete", "list", "search", "info")]
    [InlineData("Process", "mode", "list", "kill")]
    [InlineData("Process", "sort_by", "memory", "cpu", "name", "pid")]
    [InlineData("Registry", "mode", "get", "set", "delete", "list")]
    [InlineData("Registry", "type", "String", "DWord", "QWord", "Binary", "ExpandString")]
    [InlineData("App", "mode", "launch", "ensure", "status", "switch", "resize")]
    [InlineData("App", "ambiguous", "first", "error")]
    [InlineData("Clipboard", "mode", "get", "set")]
    [InlineData("Click", "button", "left", "right", "middle")]
    [InlineData("Scroll", "direction", "up", "down", "left", "right")]
    [InlineData("Scroll", "type", "vertical", "horizontal")]
    public void EnumeratedParameters_ExposeAllowedValues(string tool, string param, params string[] expected)
    {
        var values = EnumValues(Prop(tool, param));

        Assert.Equal(expected.OrderBy(v => v), values.OrderBy(v => v));
    }

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Process")]
    [InlineData("Registry")]
    [InlineData("App")]
    public void FormatParameter_IsMarkdownOrJson(string tool)
    {
        Assert.Equal(["json", "markdown"], EnumValues(Prop(tool, "format")).OrderBy(v => v));
    }

    [Theory]
    [InlineData("Click", "loc")]
    [InlineData("Type", "loc")]
    [InlineData("Scroll", "loc")]
    [InlineData("Move", "loc")]
    [InlineData("App", "window_loc")]
    [InlineData("App", "window_size")]
    public void CoordinateParameters_AreIntegerArrays(string tool, string param)
    {
        var prop = Prop(tool, param);
        var type = prop.TryGetProperty("type", out var t) ? t : default;
        // nullable arrays render as ["array","null"] or "array"
        var typeText = type.ValueKind == JsonValueKind.Array
            ? string.Join(",", type.EnumerateArray().Select(x => x.GetString()))
            : type.GetString();

        Assert.Contains("array", typeText);
        Assert.Equal("integer", prop.GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void ContextInclude_IsArrayOfModuleEnum()
    {
        var items = Prop("Context", "include").GetProperty("items");

        Assert.Equal(["clipboard", "processes", "screen", "ui_tree", "window"], EnumValues(items).OrderBy(v => v));
    }

    [Theory]
    [InlineData("PowerShell")]
    [InlineData("Wait")]
    [InlineData("Perform")]
    [InlineData("App")]
    public void InfrastructureParameters_DoNotLeakIntoSchema(string tool)
    {
        var names = Schema(tool).GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        Assert.DoesNotContain("ct", names);
        Assert.DoesNotContain("progress", names);
        Assert.DoesNotContain(names, n => n.EndsWith("Service", StringComparison.Ordinal));
    }

    [Fact]
    public void Observe_MaxElements_DefaultsTo300_InTheMethodAndInTheSchema()
    {
        // A real MCS program screen has 282 actionable elements; the former default of 150 cut it.
        var parameter = typeof(ObserveTools).GetMethod(nameof(ObserveTools.Observe))!.GetParameters()
            .Single(p => p.Name == "max_elements");

        Assert.Equal(300, parameter.DefaultValue);
        Assert.Equal(300, Prop("Observe", "max_elements").GetProperty("default").GetInt32());
    }

    // --- wire format: what the schema advertises must be what the argument binder accepts ---

    [Theory]
    [InlineData("\"read_base64\"", FileSystemMode.ReadBase64)]
    [InlineData("\"ReadBase64\"", FileSystemMode.ReadBase64)]
    [InlineData("\"list\"", FileSystemMode.List)]
    [InlineData("\"LIST\"", FileSystemMode.List)]
    public void ToolSerializerOptions_ReadEnumsAsSnakeCaseOrMemberName(string json, FileSystemMode expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<FileSystemMode>(json, McpServerSetup.ToolSerializerOptions));
    }

    [Fact]
    public void ToolSerializerOptions_WriteEnumsAsSnakeCase_AndKeepProtocolEnumsAsStrings()
    {
        Assert.Equal("\"read_base64\"", JsonSerializer.Serialize(FileSystemMode.ReadBase64, McpServerSetup.ToolSerializerOptions));
        Assert.Equal("\"DWord\"", JsonSerializer.Serialize(RegistryValueType.DWord, McpServerSetup.ToolSerializerOptions));
        Assert.Equal("\"assistant\"", JsonSerializer.Serialize(ModelContextProtocol.Protocol.Role.Assistant, McpServerSetup.ToolSerializerOptions));
    }

    [Fact]
    public void EveryToolEnum_DeclaresItsJsonConverter()
    {
        // Without the global SDK enum converter, an enum lacking its own [JsonConverter] would be
        // exposed as an integer. Every enum in the Tools namespace must therefore carry one.
        var toolEnums = typeof(ToolHelpers).Assembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace == typeof(ToolHelpers).Namespace)
            .ToList();

        Assert.NotEmpty(toolEnums);
        Assert.All(toolEnums, t => Assert.NotNull(t.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonConverterAttribute), false).SingleOrDefault()));
    }

    [Fact]
    public void Expect_IsReadOnly_AndTakesAConditionsArray()
    {
        var tool = fixture.Tools["Expect"].ProtocolTool;

        Assert.True(tool.Annotations?.ReadOnlyHint);
        Assert.Contains("conditions", Schema("Expect").GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, Prop("Expect", "timeout_ms").GetProperty("default").GetInt32());
        Assert.Equal(["desktop", "foreground", "process"], EnumValues(Prop("Expect", "scope")).OrderBy(v => v));
        Assert.Equal(["json", "markdown"], EnumValues(Prop("Expect", "format")).OrderBy(v => v));
    }
}
