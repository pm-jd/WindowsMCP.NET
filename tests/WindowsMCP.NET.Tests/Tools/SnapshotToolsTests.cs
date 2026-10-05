using ModelContextProtocol.Protocol;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// A composited screenshot (session without a display) must reach the caller together with the note saying what
/// the picture lacks; a normal screenshot stays a single image block as before.
/// </summary>
public class SnapshotToolsTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void ImageBlocks_NormalScreenshot_IsJustTheImage()
    {
        var blocks = SnapshotTools.ImageBlocks(Png, note: null);

        var image = Assert.IsType<ImageContentBlock>(Assert.Single(blocks));
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void ImageBlocks_CompositedScreenshot_AddsTheNoteAfterTheImage()
    {
        var blocks = SnapshotTools.ImageBlocks(Png, note: "Composited screenshot: 3 windows");

        Assert.Equal(2, blocks.Count);
        Assert.IsType<ImageContentBlock>(blocks[0]);
        Assert.Equal("Composited screenshot: 3 windows", Assert.IsType<TextContentBlock>(blocks[1]).Text);
    }
}
