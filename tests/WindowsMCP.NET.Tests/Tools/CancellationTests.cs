using System.Diagnostics;
using System.Text.Json;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// The MCP SDK binds a CancellationToken parameter to the client's request token. Long-running
/// tools must honour it: stop promptly and surface OperationCanceledException (which the SDK
/// maps to a cancelled request) instead of swallowing it into an "[ERROR]" string.
/// </summary>
public class CancellationTests
{
    [Fact]
    public async Task Wait_CancelledToken_ThrowsPromptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InputTools.Wait(10, cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task PowerShell_CancelledToken_KillsProcessAndThrowsPromptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SystemTools.PowerShell("Start-Sleep -Seconds 30", timeout: 60, ct: cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task PowerShell_Timeout_StillReportsTimeoutWhenTokenNotCancelled()
    {
        var result = await SystemTools.PowerShell("Start-Sleep -Seconds 30", timeout: 1, ct: CancellationToken.None);

        Assert.StartsWith("[TIMEOUT after 1s]", result);
    }

    [Fact]
    public async Task Perform_CancelledToken_StopsChainAndThrows()
    {
        // wait-only steps never touch the UI services, so they can be null here.
        var steps = JsonDocument.Parse("""[{"action":"wait","duration":5},{"action":"wait","duration":5}]""").RootElement;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PerformTools.Perform(null!, null!, null!, null!, null!, steps, snapshot_after: false, ct: cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }
}
