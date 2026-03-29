using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed class McpTestClient
{
    private readonly McpClient _client;

    public McpTestClient(McpClient client) => _client = client;

    public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken ct = default)
        => await _client.ListToolsAsync(cancellationToken: ct);

    public async Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null, CancellationToken ct = default)
        => await _client.CallToolAsync(toolName, args, cancellationToken: ct);

    public async Task<string> CallToolTextAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null, CancellationToken ct = default)
    {
        var result = await CallToolAsync(toolName, args, ct);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    public async Task<byte[]?> CallToolImageAsync(string toolName, IReadOnlyDictionary<string, object?>? args = null, CancellationToken ct = default)
    {
        var result = await CallToolAsync(toolName, args, ct);
        var image = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
        return image is not null ? image.DecodedData.ToArray() : null;
    }
}
