using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// The SDK binds an IProgress&lt;ProgressNotificationValue&gt; parameter to the request's
/// progress token (works in stateless HTTP too). Perform reports one notification per step so
/// long chains show movement instead of a silent multi-second call.
/// </summary>
public class PerformProgressTests
{
    private sealed class Recorder : IProgress<ProgressNotificationValue>
    {
        public List<ProgressNotificationValue> Items { get; } = [];
        public void Report(ProgressNotificationValue value) => Items.Add(value);
    }

    [Fact]
    public async Task Perform_ReportsProgressAfterEachStep()
    {
        var steps = JsonDocument.Parse("""[{"action":"wait","duration":0},{"action":"wait","duration":0},{"action":"wait","duration":0}]""").RootElement;
        var recorder = new Recorder();

        await PerformTools.Perform(null!, null!, null!, null!, null!, steps, snapshot_after: false, delay_between_ms: 0,
            progress: recorder, ct: TestContext.Current.CancellationToken);

        Assert.Equal([1f, 2f, 3f], recorder.Items.Select(i => i.Progress));
        Assert.All(recorder.Items, i => Assert.Equal(3f, i.Total));
        Assert.Contains("Step 1/3", recorder.Items[0].Message);
    }

    [Fact]
    public async Task Perform_WithoutProgressSink_StillRuns()
    {
        var steps = JsonDocument.Parse("""[{"action":"wait","duration":0}]""").RootElement;

        var result = await PerformTools.Perform(null!, null!, null!, null!, null!, steps, snapshot_after: false,
            ct: TestContext.Current.CancellationToken);

        Assert.Contains(result.OfType<TextContentBlock>(), t => t.Text.Contains("Completed. 1/1 succeeded."));
    }
}
