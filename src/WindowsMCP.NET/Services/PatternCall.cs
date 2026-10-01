namespace WindowsMcpNet.Services;

/// <summary>
/// Runs one UIA pattern call under a time limit and answers a single question: was a call dispatched?
/// <para>
/// A pattern call that throws, or that does not return, may still have run in the target application —
/// WinForms <c>Invoke</c> on a button that opens a modal dialog blocks until the dialog is closed (or UIA
/// times out) although the click has long happened. Treating that as "failed" made the caller click
/// again with the mouse. So the only <see langword="false"/> here is "the element has no such pattern"
/// (nothing was sent); every dispatched call is <see langword="true"/> and the caller verifies the effect.
/// </para>
/// </summary>
internal static class PatternCall
{
    /// <summary>Limit for a pattern call before it is reported as dispatched-but-still-running.</summary>
    internal static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <see langword="false"/> when <paramref name="getPattern"/> yields no pattern (null, or the lookup
    /// itself throws — the element is gone or does not support it): nothing was sent. Otherwise runs
    /// <paramref name="call"/> on a pool thread, waits at most <paramref name="limit"/> for it and
    /// returns <see langword="true"/> whatever happened to it. Never throws.
    /// </summary>
    public static bool Attempt<TPattern>(Func<TPattern?> getPattern, Action<TPattern> call, TimeSpan limit)
        where TPattern : class
    {
        TPattern? pattern;
        try
        {
            pattern = getPattern();
        }
        catch (Exception)
        {
            return false;
        }

        if (pattern is null)
            return false;

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
            task.Wait(limit);
        }
        catch (AggregateException)
        {
            // The call threw: it was dispatched and may have acted before failing.
        }

        return true;
    }
}
