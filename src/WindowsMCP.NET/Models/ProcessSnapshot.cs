namespace WindowsMcpNet.Models;

/// <summary>
/// Minimal projection of a running process, used by ProcessWindowMatcher
/// so matching logic can be unit-tested without touching real Process handles.
/// </summary>
public sealed record ProcessSnapshot(uint Pid, string Name);
