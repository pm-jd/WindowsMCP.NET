using ModelContextProtocol.Protocol;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class ScreenToolsParityTests : IAsyncLifetime
{
    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Screenshot_DiagnoseContentTypes()
    {
        var client = new McpTestClient(_fixture.Client);
        var result = await client.CallToolAsync("Screenshot");
        _output.WriteLine($"IsError: {result.IsError}");
        _output.WriteLine($"Content.Count: {result.Content.Count}");
        foreach (var block in result.Content)
        {
            _output.WriteLine($"  Block: RuntimeType={block.GetType().FullName}, Type={block.Type}");
            if (block is ImageContentBlock img)
                _output.WriteLine($"    -> ImageContentBlock, Data.Length={img.Data.Length}");
            else if (block is TextContentBlock txt)
                _output.WriteLine($"    -> TextContentBlock, Text={txt.Text}");
            else
                _output.WriteLine($"    -> OTHER CONTENT BLOCK TYPE");
        }
        var hasImage2 = result.Content.OfType<ImageContentBlock>().Any();
        _output.WriteLine($"Has image: {hasImage2}, IsError: {result.IsError}");
        Assert.True(hasImage2, "Should have image block");
        Assert.True(result.Content.Count > 0, "Screenshot should return at least one content block");
    }
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public ScreenToolsParityTests(McpServerFixture fixture, ITestOutputHelper output)
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
    public async Task Screenshot_ReturnsValidPng()
    {
        var imageBytes = await _client.CallToolImageAsync("Screenshot");

        Assert.NotNull(imageBytes);
        _output.WriteLine($"Image bytes: {imageBytes!.Length:N0}");

        // Verify PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(imageBytes.Length > 1000, $"Image too small: {imageBytes.Length} bytes");
        Assert.Equal(0x89, imageBytes[0]);
        Assert.Equal(0x50, imageBytes[1]); // 'P'
        Assert.Equal(0x4E, imageBytes[2]); // 'N'
        Assert.Equal(0x47, imageBytes[3]); // 'G'
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Snapshot_ReturnsImageAndTree()
    {
        var result = await _client.CallToolAsync("Snapshot", new Dictionary<string, object?>
        {
            ["use_annotation"] = true
        });

        _output.WriteLine($"Content blocks: {result.Content.Count}");

        var hasImage = result.Content.OfType<ImageContentBlock>().Any();
        var hasText  = result.Content.OfType<TextContentBlock>().Any();

        _output.WriteLine($"Has image: {hasImage}");
        _output.WriteLine($"Has text:  {hasText}");

        var textContent = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        _output.WriteLine($"Text preview (200): {textContent[..Math.Min(200, textContent.Length)]}");

        Assert.True(hasImage, "Snapshot should return an image content block");
        Assert.True(hasText,  "Snapshot should return a text content block with the UI tree");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Snapshot_NoAnnotation_ReturnsImageAndTree()
    {
        // The C# Snapshot tool has use_annotation (bool) and display (int?) parameters.
        // use_annotation=false means no overlay labels, but both image and text tree are always returned.
        var result = await _client.CallToolAsync("Snapshot", new Dictionary<string, object?>
        {
            ["use_annotation"] = false
        });

        _output.WriteLine($"Content blocks: {result.Content.Count}");

        var hasImage = result.Content.OfType<ImageContentBlock>().Any();
        var hasText  = result.Content.OfType<TextContentBlock>().Any();

        _output.WriteLine($"Has image: {hasImage}");
        _output.WriteLine($"Has text:  {hasText}");

        // Both image and text are always present regardless of annotate flag
        Assert.True(hasImage, "Snapshot (no annotation) should still return an image");
        Assert.True(hasText,  "Snapshot (no annotation) should still return the UI tree text");

        // Verify image is a valid PNG
        var imageBytes = result.Content.OfType<ImageContentBlock>()
            .Select(c => c.DecodedData.ToArray())
            .FirstOrDefault();

        Assert.NotNull(imageBytes);
        Assert.True(imageBytes!.Length > 1000);
        Assert.Equal(0x89, imageBytes[0]);
        Assert.Equal(0x50, imageBytes[1]);
        Assert.Equal(0x4E, imageBytes[2]);
        Assert.Equal(0x47, imageBytes[3]);
    }
}
