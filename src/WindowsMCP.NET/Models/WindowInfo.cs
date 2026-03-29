namespace WindowsMcpNet.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    uint ProcessId,
    int X, int Y,
    int Width, int Height,
    bool IsVisible);
