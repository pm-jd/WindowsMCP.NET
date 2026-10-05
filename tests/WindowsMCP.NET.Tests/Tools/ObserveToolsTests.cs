using System.ComponentModel;
using System.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ObserveToolsTests
{
    private static ObservationService NewObservationService() =>
        new(NullLogger<ObservationService>.Instance);

    [Fact]
    public void Observe_ProcessScopeWithoutName_ReturnsError()
    {
        using var observation = NewObservationService();
        var store = new ObservationStore();
        var capture = new ScreenCaptureService();

        var result = ObserveTools.Observe(observation, store, capture,
            scope: ObserveScope.Process, process: null, ct: TestContext.Current.CancellationToken).Text();

        Assert.StartsWith("[ERROR] ArgumentException", result);
    }

    [Fact]
    public void Observe_UnknownProcess_ReturnsErrorNamingProcess()
    {
        using var observation = NewObservationService();
        var store = new ObservationStore();
        var capture = new ScreenCaptureService();

        var result = ObserveTools.Observe(observation, store, capture,
            scope: ObserveScope.Process, process: "no_such_process_xyz", ct: TestContext.Current.CancellationToken).Text();

        Assert.Contains("no_such_process_xyz", result);
    }

    [Fact]
    public void AppendScreenshot_CaptureFails_KeepsTheObservation_AndSaysWhy()
    {
        // A minimized RDP session has no desktop to copy from: the observation itself is still good.
        var result = ToolHelpers.TextResult("## Main  (app, pid 1)");

        ObserveTools.AppendScreenshot(result, () => throw new InvalidOperationException("The handle is invalid."));

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("## Main  (app, pid 1)", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("screenshot failed: InvalidOperationException: The handle is invalid.",
            Assert.IsType<TextContentBlock>(result.Content[1]).Text);
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void AppendScreenshot_CaptureSucceeds_AppendsOneJpegBlock()
    {
        var result = ToolHelpers.TextResult("## Main  (app, pid 1)");

        ObserveTools.AppendScreenshot(result, () => ([0xFF, 0xD8, 0xFF, 0xD9], null));

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("image/jpeg", Assert.IsType<ImageContentBlock>(result.Content[1]).MimeType);
    }

    [Fact]
    public void AppendScreenshot_CompositedCapture_AddsTheNoteAfterTheJpeg()
    {
        // Session without a display: the picture is assembled from the windows and the caller has to know that.
        var result = ToolHelpers.TextResult("## Main  (app, pid 1)");

        ObserveTools.AppendScreenshot(result, () => ([0xFF, 0xD8, 0xFF, 0xD9], "Composited screenshot: 2 windows"));

        Assert.Equal(3, result.Content.Count);
        Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal("Composited screenshot: 2 windows", Assert.IsType<TextContentBlock>(result.Content[2]).Text);
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void ScreenshotRegion_AllWindowsUnreadable_StillCoversThem()
    {
        // UI Automation did not answer for any window: the screenshot is all the caller can get, so the
        // region must come from the windows' (Win32) rectangles alone.
        var windows = new[]
        {
            new ObservedWindow(1, "Öffnen", "#32770", "MCS", 7, true, true, new Rectangle(600, 300, 800, 500)) { Unreadable = true },
            new ObservedWindow(2, "MCS - service", "Main", "MCS", 7, false, false, new Rectangle(-8, -8, 2576, 1416)) { Unreadable = true },
        };

        var region = ObserveTools.ScreenshotRegion(windows, virtualScreen: new Rectangle(0, 0, 2560, 1440));

        Assert.Equal(new Rectangle(0, 0, 2560, 1408), region);
    }

    [Fact]
    public void ScreenshotRegion_NoWindows_IsEmpty() =>
        Assert.True(ObserveTools.ScreenshotRegion([], new Rectangle(0, 0, 2560, 1440)).IsEmpty);

    [Fact]
    public void Observe_Description_DocumentsElementIdsAndJsonShape()
    {
        var method = typeof(ObserveTools).GetMethod(nameof(ObserveTools.Observe))!;
        var attr = (DescriptionAttribute)method.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];

        Assert.Contains("element", attr.Description);
        Assert.Contains("signature", attr.Description);
        Assert.Contains("truncated", attr.Description);
    }
}
