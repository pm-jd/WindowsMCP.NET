using System.Text.Json;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ContextToolsTests
{
    [Fact]
    public void DefaultInclude_ReturnsWindowAndScreen()
    {
        var modules = ContextTools.ParseInclude(null);

        Assert.Equal([ContextModule.Window, ContextModule.Screen], modules);
    }

    [Fact]
    public void ParseInclude_WithExplicitModules_PreservesOrder()
    {
        var modules = ContextTools.ParseInclude([ContextModule.Processes, ContextModule.Clipboard, ContextModule.Window]);

        Assert.Equal([ContextModule.Processes, ContextModule.Clipboard, ContextModule.Window], modules);
    }

    [Fact]
    public void ParseInclude_Duplicates_AreCollapsed()
    {
        var modules = ContextTools.ParseInclude([ContextModule.Window, ContextModule.Window, ContextModule.UiTree]);

        Assert.Equal([ContextModule.Window, ContextModule.UiTree], modules);
    }

    [Fact]
    public void ParseInclude_EmptyArray_ReturnsDefault()
    {
        var modules = ContextTools.ParseInclude([]);

        Assert.Equal([ContextModule.Window, ContextModule.Screen], modules);
    }

    [Theory]
    [InlineData("\"ui_tree\"", ContextModule.UiTree)]
    [InlineData("\"UiTree\"", ContextModule.UiTree)]
    [InlineData("\"clipboard\"", ContextModule.Clipboard)]
    public void ContextModule_DeserialisesSnakeCaseAndIgnoresCase(string json, ContextModule expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<ContextModule>(json));
    }

    [Fact]
    public void ContextModule_SerialisesAsSnakeCase()
    {
        Assert.Equal("\"ui_tree\"", JsonSerializer.Serialize(ContextModule.UiTree));
    }
}
