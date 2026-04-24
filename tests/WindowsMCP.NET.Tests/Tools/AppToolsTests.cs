using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class AppToolsTests
{
    private static Microsoft.Extensions.Logging.ILogger<WindowsMcpNet.Services.DesktopService> NullLog =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsMcpNet.Services.DesktopService>.Instance;

    [Fact]
    public async Task Ensure_EmptyName_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "ensure", name: "");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'name' is required", result);
    }

    [Fact]
    public async Task Status_EmptyName_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "status", name: "");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'name' is required", result);
    }

    [Fact]
    public async Task Ensure_UnknownAmbiguous_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "ensure", name: "notepad", ambiguous: "bogus");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("'ambiguous'", result);
    }

    [Fact]
    public async Task App_UnknownMode_ReturnsError()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "quark", name: "notepad");
        Assert.Contains("[ERROR]", result);
        Assert.Contains("Unknown mode", result);
    }

    [Fact]
    public void App_Description_ListsAllFiveModes()
    {
        var method = typeof(AppTools).GetMethod(nameof(AppTools.App))!;
        var attr = (System.ComponentModel.DescriptionAttribute)
            method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)[0];
        Assert.Contains("launch",  attr.Description);
        Assert.Contains("ensure",  attr.Description);
        Assert.Contains("status",  attr.Description);
        Assert.Contains("switch",  attr.Description);
        Assert.Contains("resize",  attr.Description);
    }

    [Fact]
    public async Task Status_FormatJson_NotRunning_ReturnsZeroMatches()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "status",
            name: $"definitely_not_a_real_app_{Guid.NewGuid():N}", format: "json");

        using var doc = System.Text.Json.JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("running").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("match_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("matches").GetArrayLength());
    }

    [Fact]
    public async Task Status_FormatJson_EmptyName_StillReturnsErrorString()
    {
        // Validation runs before format — error remains plain text, not JSON.
        var ds = new WindowsMcpNet.Services.DesktopService(NullLog);
        var result = await AppTools.App(ds, mode: "status", name: "", format: "json");
        Assert.StartsWith("[ERROR]", result);
    }
}
