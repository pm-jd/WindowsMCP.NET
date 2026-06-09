using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WindowsMcpNet.Setup;

/// <summary>
/// Manages autostart registration. Prefers an elevated Windows Task Scheduler
/// entry (so the server can run with highest privileges, required to automate
/// elevated windows). Creating a <c>HighestAvailable</c> task itself needs
/// elevation, so when the process is not elevated it falls back to the per-user
/// HKCU <c>Run</c> key, which registers reliably without admin rights.
/// All failures are surfaced (console + <c>autostart.log</c>) instead of being
/// swallowed, so a failed registration is diagnosable remotely.
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "WindowsMCP.NET";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WindowsMCP.NET";

    public static bool Enable()
    {
        var exePath = CurrentExePath();

        // Already elevated (started via the task or the elevated helper):
        // create the HighestAvailable task directly.
        if (IsElevated())
            return EnableElevated(exePath);

        // Not elevated: if an elevated install already registered the task,
        // leave it in place rather than adding a duplicate mechanism.
        if (ScheduledTaskExists())
        {
            Log("Autostart already enabled via existing scheduled task; left unchanged.");
            return true;
        }

        // Self-elevate once (single UAC prompt) so the task can launch the
        // server with admin rights at logon WITHOUT any prompt thereafter.
        if (TryRelaunchElevated("--autostart-register"))
        {
            Log("Autostart enabled (elevated scheduled task — app will run as admin at logon).");
            return true;
        }

        // Elevation declined/unavailable → non-elevated fallback. The app will
        // then start automatically at logon but WITHOUT admin rights.
        Log("Elevation declined/unavailable; falling back to per-user Run key (app will NOT run as admin).");
        return SetRunKey(exePath);
    }

    /// <summary>Creates the elevated task. Must be called from an elevated context.</summary>
    private static bool EnableElevated(string exePath)
    {
        // A non-elevated 'schtasks /Create /RL HIGHEST' returns "Access is
        // denied"; here we are elevated, so the create succeeds.
        if (CreateScheduledTask(exePath))
        {
            RemoveRunKey(); // avoid a second, duplicate launch mechanism
            Log("Autostart enabled (scheduled task, elevated).");
            return true;
        }

        Log("Could not create scheduled task despite elevation; falling back to Run key.");
        return SetRunKey(exePath);
    }

    public static bool Disable()
    {
        var runRemoved = RemoveRunKey();

        if (!ScheduledTaskExists())
        {
            if (!runRemoved)
                Log("Autostart disable: Run key removal failed.");
            else
                Log("Autostart disabled.");
            return runRemoved;
        }

        // A scheduled task exists; removing it requires elevation.
        var taskRemoved = IsElevated()
            ? DeleteScheduledTask()
            : TryRelaunchElevated("--autostart-unregister");

        if (!taskRemoved)
            Log("Could not remove scheduled task (elevation declined/unavailable). Run as administrator to disable it.");

        var ok = runRemoved && taskRemoved;
        Log(ok ? "Autostart disabled." : "Autostart only partially disabled — see warnings above.");
        return ok;
    }

    public static bool IsEnabled() => ScheduledTaskExists() || RunKeyExists();

    /// <summary>
    /// Entry point for the elevated self-relaunch (CLI: <c>--autostart-register</c>
    /// / <c>--autostart-unregister</c>). Returns a process exit code (0 = success).
    /// </summary>
    public static int RunElevatedCommand(string arg)
    {
        var exePath = CurrentExePath();
        return arg switch
        {
            "--autostart-register" => EnableElevated(exePath) ? 0 : 1,
            "--autostart-unregister" => DeleteScheduledTask() ? 0 : 1,
            _ => 1,
        };
    }

    private static bool TryRelaunchElevated(string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = CurrentExePath(),
                Arguments = arg,
                UseShellExecute = true, // required for the "runas" verb
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            if (!process.WaitForExit(30000))
            {
                Log("Elevated autostart helper timed out.");
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user dismissed the UAC prompt.
            Log("Elevation was declined by the user (UAC cancelled).");
            return false;
        }
        catch (Exception ex)
        {
            Log($"TryRelaunchElevated error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ----- mechanism: scheduled task -----

    private static bool ScheduledTaskExists() =>
        RunSchtasks($"/Query /TN \"{TaskName}\"").ExitCode == 0;

    private static bool DeleteScheduledTask() =>
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F").ExitCode == 0;

    private static bool CreateScheduledTask(string exePath)
    {
        string? xmlPath = null;
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-32-544";
            xmlPath = Path.Combine(
                Path.GetTempPath(), $"WindowsMCP.NET.autostart.{Environment.ProcessId}.xml");

            // schtasks requires the task XML to be UTF-16 encoded.
            File.WriteAllText(xmlPath, BuildTaskXml(exePath, sid), Encoding.Unicode);

            var result = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            if (result.ExitCode != 0)
                Log($"schtasks /Create failed (exit {result.ExitCode}): {result.Error.Trim()}");
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log($"CreateScheduledTask error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (xmlPath is not null)
            {
                try { File.Delete(xmlPath); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Hardened task definition: no battery gating (the USV/EFEM landmine),
    /// no execution-time limit, and "start when available" so a missed logon
    /// trigger is caught up. Public for unit testing.
    /// </summary>
    public static string BuildTaskXml(string exePath, string userSid)
    {
        var cmd = SecurityElement.Escape(exePath);
        var sid = SecurityElement.Escape(userSid);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <URI>\{TaskName}</URI>
                <Description>WindowsMCP.NET autostart</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{cmd}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    // ----- mechanism: per-user Run key (non-elevated fallback) -----

    private static bool RunKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool SetRunKey(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            Log($"SetRunKey error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(RunValueName) is null)
                return true;
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            Log($"RemoveRunKey error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ----- helpers -----

    private static string CurrentExePath() =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(AppContext.BaseDirectory, "WindowsMCP.NET.exe");

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct SchtasksResult(int ExitCode, string Error);

    private static SchtasksResult RunSchtasks(string arguments)
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
            if (process is null)
                return new SchtasksResult(1, "Failed to start schtasks.exe");

            // Drain both pipes to completion (schtasks output is tiny) before
            // waiting, then check the WaitForExit result BEFORE touching
            // ExitCode — reading ExitCode on a still-running process throws.
            _ = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(10000))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return new SchtasksResult(1, "schtasks timed out after 10s");
            }

            return new SchtasksResult(process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            return new SchtasksResult(1, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine($"  {message}");
        try
        {
            // Persistent, greppable trail (does not match the per-process
            // 'WindowsMCP.NET.*.log' cleanup glob, so it survives restarts).
            var logPath = Path.Combine(AppContext.BaseDirectory, "autostart.log");
            File.AppendAllText(
                logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            /* best effort */
        }
    }
}
