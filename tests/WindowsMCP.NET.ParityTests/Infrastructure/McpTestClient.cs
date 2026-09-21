using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace WindowsMcpNet.ParityTests.Infrastructure;

/// <summary>
/// Thin wrapper over <see cref="McpClient"/> for the parity tests. Every call is bound to
/// <see cref="TestContext.Current"/>'s cancellation token so a cancelled or timed-out test
/// tears down the in-flight MCP request instead of hanging on the server process.
/// </summary>
public sealed class McpTestClient
{
    private readonly McpClient _client;

    public McpTestClient(McpClient client) => _client = client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<IList<McpClientTool>> ListToolsAsync()
        => await _client.ListToolsAsync(cancellationToken: Ct);

    public async Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null)
        => await _client.CallToolAsync(toolName, args, cancellationToken: Ct);

    public async Task<string> CallToolTextAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null)
    {
        var result = await CallToolAsync(toolName, args);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    public async Task<byte[]?> CallToolImageAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null)
    {
        var result = await CallToolAsync(toolName, args);
        var image = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
        return image is not null ? image.DecodedData.ToArray() : null;
    }
}
