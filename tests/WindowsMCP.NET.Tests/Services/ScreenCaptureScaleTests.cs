using System.Drawing;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ScreenCaptureService.ScaleToFit"/> is the pure sizing rule behind the Observe tool's
/// optional JPEG screenshot: it keeps the aspect ratio, caps the longest edge, and never enlarges an
/// already-small region.
/// </summary>
public class ScreenCaptureScaleTests
{
    [Fact]
    public void ScaleToFit_LimitsLongestEdge()
    {
        var result = ScreenCaptureService.ScaleToFit(new Size(3840, 2160), 1568);

        Assert.Equal(new Size(1568, 882), result);
    }

    [Fact]
    public void ScaleToFit_NeverUpscales()
    {
        var result = ScreenCaptureService.ScaleToFit(new Size(800, 600), 1568);

        Assert.Equal(new Size(800, 600), result);
    }
}
