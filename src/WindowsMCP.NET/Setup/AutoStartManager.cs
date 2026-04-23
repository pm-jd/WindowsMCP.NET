using System.Diagnostics;

namespace WindowsMcpNet.Setup;

public static class AutoStartManager
{
    private const string TaskName = "WindowsMCP.NET";

    public static bool Enable()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "WindowsMCP.NET.exe");

        // Remove existing task first (ignore errors)
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

        // Create new task: run at logon, highest privileges
        var result = RunSchtasks(
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");

        if (result == 0)
        {
            Console.WriteLine("  Autostart enabled (Windows Task Scheduler).");
            return true;
        }

        Console.Error.WriteLine("  Warning: Could not create scheduled task for autostart.");
        return false;
    }

    public static bool Disable()
    {
        var result = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        if (result == 0)
        {
            Console.WriteLine("  Autostart disabled.");
            return true;
        }

        Console.Error.WriteLine("  Warning: Could not remove scheduled task.");
        return false;
    }

    public static bool IsEnabled()
    {
        return RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode ?? 1;
        }
        catch
        {
            return 1;
        }
    }
}
