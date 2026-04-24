using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class ErrorFlagParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public ErrorFlagParityTests(McpServerFixture fixture, ITestOutputHelper output)
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
    public async Task UnknownMode_PropagatesIsErrorFlag()
    {
        // Trigger a known error path: bogus mode falls through to ArgumentException.
        var result = await _client.CallToolAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "definitely_not_a_mode",
            ["name"] = "anything",
        });

        Assert.True(result.IsError, "Server should set IsError=true on tool errors so clients don't have to string-match the body.");
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task SuccessfulCall_DoesNotSetIsError()
    {
        // Verify the filter doesn't false-positive on legitimate output.
        var result = await _client.CallToolAsync("Process", new Dictionary<string, object?>
        {
            ["mode"] = "list",
            ["limit"] = 1,
        });

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task ErrorResponse_StillIncludesHumanReadableText()
    {
        // The filter should preserve original text content (additive behavior).
        var result = await _client.CallToolAsync("FileSystem", new Dictionary<string, object?>
        {
            ["mode"] = "read",
            ["path"] = $"C:\\definitely_not_a_real_path_{Guid.NewGuid():N}\\nope.txt",
        });

        Assert.True(result.IsError);
        var text = string.Join("\n", result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(c => c.Text));
        Assert.Contains("[ERROR]", text);
        Assert.Contains("not found", text, StringComparison.OrdinalIgnoreCase);
    }
}
