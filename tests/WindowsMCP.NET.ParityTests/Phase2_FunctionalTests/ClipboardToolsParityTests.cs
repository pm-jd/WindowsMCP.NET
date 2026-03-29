using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class ClipboardToolsParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public ClipboardToolsParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Clipboard_SetAndGet_Roundtrips()
    {
        var uniqueText = $"parity_clipboard_{Guid.NewGuid():N}";

        // Set clipboard
        var setResult = await _client.CallToolTextAsync("Clipboard", new Dictionary<string, object?>
        {
            ["mode"] = "set",
            ["text"] = uniqueText
        });
        _output.WriteLine($"Set result: {setResult}");

        // Get clipboard
        var getResult = await _client.CallToolTextAsync("Clipboard", new Dictionary<string, object?>
        {
            ["mode"] = "get"
        });
        _output.WriteLine($"Get result: {getResult}");

        Assert.Equal(uniqueText, getResult);
    }
}
