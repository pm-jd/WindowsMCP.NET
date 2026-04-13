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

    public AppConfig Run(bool newKey = false, bool newCert = false, string? advertiseHost = null)
    {
        var config = _configManager.Exists ? _configManager.Load() : new AppConfig();

        if (advertiseHost is not null)
            config.AdvertiseHost = advertiseHost;

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

        // HTTPS (optional)
        Console.Write("  Enable HTTPS? (requires cert import on each client) [y/N] ");
        var certAnswer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (certAnswer is "y" or "j")
        {
            var certFullPath = Path.Combine(_baseDirectory, config.Https.CertPath);
            if (!File.Exists(certFullPath) || newCert)
            {
                var (certPath, certPassword) = CertificateGenerator.Generate(_baseDirectory);
                config.Https.CertPath = Path.GetFileName(certPath);
                config.Https.CertPassword = certPassword;
                Console.WriteLine("  Certificate created.");
            }
            config.Https.Enabled = true;
        }
        else
        {
            config.Https.Enabled = false;
            Console.WriteLine("  Using HTTP (recommended for internal networks).");
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
        var (host, alternatives) = ResolveAdvertiseHost(config);
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

        if (alternatives.Count > 0)
        {
            Console.WriteLine("  Alternative addresses detected on this machine:");
            foreach (var alt in alternatives)
                Console.WriteLine($"    {scheme}://{alt}:{config.Port}");
            Console.WriteLine("  If the address above is not reachable from clients, set 'advertiseHost'");
            Console.WriteLine("  in config.json or pass --advertise-host <ip> to override auto-detection.");
            Console.WriteLine();
        }
    }

    private static (string Host, List<string> Alternatives) ResolveAdvertiseHost(AppConfig config)
    {
        // Priority: config.AdvertiseHost → WMCP_ADVERTISE_HOST env → route-probe → gateway-filtered NIC → hostname
        if (!string.IsNullOrWhiteSpace(config.AdvertiseHost))
            return (config.AdvertiseHost!, []);

        var envHost = Environment.GetEnvironmentVariable("WMCP_ADVERTISE_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
            return (envHost, []);

        var candidates = GetRoutableIPv4Addresses();
        if (candidates.Count == 0)
            return (Dns.GetHostName(), []);

        var primary = ProbeOutboundIPv4() ?? candidates[0];
        var alternatives = candidates.Where(ip => ip != primary).ToList();
        return (primary, alternatives);
    }

    private static string? ProbeOutboundIPv4()
    {
        // Classic trick: UDP Connect does not send a packet but makes Windows
        // walk the routing table and bind LocalEndPoint to the interface it
        // would use for outbound traffic. That's the default-gateway NIC,
        // which is almost always the correct LAN address.
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetRoutableIPv4Addresses()
    {
        var result = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

            var props = ni.GetIPProperties();
            // Gateway filter kicks out WSL, Hyper-V virtual switches, Docker
            // host-only adapters — none of them have a default gateway.
            if (props.GatewayAddresses.Count == 0) continue;

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                // Skip APIPA (link-local) — visible as "up" but not routable.
                if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                result.Add(ip);
            }
        }
        return result;
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

}
