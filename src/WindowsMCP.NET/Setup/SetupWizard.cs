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

    public sealed record HostCandidate(string Host, string Label, bool IsPrimary);

    public static void PrintConfigSnippet(AppConfig config)
    {
        var scheme = config.Https.Enabled ? "https" : "http";
        var candidates = ResolveAdvertiseHosts(config);

        Console.WriteLine();
        if (candidates.Count == 1)
        {
            Console.WriteLine("  Run this command on the client machine to connect:");
            PrintCandidateBlock(candidates[0], scheme, config, indent: "  ");
        }
        else
        {
            Console.WriteLine("  This machine has multiple reachable addresses. Copy the block");
            Console.WriteLine("  that matches the network your clients are on:");
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                Console.WriteLine();
                var marker = c.IsPrimary ? "  (auto-detected default gateway)" : "";
                Console.WriteLine($"  --- Option {i + 1} - {c.Label} - {c.Host}{marker}");
                PrintCandidateBlock(c, scheme, config, indent: "  ");
            }
            Console.WriteLine();
            Console.WriteLine("  If none of these are reachable from clients, set 'advertiseHost'");
            Console.WriteLine("  in config.json or pass --advertise-host <ip> to override auto-detection.");
            Console.WriteLine();
        }
    }

    private static void PrintCandidateBlock(HostCandidate candidate, string scheme, AppConfig config, string indent)
    {
        var url = $"{scheme}://{candidate.Host}:{config.Port}";
        Console.WriteLine();
        Console.WriteLine($"{indent}claude mcp add windows-mcp-dotnet \"{url}\" --transport http --scope user --header \"Authorization: Bearer {config.ApiKey}\"");
        Console.WriteLine();
        Console.WriteLine($"{indent}\"windows-mcp-dotnet\": {{");
        Console.WriteLine($"{indent}  \"type\": \"http\",");
        Console.WriteLine($"{indent}  \"url\": \"{url}\",");
        Console.WriteLine($"{indent}  \"headers\": {{");
        Console.WriteLine($"{indent}    \"Authorization\": \"Bearer {config.ApiKey}\"");
        Console.WriteLine($"{indent}  }}");
        Console.WriteLine($"{indent}}}");
    }

    public static List<HostCandidate> ResolveAdvertiseHosts(AppConfig config)
    {
        // Priority: config.AdvertiseHost → WMCP_ADVERTISE_HOST env → route-probe + NIC enumeration → hostname
        if (!string.IsNullOrWhiteSpace(config.AdvertiseHost))
            return [new HostCandidate(config.AdvertiseHost!, "configured (advertiseHost)", IsPrimary: true)];

        var envHost = Environment.GetEnvironmentVariable("WMCP_ADVERTISE_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
            return [new HostCandidate(envHost, "configured (WMCP_ADVERTISE_HOST)", IsPrimary: true)];

        var nicCandidates = GetRoutableIPv4Addresses();
        if (nicCandidates.Count == 0)
            return [new HostCandidate(Dns.GetHostName(), "hostname fallback", IsPrimary: true)];

        var probedIp = ProbeOutboundIPv4();
        var primaryIndex = probedIp is null
            ? 0
            : nicCandidates.FindIndex(c => c.Ip == probedIp);
        if (primaryIndex < 0) primaryIndex = 0;

        var result = new List<HostCandidate>(nicCandidates.Count);
        result.Add(new HostCandidate(nicCandidates[primaryIndex].Ip, nicCandidates[primaryIndex].InterfaceName, IsPrimary: true));
        for (int i = 0; i < nicCandidates.Count; i++)
        {
            if (i == primaryIndex) continue;
            result.Add(new HostCandidate(nicCandidates[i].Ip, nicCandidates[i].InterfaceName, IsPrimary: false));
        }
        return result;
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

    private static List<(string Ip, string InterfaceName)> GetRoutableIPv4Addresses()
    {
        var result = new List<(string, string)>();
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
                result.Add((ip, ni.Name));
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
