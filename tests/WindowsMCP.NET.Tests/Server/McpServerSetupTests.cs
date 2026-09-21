using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using WindowsMcpNet.Server;
using Xunit;

namespace WindowsMcpNet.Tests.Server;

public class McpServerSetupTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWindowsMcpServices().AddWindowsMcpServer(version: "1.2.3");
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddWindowsMcpServer_SetsServerInfoAndInstructions()
    {
        using var sp = Build();
        var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal("WindowsMCP.NET", options.ServerInfo?.Name);
        Assert.Equal("1.2.3", options.ServerInfo?.Version);
        Assert.Equal(McpServerSetup.Instructions, options.ServerInstructions);
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
        using var sp = Build();
        var tools = sp.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).OrderBy(n => n).ToList();

        Assert.Equal(20, tools.Count);
        Assert.Contains("Context", tools);
        Assert.Contains("Perform", tools);
        Assert.Contains("PowerShell", tools);
    }
}
