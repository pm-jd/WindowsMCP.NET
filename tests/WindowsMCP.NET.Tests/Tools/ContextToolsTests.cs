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
        Assert.Contains("window", modules);
        Assert.Contains("screen", modules);
        Assert.Equal(2, modules.Count);
    }

    [Fact]
    public void ParseInclude_WithExplicitModules()
    {
        var json = JsonDocument.Parse("""["window", "clipboard", "processes"]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Equal(3, modules.Count);
        Assert.Contains("clipboard", modules);
        Assert.DoesNotContain("screen", modules);
    }

    [Fact]
    public void ParseInclude_UnknownModule_IsIgnored()
    {
        var json = JsonDocument.Parse("""["window", "bogus"]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Single(modules);
        Assert.Contains("window", modules);
    }

    [Fact]
    public void ParseInclude_EmptyArray_ReturnsDefault()
    {
        var json = JsonDocument.Parse("""[]""").RootElement;
        var modules = ContextTools.ParseInclude(json);
        Assert.Contains("window", modules);
        Assert.Contains("screen", modules);
    }
}
