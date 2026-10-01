using System.Drawing;

namespace WindowsMcpNet.Models;

/// <summary>A top-level window captured by Observe.</summary>
public sealed record ObservedWindow(nint Handle, string Title, string ClassName, string Process, int Pid, bool Foreground, bool Modal, Rectangle Rect);

/// <summary>
/// A raw node from the UI-tree walk, before filtering down to <see cref="ObservedElement"/>s.
/// </summary>
/// <param name="Parent">Index of the parent <see cref="ObservedNode"/> in the same observation, or null for a root.</param>
/// <param name="Window">Index into the observation's <see cref="ObservedWindow"/> list.</param>
/// <param name="HitVisible">Result of hit-testing the element's midpoint; null means it wasn't hit-tested.</param>
public sealed record ObservedNode(int Index, int? Parent, int Depth, int Window, string ControlType, string Name, string AutomationId, Rectangle Rect, bool Enabled, bool Focused, string? Value, string? Toggle, bool Selected, string? Expand, bool? HitVisible);

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
}

/// <summary>Timing breakdown for one Observe call, in milliseconds.</summary>
public sealed record ObservationTimings(long WalkMs, long HitMs, long TotalMs);

/// <summary>Result of an Observe call: the windows in scope, the actionable elements found, and bookkeeping.</summary>
public sealed record Observation(IReadOnlyList<ObservedWindow> Windows, string? FocusId, IReadOnlyList<ObservedElement> Elements, IReadOnlyList<string> Texts, string Signature, bool Truncated, ObservationTimings Timings);
