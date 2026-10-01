namespace WindowsMcpNet.Services;

/// <summary>What became of one UIA pattern call (see <see cref="PatternCall.Attempt"/>).</summary>
public enum PatternCallResult
{
    /// <summary>The element has no such pattern: nothing was sent.</summary>
    NotSupported,

    /// <summary>The call was dispatched and came back within the limit — normally or by throwing.</summary>
    Returned,

    /// <summary>The call was dispatched and had not come back when the limit ran out: its handler is
    /// still running in the target application (typically it opened a modal dialog).</summary>
    StillRunning,
}

/// <summary>
/// Runs one UIA pattern call under a time limit and answers: was a call dispatched, and has it returned?
/// <para>
/// A pattern call that throws, or that does not return, may still have run in the target application —
/// WinForms <c>Invoke</c> on a button that opens a modal dialog blocks until the dialog is closed (or UIA
/// times out) although the click has long happened. Treating that as "failed" made the caller click
/// again with the mouse. So the only <see cref="PatternCallResult.NotSupported"/> here is "the element
/// has no such pattern" (nothing was sent); every dispatched call is reported as such and the caller
/// verifies the effect. A call that is still running is told apart from one that returned, because
/// while it runs the application answers no UI Automation request — the caller has to say so.
/// </para>
/// </summary>
internal static class PatternCall
{
    /// <summary>Limit for a pattern call before it is reported as dispatched-but-still-running.</summary>
    internal static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <see cref="PatternCallResult.NotSupported"/> when <paramref name="getPattern"/> yields no pattern
    /// (null, or the lookup itself throws — the element is gone or does not support it): nothing was
    /// sent. Otherwise runs <paramref name="call"/> on a pool thread and waits at most
    /// <paramref name="limit"/> for it: <see cref="PatternCallResult.Returned"/> when it completed or
    /// threw, <see cref="PatternCallResult.StillRunning"/> when it did neither. Never throws.
    /// </summary>
    public static PatternCallResult Attempt<TPattern>(Func<TPattern?> getPattern, Action<TPattern> call, TimeSpan limit)
        where TPattern : class
    {
        TPattern? pattern;
        try
        {
            pattern = getPattern();
        }
        catch (Exception)
        {
            return PatternCallResult.NotSupported;
        }

        if (pattern is null)
            return PatternCallResult.NotSupported;

        var task = Task.Run(() => call(pattern));

        // Observe a failure whenever it happens — also long after the limit — so a late exception of an
        // abandoned call never surfaces as an unobserved task exception.
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return task.Wait(limit) ? PatternCallResult.Returned : PatternCallResult.StillRunning;
        }
        catch (AggregateException)
        {
            // The call threw: it was dispatched and may have acted before failing.
            return PatternCallResult.Returned;
        }
    }
}
