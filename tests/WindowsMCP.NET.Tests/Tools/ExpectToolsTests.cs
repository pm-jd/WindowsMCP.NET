using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// The wait loop of the Expect tool (design spec §1.3) with a scripted sequence of observations and a
/// clock that only moves when the test says so — no UI Automation, no real waiting — and the scope
/// guard that keeps a wait on the application it started with.
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

    private static Observation WithWindows(string process, int pid, params string[] titles) =>
        new(titles.Select(t => new ObservedWindow(1, t, "Win", process, pid, false, false, new Rectangle(0, 0, 10, 10))).ToList(),
            null, [], [], "sig", false, Timings);

    private static readonly Observation Without = WithWindows("app", 1, "Main");
    private static readonly Observation WithLogin = WithWindows("app", 1, "Login", "Main");

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
                resolveId: null,
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_PassOnFirstObservation_OneObservation_NotTimedOut()
    {
        var harness = new Harness(WithLogin);

        var outcome = await harness.Run(timeoutMs: 5000, Ct);

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

        var outcome = await harness.Run(timeoutMs: 0, Ct);

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

        var outcome = await harness.Run(timeoutMs: 5000, Ct);

        Assert.Equal(ExpectResult.Pass, outcome.Result); // a fail does not end the wait
        Assert.Equal(3, outcome.Observations);
        Assert.False(outcome.TimedOut);
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)], harness.Pauses);
        Assert.Equal(700, outcome.ElapsedMs); // 3 observations of 100 ms + 2 pauses of 200 ms
    }

    [Fact]
    public async Task Run_TimesOut_ReturnsLastEvaluation_ObservedAtTheDeadline()
    {
        var harness = new Harness(Without) { ObservationTakes = TimeSpan.FromMilliseconds(300) };

        var outcome = await harness.Run(timeoutMs: 1000, Ct);

        Assert.Equal(ExpectResult.Fail, outcome.Result);
        Assert.True(outcome.TimedOut);
        // 300 · pause to 500 · 800 · pause to 1000 (the limit) · one last look, done at 1300
        Assert.Equal(3, outcome.Observations);
        Assert.Equal(1300, outcome.ElapsedMs);
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)], harness.Pauses);
    }

    [Fact]
    public async Task Run_LastPauseIsCutToTheRemainingTime()
    {
        var harness = new Harness(Without);

        var outcome = await harness.Run(timeoutMs: 450, Ct);

        // 100 · pause 200 → 300 · 400 · pause 50 → 450 (the limit) · last look
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50)], harness.Pauses);
        Assert.Equal(3, outcome.Observations);
        Assert.True(outcome.TimedOut);
    }

    [Fact]
    public async Task Run_StateReachedJustBeforeTheLimit_IsStillSeen()
    {
        // With a 250 ms observation and a 400 ms limit the wait must not give up at 250 ms.
        var harness = new Harness(Without, WithLogin) { ObservationTakes = TimeSpan.FromMilliseconds(250) };

        var outcome = await harness.Run(timeoutMs: 400, Ct);

        Assert.Equal(ExpectResult.Pass, outcome.Result);
        Assert.False(outcome.TimedOut);
        Assert.Equal(2, outcome.Observations);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Run_TimeoutShorterThanAnObservation_OneObservation(bool passes, bool expectedTimedOut)
    {
        var harness = new Harness(passes ? WithLogin : Without);

        var outcome = await harness.Run(timeoutMs: 1, Ct);

        Assert.Equal(1, outcome.Observations);
        Assert.Equal(expectedTimedOut, outcome.TimedOut);
        Assert.Empty(harness.Pauses);
    }

    [Fact]
    public async Task Run_RemembersOnlyTheObservationTheResultIsAbout()
    {
        // Every remembered observation pushes an older one out of the store (8 are kept): a long wait
        // must not cost the caller the ids it observed before.
        var harness = new Harness(Without, Without, WithLogin);

        await harness.Run(timeoutMs: 5000, Ct);

        Assert.Equal([WithLogin], harness.Remembered);
    }

    [Fact]
    public async Task Run_NothingObserved_NothingRemembered()
    {
        var harness = new Harness(ScopeGuard.Nothing("there is no foreground application window"));

        var outcome = await harness.Run(timeoutMs: 0, Ct);

        Assert.Equal(ExpectResult.Unknown, outcome.Result);
        Assert.Empty(harness.Remembered);
        Assert.Empty(outcome.Observed);
    }

    [Fact]
    public async Task Run_ReportsWhatWasObserved()
    {
        var two = new Observation(
            [
                new ObservedWindow(1, "Login", "Win", "MCS", 2116, false, true, new Rectangle(0, 0, 10, 10)),
                new ObservedWindow(2, "MCS", "Win", "MCS", 2116, false, false, new Rectangle(0, 0, 10, 10)),
                new ObservedWindow(3, "Editor", "Win", "notepad", 77, false, false, new Rectangle(0, 0, 10, 10)),
            ],
            null, [], [], "sig", false, Timings);

        var outcome = await new Harness(two).Run(timeoutMs: 0, Ct);

        Assert.Equal([new ObservedProcess("MCS", 2116), new ObservedProcess("notepad", 77)], outcome.Observed);
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
            LoginOpen, () => throw new InvalidOperationException("no desktop"), _ => { }, null, 5000, time,
            (_, _) => Task.CompletedTask, Ct));
    }

    [Fact]
    public async Task Run_PassesTheIdResolverOn()
    {
        var locator = new ElementLocator("app", "Win", [new LocatorStep("Button", "ok", 0)]);
        var observation = new Observation(
            Without.Windows, null,
            [new ObservedElement("now", "Button", "OK", null, null, null, null, null, false, null, true, Rectangle.Empty, locator) { WindowHandle = 1 }],
            [], "sig", false, Timings);
        IReadOnlyList<Expectation> byId =
            [new ElementExpectation(1, "then", null, null, null, null, ExpectState.Enabled, null, null, ExpectMatch.Exact)];

        var outcome = await ExpectTools.RunAsync(
            byId, () => observation, _ => { }, id => id == "then" ? new StoredElement(locator, 1, false) : null, 0,
            new ManualTime(), (_, _) => Task.CompletedTask, Ct);

        Assert.Equal(ExpectResult.Pass, outcome.Result);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(2500, 2500)]
    [InlineData(30_000, 30_000)]
    [InlineData(120_000, 30_000)]
    public void ClampTimeout(int requested, int expected) =>
        Assert.Equal(expected, ExpectTools.ClampTimeout(requested));

    // ── scope guard ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guard_Foreground_SameApplication_PassesThrough()
    {
        var guard = new ScopeGuard(ObserveScope.Foreground);

        Assert.Same(Without, guard.Check(Without));
        Assert.Same(WithLogin, guard.Check(WithLogin));
    }

    [Fact]
    public void Guard_Foreground_AnotherApplicationTakesTheForeground_Unobserved_UntilTheFirstOneIsBack()
    {
        // "Progress window closed" must not pass because some other application came to the front.
        var guard = new ScopeGuard(ObserveScope.Foreground);
        var other = WithWindows("notepad", 77, "Editor");

        guard.Check(Without);
        var changed = guard.Check(other);

        Assert.Equal("the foreground application changed during the wait (was app (pid 1), now notepad (pid 77))", changed.Unobserved);
        Assert.Same(other.Windows, changed.Windows);
        Assert.Null(guard.Check(WithLogin).Unobserved);
    }

    [Theory]
    [InlineData(ObserveScope.Process)]
    [InlineData(ObserveScope.Desktop)]
    public void Guard_OtherScopes_DoNotPin(ObserveScope scope)
    {
        var guard = new ScopeGuard(scope);

        guard.Check(Without);

        Assert.Null(guard.Check(WithWindows("notepad", 77, "Editor")).Unobserved);
    }

    [Theory]
    [InlineData(ObserveScope.Foreground)]
    [InlineData(ObserveScope.Process)]
    [InlineData(ObserveScope.Desktop)]
    public void Guard_NoWindowObserved_Unobserved(ObserveScope scope)
    {
        // Every window gone or zero-sized between the scope check and the walk: nothing was seen,
        // so nothing is proven — "closed" and "absent" must not pass on that.
        var empty = new Observation([], null, [], [], "sig", false, Timings);

        Assert.Equal("no window was observed", new ScopeGuard(scope).Check(empty).Unobserved);
    }

    [Fact]
    public void Guard_OnlyMinimizedWindows_IsAnObservation()
    {
        // The application is there, merely minimised: "window open" must be answerable.
        var minimised = new Observation([], null, [], [], "sig", false, Timings) { MinimizedWindows = ["MCS"] };

        Assert.Null(new ScopeGuard(ObserveScope.Process).Check(minimised).Unobserved);
    }

    [Fact]
    public void Guard_Unavailable_NoForeground_IsUnobserved()
    {
        var observation = ScopeGuard.Unavailable(ObservationService.ScopeAvailability.NoForeground, null);

        Assert.Equal("there is no foreground application window", observation.Unobserved);
        Assert.Empty(observation.Windows);
    }

    [Fact]
    public void Guard_Unavailable_ProcessHasNoWindow_IsAProvenEmptyScope()
    {
        // scope=process asks about the windows of that process: when it has none, "window closed" and
        // "element absent" are proven, "open"/"exists" fail — waiting for an application to start or
        // to exit is what this is for.
        var observation = ScopeGuard.Unavailable(ObservationService.ScopeAvailability.ProcessHasNoWindow, "mcs");

        Assert.Null(observation.Unobserved);
        Assert.Empty(observation.Windows);
        var outcomes = ExpectationEvaluator.Evaluate(observation,
        [
            new WindowExpectation(1, "MCS", ExpectWindowState.Closed, null, ExpectMatch.Contains),
            new WindowExpectation(2, "MCS", ExpectWindowState.Open, null, ExpectMatch.Contains),
            new ElementExpectation(3, null, "Button", "Start", null, null, ExpectState.Absent, null, null, ExpectMatch.Exact),
            new ElementExpectation(4, null, "Button", "Start", null, null, ExpectState.Enabled, null, null, ExpectMatch.Exact),
        ]);
        Assert.Equal([ExpectResult.Pass, ExpectResult.Fail, ExpectResult.Pass, ExpectResult.Fail], outcomes.Select(o => o.Result));
    }
}
