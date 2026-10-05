using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// The wait loop of the Expect tool (design spec §1.3) with a scripted sequence of observations and a
/// clock that only moves when the test says so — no UI Automation, no real waiting.
/// </summary>
public class ExpectToolsTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 10, HitMs: 5, TotalMs: 15);

    /// <summary>A clock advanced by hand: by the scripted observations and by the loop's pauses.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private static Observation WithWindows(params string[] titles) =>
        new(titles.Select(t => new ObservedWindow(1, t, "Win", "app.exe", 1, false, false, new Rectangle(0, 0, 10, 10))).ToList(),
            null, [], [], "sig", false, Timings);

    private static readonly Observation Without = WithWindows("Main");
    private static readonly Observation WithLogin = WithWindows("Login", "Main");

    private static readonly IReadOnlyList<Expectation> LoginOpen =
        [new WindowExpectation(1, "Login", ExpectWindowState.Open, null, ExpectMatch.Exact)];

    private sealed class Harness
    {
        private readonly Queue<Observation> _script;
        private readonly Observation _last;

        public Harness(params Observation[] script)
        {
            _script = new Queue<Observation>(script);
            _last = script[^1];
        }

        public ManualTime Time { get; } = new();
        public TimeSpan ObservationTakes { get; init; } = TimeSpan.FromMilliseconds(100);
        public List<Observation> Remembered { get; } = [];
        public List<TimeSpan> Pauses { get; } = [];
        public int Observed { get; private set; }

        public Task<ExpectOutcome> Run(int timeoutMs, CancellationToken ct = default) =>
            ExpectTools.RunAsync(
                LoginOpen,
                () =>
                {
                    Observed++;
                    Time.Advance(ObservationTakes);
                    return _script.Count > 0 ? _script.Dequeue() : _last;
                },
                Remembered.Add,
                timeoutMs,
                Time,
                (delay, _) =>
                {
                    Pauses.Add(delay);
                    Time.Advance(delay);
                    return Task.CompletedTask;
                },
                ct);
    }

    [Fact]
    public async Task Run_PassOnFirstObservation_OneObservation_NotTimedOut()
    {
        var harness = new Harness(WithLogin);

        var outcome = await harness.Run(timeoutMs: 5000, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.Equal(1, outcome.Observations);
        Assert.False(outcome.TimedOut);
        Assert.Equal(100, outcome.ElapsedMs);
        Assert.Empty(harness.Pauses);
    }

    [Fact]
    public async Task Run_NoTimeout_Fail_OneObservation_NotTimedOut()
    {
        var harness = new Harness(Without);

        var outcome = await harness.Run(timeoutMs: 0, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.Equal(1, outcome.Observations);
        Assert.False(outcome.TimedOut);
        Assert.Empty(harness.Pauses);
        Assert.Equal(ExpectResult.Fail, Assert.Single(outcome.Conditions).Result);
    }

    [Fact]
    public async Task Run_PassOnThirdObservation()
    {
        var harness = new Harness(Without, Without, WithLogin);

        var outcome = await harness.Run(timeoutMs: 5000, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectResult.Pass, outcome.Result); // a fail does not end the wait
        Assert.Equal(3, outcome.Observations);
        Assert.False(outcome.TimedOut);
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)], harness.Pauses);
        Assert.Equal(700, outcome.ElapsedMs); // 3 observations of 100 ms + 2 pauses of 200 ms
    }

    [Fact]
    public async Task Run_TimesOut_ReturnsLastEvaluation()
    {
        var harness = new Harness(Without) { ObservationTakes = TimeSpan.FromMilliseconds(300) };

        var outcome = await harness.Run(timeoutMs: 1000, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.True(outcome.TimedOut);
        Assert.Equal(2, outcome.Observations); // 300, pause to 500, 800 — another pause would reach the limit
        Assert.Equal(800, outcome.ElapsedMs);
        Assert.Single(harness.Pauses);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Run_TimeoutShorterThanAnObservation_OneObservation(bool passes, bool expectedTimedOut)
    {
        var harness = new Harness(passes ? WithLogin : Without);

        var outcome = await harness.Run(timeoutMs: 1, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Observations);
        Assert.Equal(expectedTimedOut, outcome.TimedOut);
        Assert.Empty(harness.Pauses);
    }

    [Fact]
    public async Task Run_RemembersEveryObservation()
    {
        var harness = new Harness(Without, WithLogin);

        await harness.Run(timeoutMs: 5000, TestContext.Current.CancellationToken);

        Assert.Equal([Without, WithLogin], harness.Remembered);
    }

    [Fact]
    public async Task Run_Cancelled_Throws_WithoutObserving()
    {
        var harness = new Harness(Without);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Run(timeoutMs: 5000, cts.Token));

        Assert.Equal(0, harness.Observed);
    }

    [Fact]
    public async Task Run_ObserveThrows_Propagates()
    {
        var time = new ManualTime();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExpectTools.RunAsync(
            LoginOpen, () => throw new InvalidOperationException("no desktop"), _ => { }, 5000, time,
            (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(2500, 2500)]
    [InlineData(30_000, 30_000)]
    [InlineData(120_000, 30_000)]
    public void ClampTimeout(int requested, int expected) =>
        Assert.Equal(expected, ExpectTools.ClampTimeout(requested));
}
