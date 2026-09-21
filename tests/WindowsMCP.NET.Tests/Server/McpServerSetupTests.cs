using WindowsMcpNet.Server;
using WindowsMcpNet.Tests.TestSupport;
using Xunit;

namespace WindowsMcpNet.Tests.Server;

[Collection(McpToolsCollection.Name)]
public class McpServerSetupTests(McpToolsFixture fixture)
{
    [Fact]
    public void AddWindowsMcpServer_SetsServerInfoAndInstructions()
    {
        Assert.Equal("WindowsMCP.NET", fixture.Options.ServerInfo?.Name);
        Assert.Equal(McpToolsFixture.Version, fixture.Options.ServerInfo?.Version);
        Assert.Equal(McpServerSetup.Instructions, fixture.Options.ServerInstructions);
    }

    [Fact]
    public void Instructions_MentionTheKeyWorkflowTools()
    {
        Assert.Contains("Context", McpServerSetup.Instructions);
        Assert.Contains("Perform", McpServerSetup.Instructions);
        Assert.Contains("ensure", McpServerSetup.Instructions);
        Assert.Contains("[ERROR]", McpServerSetup.Instructions);
    }

    [Fact]
    public void AddWindowsMcpServer_RegistersAllTwentyTools()
    {
        var tools = fixture.Tools.Keys.OrderBy(n => n).ToList();

        Assert.Equal(20, tools.Count);
        Assert.Contains("Context", tools);
        Assert.Contains("Perform", tools);
        Assert.Contains("PowerShell", tools);
    }
}
