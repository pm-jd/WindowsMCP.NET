using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using WindowsMcpNet.Config;

namespace WindowsMcpNet.Setup;

public sealed class SetupWizard
{
    private readonly ConfigManager _configManager;
    private readonly string _baseDirectory;

    public SetupWizard(ConfigManager configManager, string baseDirectory)
    {
        _configManager = configManager;
        _baseDirectory = baseDirectory;
    }

    public AppConfig Run(bool newKey = false, bool newCert = false)
    {
        var config = _configManager.Exists ? _configManager.Load() : new AppConfig();

        Console.WriteLine();
        Console.WriteLine("  WindowsMCP.NET — Setup");
        Console.WriteLine();

        // API Key
        if (config.ApiKey is null || newKey)
        {
            config.ApiKey = GenerateApiKey();
            Console.WriteLine($"  API-Key generated: {config.ApiKey}");
        }
        else
        {
            Console.WriteLine($"  API-Key: {config.ApiKey} (existing)");
        }

        // Certificate
        var certFullPath = Path.Combine(_baseDirectory, config.Https.CertPath);
        if (!File.Exists(certFullPath) || newCert)
        {
            Console.WriteLine();
            Console.Write("  Generate self-signed HTTPS certificate? [Y/n] ");
            var certAnswer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (certAnswer is "" or "y" or "j")
            {
                var (certPath, certPassword) = CertificateGenerator.Generate(_baseDirectory);
                config.Https.Enabled = true;
                config.Https.CertPath = Path.GetFileName(certPath);
                config.Https.CertPassword = certPassword;
                Console.WriteLine("  Certificate created.");
            }
            else
            {
                config.Https.Enabled = false;
                Console.WriteLine("  HTTPS disabled.");
            }
        }

        // Port
        Console.Write($"  Port [{config.Port}]: ");
        var portInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(portInput) && int.TryParse(portInput, out var port))
        {
            config.Port = port;
        }

        // Firewall rule
        OpenFirewallPort(config.Port);

        // Autostart
        Console.Write("  Start automatically with Windows? [Y/n] ");
        var autoAnswer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (autoAnswer is "" or "y" or "j")
        {
            AutoStartManager.Enable();
        }
        else if (AutoStartManager.IsEnabled())
        {
            AutoStartManager.Disable();
        }

        _configManager.Save(config);
        Console.WriteLine();
        Console.WriteLine($"  Config saved to: {Path.Combine(_baseDirectory, "config.json")}");

        PrintConfigSnippet(config);

        return config;
    }

    public static void PrintConfigSnippet(AppConfig config)
    {
        var scheme = config.Https.Enabled ? "https" : "http";
        var host = GetPrimaryLocalIp() ?? Dns.GetHostName();
        var url = $"{scheme}://{host}:{config.Port}";

        Console.WriteLine();
        Console.WriteLine("  Run this command on the client machine to connect:");
        Console.WriteLine();
        Console.WriteLine($"  claude mcp add windows-mcp-dotnet \"{url}\" --transport http --scope user --header \"Authorization: Bearer {config.ApiKey}\"");
        Console.WriteLine();
        Console.WriteLine("  Or add this to your Claude Code settings JSON:");
        Console.WriteLine();
        Console.WriteLine("  \"windows-mcp-dotnet\": {");
        Console.WriteLine("    \"type\": \"http\",");
        Console.WriteLine($"    \"url\": \"{url}\",");
        Console.WriteLine("    \"headers\": {");
        Console.WriteLine($"      \"Authorization\": \"Bearer {config.ApiKey}\"");
        Console.WriteLine("    }");
        Console.WriteLine("  }");
        Console.WriteLine();
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return "wmcp_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void OpenFirewallPort(int port)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"WindowsMCP.NET\" dir=in action=allow protocol=TCP localport={port}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
            if (process?.ExitCode == 0)
                Console.WriteLine($"  Firewall rule created (TCP port {port}).");
            else
                Console.Error.WriteLine($"  Warning: Could not create firewall rule for port {port}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Firewall configuration failed: {ex.Message}");
        }
    }

    private static string? GetPrimaryLocalIp()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                         && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .FirstOrDefault(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address.ToString();
    }
}
