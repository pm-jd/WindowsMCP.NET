using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Models;

/// <summary>One condition of an Expect call.</summary>
/// <param name="Index">1-based position in the call's <c>conditions</c> list, as used in every message.</param>
public abstract record Expectation(int Index);

/// <summary>A top-level window with this title is open (optionally: is / is not modal) or closed.</summary>
public sealed record WindowExpectation(int Index, string Title, ExpectWindowState State, bool? Modal, ExpectMatch Match)
    : Expectation(Index);

/// <summary>
/// An element — named by its Observe id (<paramref name="Id"/>) or described by a selector — is in
/// <paramref name="State"/> and, when given, has the value. With an id the selector fields are null.
/// </summary>
public sealed record ElementExpectation(
    int Index, string? Id, string? Type, string? Name, string? Panel, string? InWindow,
    ExpectState State, string? Value, string? ValueContains, ExpectMatch Match) : Expectation(Index);

/// <summary>A static text of the observation contains this string.</summary>
public sealed record TextExpectation(int Index, string Text) : Expectation(Index);

/// <summary>The answer to one condition.</summary>
/// <param name="Condition">The condition as one line.</param>
/// <param name="Actual">What was found, or why the answer is unknown.</param>
/// <param name="ElementIds">Ids of the matched elements, at most five.</param>
public sealed record ConditionOutcome(int Index, ExpectResult Result, string Condition, string Actual, IReadOnlyList<string> ElementIds);

/// <summary>A process whose windows an Expect call looked at.</summary>
public sealed record ObservedProcess(string Process, int Pid)
{
    public override string ToString() => $"{Process} (pid {Pid})";
}

/// <summary>Result of an Expect call: the last evaluation and how long it took to get there.</summary>
public sealed record ExpectOutcome(ExpectResult Result, IReadOnlyList<ConditionOutcome> Conditions, int Observations, long ElapsedMs, bool TimedOut)
{
    /// <summary>The processes of the windows in the last observation — what the answers are about.
    /// Empty when nothing was observed.</summary>
    public IReadOnlyList<ObservedProcess> Observed { get; init; } = [];
}
