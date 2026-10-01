using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
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

        ObserveTools.AppendScreenshot(result, () => [0xFF, 0xD8, 0xFF, 0xD9]);

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("image/jpeg", Assert.IsType<ImageContentBlock>(result.Content[1]).MimeType);
    }

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
