using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void Observe_Description_DocumentsElementIdsAndJsonShape()
    {
        var method = typeof(ObserveTools).GetMethod(nameof(ObserveTools.Observe))!;
        var attr = (DescriptionAttribute)method.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];

        Assert.Contains("element", attr.Description);
        Assert.Contains("signature", attr.Description);
        Assert.Contains("truncated", attr.Description);
    }
}
