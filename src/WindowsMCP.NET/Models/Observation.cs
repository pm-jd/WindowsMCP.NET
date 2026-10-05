using System.Drawing;

namespace WindowsMcpNet.Models;

/// <summary>A top-level window captured by Observe.</summary>
public sealed record ObservedWindow(nint Handle, string Title, string ClassName, string Process, int Pid, bool Foreground, bool Modal, Rectangle Rect)
{
    /// <summary>The window did not answer UI Automation (its root or subtree could not be read), so it
    /// has no nodes; title and rectangle come from Win32. It is reported instead of being left out: an
    /// observation is never silently empty.</summary>
    public bool Unreadable { get; init; }
}

/// <summary>
/// A raw node from the UI-tree walk, before filtering down to <see cref="ObservedElement"/>s.
/// </summary>
/// <param name="Parent">Index of the parent <see cref="ObservedNode"/> in the same observation, or null for a root.</param>
/// <param name="Window">Index into the observation's <see cref="ObservedWindow"/> list.</param>
/// <param name="HitVisible">Result of hit-testing the element's midpoint; null means it wasn't hit-tested.</param>
public sealed record ObservedNode(int Index, int? Parent, int Depth, int Window, string ControlType, string Name, string AutomationId, Rectangle Rect, bool Enabled, bool Focused, string? Value, string? Toggle, bool Selected, string? Expand, bool? HitVisible)
{
    /// <summary>The control is a password field (UIA <c>IsPassword</c>). Its value is never collected,
    /// emitted or hashed into the signature.</summary>
    public bool Password { get; init; }

    /// <summary>The hit-test found the node's centre in another top-level window than the one it was
    /// collected in (Win32 pre-check): it is drawn there — an item of an open drop-down, a control of an
    /// owned dialog — and listed in that window's own block. Such a node is never visible here; the
    /// tab-item exemption for UIA hit-tests that miss does not apply to it.</summary>
    public bool InOtherWindow { get; init; }

    /// <summary>The control answered the Value property (even with an empty string). False for a
    /// control without a readable value — then a missing <see cref="Value"/> does not mean "empty".</summary>
    public bool ValueReported { get; init; }

    /// <summary>The control answered SelectionItem.IsSelected. False for a control without that
    /// pattern — then <see cref="Selected"/> = false does not mean "not selected".</summary>
    public bool SelectionReported { get; init; }
}

/// <summary>An actionable element surfaced to the caller, with the locator needed to act on it later.</summary>
public sealed record ObservedElement(string Id, string Type, string Name, string? Label, string? Panel, string? Window, string? Value, string? Toggle, bool Selected, string? Expand, bool Enabled, Rectangle Rect, ElementLocator Locator)
{
    /// <summary>Handle of the top-level window the element was observed in. Not part of the locator
    /// (and so not of the id, except to tell identical locators apart): it gives the id its window
    /// affinity when it is resolved later.</summary>
    public nint WindowHandle { get; init; }

    /// <summary>True when the element is NOT in the bottom-most (main) window of its process in that
    /// observation — a popup, menu or dialog. Ids of transient elements die with their window.</summary>
    public bool Transient { get; init; }

    /// <summary>The element is a password field: its value is never collected, so a missing
    /// <see cref="Value"/> says nothing about its content (Expect answers a value condition on it with
    /// unknown). Not emitted by Observe and not part of the signature.</summary>
    public bool Password { get; init; }

    /// <summary>See <see cref="ObservedNode.ValueReported"/>. Not emitted by Observe, not in the signature.</summary>
    public bool ValueReported { get; init; }

    /// <summary>See <see cref="ObservedNode.SelectionReported"/>. Not emitted by Observe, not in the signature.</summary>
    public bool SelectionReported { get; init; }
}

/// <summary>Timing breakdown for one Observe call, in milliseconds.</summary>
public sealed record ObservationTimings(long WalkMs, long HitMs, long TotalMs);

/// <summary>Result of an Observe call: the windows in scope, the actionable elements found, and bookkeeping.</summary>
public sealed record Observation(IReadOnlyList<ObservedWindow> Windows, string? FocusId, IReadOnlyList<ObservedElement> Elements, IReadOnlyList<string> Texts, string Signature, bool Truncated, ObservationTimings Timings)
{
    /// <summary>Number of actionable elements that were found but not listed because of
    /// <c>max_elements</c> (0 when nothing was cut there; <see cref="Truncated"/> may still be set by
    /// the time budget or the texts cap).</summary>
    public int Omitted { get; init; }

    /// <summary>The collection ran out of its time budget: windows reached after that are missing
    /// from <see cref="Windows"/> and nodes left without a hit-test are listed although they may be
    /// hidden. <see cref="Truncated"/> is set too; this flag tells the cause apart, because then even
    /// what IS listed proves nothing about what is visible.</summary>
    public bool BudgetExceeded { get; init; }

    /// <summary>Set by Expect when this is not an observation of what the caller asked about (nothing
    /// in scope, the foreground application changed during a wait): the reason. Every condition is
    /// then answered unknown.</summary>
    public string? Unobserved { get; init; }
}
