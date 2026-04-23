using FuzzySharp;
using WindowsMcpNet.Models;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

public sealed class DesktopService
{
    private readonly ILogger<DesktopService> _logger;

    public DesktopService(ILogger<DesktopService> logger)
    {
        _logger = logger;
    }

    public List<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd)) return true;

            var titleBuf = new char[256];
            var titleLen = User32.GetWindowTextW(hWnd, titleBuf, 256);
            if (titleLen == 0) return true;

            var title = new string(titleBuf, 0, titleLen);
            User32.GetWindowThreadProcessId(hWnd, out var pid);
            User32.GetWindowRect(hWnd, out var rect);

            windows.Add(new WindowInfo(
                Handle: hWnd,
                Title: title,
                ProcessId: pid,
                X: rect.Left, Y: rect.Top,
                Width: rect.Right - rect.Left,
                Height: rect.Bottom - rect.Top,
                IsVisible: true));
            return true;
        }, nint.Zero);
        return windows;
    }

    public WindowInfo? GetForegroundWindow()
    {
        var hWnd = User32.GetForegroundWindow();
        if (hWnd == nint.Zero) return null;

        var titleBuf = new char[256];
        var titleLen = User32.GetWindowTextW(hWnd, titleBuf, 256);
        var title = titleLen > 0 ? new string(titleBuf, 0, titleLen) : "";

        User32.GetWindowThreadProcessId(hWnd, out var pid);
        User32.GetWindowRect(hWnd, out var rect);

        return new WindowInfo(hWnd, title, pid,
            rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top, true);
    }

    /// <summary>
    /// Enumerates all visible top-level windows with their owning process names,
    /// then delegates matching to ProcessWindowMatcher.
    /// Preserves Z-order (EnumWindows returns top-to-bottom).
    /// </summary>
    public List<(WindowInfo Window, string ProcessName)> FindMatches(string name)
    {
        var windows = ListWindows();  // Z-ordered via EnumWindows
        var pidToName = new Dictionary<uint, string>();

        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                pidToName[(uint)proc.Id] = proc.ProcessName;
            }
            catch { /* access denied — skip */ }
            finally { proc.Dispose(); }
        }

        var candidates = windows
            .Where(w => pidToName.ContainsKey(w.ProcessId))
            .Select(w => (Process: new ProcessSnapshot(w.ProcessId, pidToName[w.ProcessId]),
                          Window: w));

        return ProcessWindowMatcher.Match(candidates, name);
    }

    /// <summary>
    /// Brings a window to the foreground using the AttachThreadInput Win32 workaround
    /// to bypass foreground-lock restrictions common in remote/automation scenarios.
    /// Returns true if SetForegroundWindow succeeded; false indicates a soft failure
    /// (window may still have flashed in taskbar).
    /// </summary>
    public bool BringToForeground(nint hWnd)
    {
        if (User32.IsIconic(hWnd))
            User32.ShowWindow(hWnd, User32.SW_RESTORE);

        uint currentThread = Kernel32.GetCurrentThreadId();
        uint targetThread  = User32.GetWindowThreadProcessId(hWnd, out _);

        if (currentThread == targetThread)
        {
            return User32.SetForegroundWindow(hWnd);
        }

        bool attached = User32.AttachThreadInput(currentThread, targetThread, true);
        try
        {
            bool ok = User32.SetForegroundWindow(hWnd);
            User32.BringWindowToTop(hWnd);
            return ok;
        }
        finally
        {
            if (attached)
                User32.AttachThreadInput(currentThread, targetThread, false);
        }
    }

    public WindowInfo? SwitchToWindow(string name)
    {
        var windows = ListWindows();
        var best = windows
            .Select(w => (Window: w, Score: Fuzz.PartialRatio(name.ToLowerInvariant(), w.Title.ToLowerInvariant())))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best.Score < 60) return null;

        var hWnd = best.Window.Handle;
        User32.ShowWindow(hWnd, User32.SW_RESTORE);
        User32.SetForegroundWindow(hWnd);
        _logger.LogInformation("Switched to window: {Title}", best.Window.Title);
        return best.Window;
    }

    public bool ResizeWindow(nint hWnd, int x, int y, int width, int height)
    {
        return User32.MoveWindow(hWnd, x, y, width, height, true);
    }

    public async Task<WindowInfo?> LaunchApp(string name)
    {
        _logger.LogInformation("Launching app: {Name}", name);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = name,
            UseShellExecute = true,
        });

        if (process is null) return null;

        process.WaitForInputIdle(3000);
        await Task.Delay(500);

        var windows = ListWindows();
        return windows.FirstOrDefault(w => w.ProcessId == (uint)process.Id);
    }
}
