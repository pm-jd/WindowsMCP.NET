using System.ComponentModel;
using System.Text;
using Microsoft.Win32;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "PowerShell", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Execute a PowerShell command on the remote machine.")]
    public static async Task<string> PowerShell(
        [Description("PowerShell command or script to execute")] string command,
        [Description("Timeout in seconds (default 30, max 120)")] int timeout = 30)
    {
        try
        {
            timeout = Math.Clamp(timeout, 1, 120);

            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                proc.Kill(entireProcessTree: true);
                return $"[TIMEOUT after {timeout}s]\n{stdout}{stderr}";
            }

            var result = new StringBuilder();
            if (stdout.Length > 0) result.Append(stdout);
            if (stderr.Length > 0)
            {
                if (result.Length > 0) result.AppendLine();
                result.Append("[stderr]\n").Append(stderr);
            }
            if (proc.ExitCode != 0)
                result.AppendLine($"[ExitCode={proc.ExitCode}]");

            return result.Length > 0 ? result.ToString().TrimEnd() : "(no output)";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Process", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("List running processes (mode=list) or kill a process by PID (mode=kill).")]
    public static string ProcessTool(
        [Description("Mode: list or kill")] string mode,
        [Description("Process ID to kill (required for mode=kill)")] int? pid = null,
        [Description("Filter by name substring (for mode=list)")] string? name = null,
        [Description("Sort list by: memory (default), cpu, name, pid")] string sort_by = "memory",
        [Description("Maximum number of processes to return in list mode")] int limit = 20,
        [Description("Force kill (SIGKILL / TerminateProcess) instead of graceful close")] bool force = false)
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "list" => ListProcesses(name, sort_by, limit),
                "kill" => KillProcess(pid, force),
                _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: list or kill.")
            };
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "Registry", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("Read, write, delete, or list Windows registry values. " +
                 "mode: get, set, delete, list. path: full key path like HKCU\\Software\\MyApp.")]
    public static string RegistryTool(
        [Description("Mode: get, set, delete, or list")] string mode,
        [Description("Registry key path, e.g. HKCU\\Software\\MyApp")] string path,
        [Description("Value name (required for get/set/delete)")] string? name = null,
        [Description("Value data to set (for mode=set)")] string? value = null,
        [Description("Value type for set: String (default), DWord, QWord, Binary, ExpandString")] string type = "String")
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "get"    => RegistryGet(path, name),
                "set"    => RegistrySet(path, name, value, type),
                "delete" => RegistryDelete(path, name),
                "list"   => RegistryList(path),
                _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: get, set, delete, or list.")
            };
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    // --- Process helpers ---

    private static string ListProcesses(string? nameFilter, string sortBy, int limit)
    {
        var processes = System.Diagnostics.Process.GetProcesses();
        try
        {
            var query = processes
                .Where(p =>
                {
                    try { return nameFilter is null || p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                });

            IOrderedEnumerable<System.Diagnostics.Process> sorted = sortBy.ToLowerInvariant() switch
            {
                "name"   => query.OrderBy(p => p.ProcessName),
                "pid"    => query.OrderBy(p => p.Id),
                "cpu"    => query.OrderByDescending(p => { try { return p.TotalProcessorTime.TotalSeconds; } catch { return 0; } }),
                _        => query.OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } }), // memory
            };

            var procs = sorted.Take(limit).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"{"PID",-8} {"Name",-30} {"Memory (MB)",12}");
            sb.AppendLine(new string('-', 55));
            foreach (var p in procs)
            {
                try
                {
                    long memMb = p.WorkingSet64 / (1024 * 1024);
                    sb.AppendLine($"{p.Id,-8} {p.ProcessName,-30} {memMb,12}");
                }
                catch
                {
                    sb.AppendLine($"{p.Id,-8} {p.ProcessName,-30} {"(access denied)",12}");
                }
            }
            sb.AppendLine($"\nShowing: {procs.Count} process(es)");
            return sb.ToString().TrimEnd();
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private static string KillProcess(int? pid, bool force)
    {
        if (!pid.HasValue)
            throw new ArgumentException("'pid' is required for mode=kill.");

        var proc = System.Diagnostics.Process.GetProcessById(pid.Value);
        var procName = proc.ProcessName;
        if (force)
        {
            proc.Kill(entireProcessTree: true);
        }
        else
        {
            proc.CloseMainWindow();
            if (!proc.WaitForExit(3000))
                proc.Kill();
        }
        return $"Killed process '{procName}' (PID={pid.Value})";
    }

    // --- Registry helpers ---

    private static (RegistryKey hive, string subKey) SplitRegistryPath(string key)
    {
        var sep = key.IndexOf('\\');
        string hiveName = sep < 0 ? key : key[..sep];
        string sub = sep < 0 ? "" : key[(sep + 1)..];

        RegistryKey hive = hiveName.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER"   => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE"  => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT"   => Registry.ClassesRoot,
            "HKU"  or "HKEY_USERS"          => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unknown registry hive: '{hiveName}'")
        };
        return (hive, sub);
    }

    private static string RegistryGet(string key, string? valueName)
    {
        var (hive, sub) = SplitRegistryPath(key);
        using var regKey = hive.OpenSubKey(sub)
            ?? throw new InvalidOperationException($"Registry key not found: {key}");

        var val = regKey.GetValue(valueName ?? "");
        return val is null ? "(null)" : val.ToString() ?? "(null)";
    }

    private static string RegistrySet(string key, string? valueName, string? data, string kind)
    {
        if (data is null)
            throw new ArgumentException("'value' is required for mode=set.");

        var (hive, sub) = SplitRegistryPath(key);
        using var regKey = hive.CreateSubKey(sub)
            ?? throw new InvalidOperationException($"Cannot create/open registry key: {key}");

        var vkind = kind.ToLowerInvariant() switch
        {
            "dword"        => RegistryValueKind.DWord,
            "qword"        => RegistryValueKind.QWord,
            "binary"       => RegistryValueKind.Binary,
            "expandstring" => RegistryValueKind.ExpandString,
            _              => RegistryValueKind.String,
        };

        object value = vkind switch
        {
            RegistryValueKind.DWord  => int.Parse(data),
            RegistryValueKind.QWord  => long.Parse(data),
            RegistryValueKind.Binary => Convert.FromHexString(data),
            _ => data,
        };

        regKey.SetValue(valueName ?? "", value, vkind);
        return $"Set {key}\\{valueName ?? "(default)"} = {data} ({vkind})";
    }

    private static string RegistryDelete(string key, string? valueName)
    {
        var (hive, sub) = SplitRegistryPath(key);
        using var regKey = hive.OpenSubKey(sub, writable: true)
            ?? throw new InvalidOperationException($"Registry key not found: {key}");

        regKey.DeleteValue(valueName ?? "", throwOnMissingValue: false);
        return $"Deleted {key}\\{valueName ?? "(default)"}";
    }

    private static string RegistryList(string key)
    {
        var (hive, sub) = SplitRegistryPath(key);
        using var regKey = hive.OpenSubKey(sub)
            ?? throw new InvalidOperationException($"Registry key not found: {key}");

        var sb = new StringBuilder();
        sb.AppendLine($"Key: {key}");
        sb.AppendLine();

        var subkeys = regKey.GetSubKeyNames();
        if (subkeys.Length > 0)
        {
            sb.AppendLine("Subkeys:");
            foreach (var sk in subkeys)
                sb.AppendLine($"  [{sk}]");
            sb.AppendLine();
        }

        var valueNames = regKey.GetValueNames();
        if (valueNames.Length > 0)
        {
            sb.AppendLine("Values:");
            foreach (var vname in valueNames)
            {
                var val = regKey.GetValue(vname);
                var vkind = regKey.GetValueKind(vname);
                sb.AppendLine($"  {(vname.Length > 0 ? vname : "(default)")} ({vkind}) = {val}");
            }
        }
        else
        {
            sb.AppendLine("(no values)");
        }

        return sb.ToString().TrimEnd();
    }
}
