namespace WindowsMcpNet.Tools;

/// <summary>
/// Refuses to terminate processes whose death takes the session or this server down with it.
/// Mirrors <c>FileSystemTools.ValidateNotProtected</c>: cheap defence in depth for an
/// authenticated agent that is otherwise fully trusted.
/// </summary>
public static class ProcessGuard
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "fontdrvhost",
    };

    public static bool IsProtected(int pid, string? processName) =>
        pid <= 4
        || pid == Environment.ProcessId
        || (processName is not null && CriticalNames.Contains(processName));
}
