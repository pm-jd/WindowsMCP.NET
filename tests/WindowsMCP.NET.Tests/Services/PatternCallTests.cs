using System.Diagnostics;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="PatternCall.Attempt"/> decides whether a UIA pattern call counts as dispatched. The only
/// "no" is "there is no such pattern"; a call that throws or hangs may have acted and must therefore
/// never be reported as not attempted (which would make the executor click a second time).
/// </summary>
public class PatternCallTests
{
    private sealed class FakePattern;

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    [Fact]
    public void Attempt_NoPattern_ReturnsFalse_WithoutCalling()
    {
        var called = false;

        var dispatched = PatternCall.Attempt<FakePattern>(() => null, _ => called = true, Generous);

        Assert.False(dispatched);
        Assert.False(called);
    }

    [Fact]
    public void Attempt_PatternLookupThrows_ReturnsFalse_WithoutCalling()
    {
        var called = false;

        var dispatched = PatternCall.Attempt<FakePattern>(
            () => throw new InvalidOperationException("element not available"), _ => called = true, Generous);

        Assert.False(dispatched);
        Assert.False(called);
    }

    [Fact]
    public void Attempt_CallCompletes_ReturnsTrue_AfterTheCallRan()
    {
        var pattern = new FakePattern();
        FakePattern? seen = null;

        var dispatched = PatternCall.Attempt(() => pattern, p => seen = p, Generous);

        Assert.True(dispatched);
        Assert.Same(pattern, seen);
    }

    [Fact]
    public void Attempt_CallThrows_StillCountsAsDispatched_AndDoesNotThrow()
    {
        var dispatched = PatternCall.Attempt(
            () => new FakePattern(), _ => throw new InvalidOperationException("provider failed after acting"), Generous);

        Assert.True(dispatched);
    }

    [Fact]
    public void Attempt_CallDoesNotReturn_CountsAsDispatched_AfterTheLimit()
    {
        // A WinForms Invoke that opened a modal dialog: the call only returns once the dialog is closed.
        var ct = TestContext.Current.CancellationToken;
        using var dialogClosed = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        var stopwatch = Stopwatch.StartNew();

        var dispatched = PatternCall.Attempt(
            () => new FakePattern(),
            _ =>
            {
                started.Set();
                dialogClosed.Wait(TimeSpan.FromSeconds(30));
                returned.Set();
            },
            TimeSpan.FromMilliseconds(200));

        var elapsed = stopwatch.Elapsed;
        var stillRunning = !returned.IsSet;
        try
        {
            Assert.True(dispatched);
            Assert.True(stillRunning, "Attempt must return while the call is still blocked");
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), ct), "the call must have been started");
            // Not before the limit (minus timer granularity), and far from the 30 s the call would block.
            Assert.InRange(elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        }
        finally
        {
            dialogClosed.Set();
            returned.Wait(TimeSpan.FromSeconds(10), ct); // let the call finish before the events are disposed
        }
    }

    [Fact]
    public void Attempt_AbandonedCallThatFailsLater_LeavesNoUnobservedTaskException()
    {
        const string Marker = "late failure of an abandoned pattern call (PatternCallTests)";
        var unobserved = false;
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.ToString().Contains(Marker, StringComparison.Ordinal))
                unobserved = true;
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            using var release = new ManualResetEventSlim();
            using var finished = new ManualResetEventSlim();

            RunAbandonedCall(release, finished, Marker);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            // Give the faulted task time to complete, then force its finalizer — that is when an
            // unobserved exception would be raised.
            Thread.Sleep(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    /// <summary>Separate frame so the test method keeps no reference to the abandoned task.</summary>
    private static void RunAbandonedCall(ManualResetEventSlim release, ManualResetEventSlim finished, string marker)
    {
        var dispatched = PatternCall.Attempt(
            () => new FakePattern(),
            _ =>
            {
                try
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                    throw new InvalidOperationException(marker);
                }
                finally
                {
                    finished.Set();
                }
            },
            TimeSpan.FromMilliseconds(50));

        Assert.True(dispatched);
    }
}
