using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using WindowsMcpNet.Server;
using Xunit;

namespace WindowsMcpNet.Tests.TestSupport;

/// <summary>
/// One DI container with the production registration (services + MCP server), built once and
/// shared by all schema/registration tests. Mirrors the single container of the real process
/// and avoids two test classes creating the same tool concurrently.
/// </summary>
public sealed class McpToolsFixture : IDisposable
{
    public const string Version = "test";

    public ServiceProvider Provider { get; }
    public IReadOnlyDictionary<string, McpServerTool> Tools { get; }
    public McpServerOptions Options { get; }

    public McpToolsFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWindowsMcpServices().AddWindowsMcpServer(Version);
        Provider = services.BuildServiceProvider();
        Tools = Provider.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name);
        Options = Provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    public void Dispose() => Provider.Dispose();
}

[CollectionDefinition(Name)]
public sealed class McpToolsCollection : ICollectionFixture<McpToolsFixture>
{
    public const string Name = "McpTools";
}
