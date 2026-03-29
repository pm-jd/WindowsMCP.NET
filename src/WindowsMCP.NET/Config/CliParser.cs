namespace WindowsMcpNet.Config;

public sealed record CliOptions
{
    public string? Command { get; init; }
    public string? Transport { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ApiKey { get; init; }
    public string? CertPath { get; init; }
    public string? CertPassword { get; init; }
    public List<string> AllowIps { get; init; } = [];
    public string? LogLevel { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public bool NewKey { get; init; }
    public bool NewCert { get; init; }
}

public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        string? command = null;
        string? transport = null;
        string? host = null;
        int? port = null;
        string? apiKey = null;
        string? certPath = null;
        string? certPassword = null;
        var allowIps = new List<string>();
        string? logLevel = null;
        bool showHelp = false;
        bool showVersion = false;
        bool newKey = false;
        bool newCert = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "setup":
                case "info":
                    command = args[i];
                    break;
                case "--transport" when i + 1 < args.Length:
                    transport = args[++i];
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var parsedPort))
                        port = parsedPort;
                    else
                        Console.Error.WriteLine($"Warning: Invalid port value '{args[i]}', ignoring.");
                    break;
                case "--api-key" when i + 1 < args.Length:
                    apiKey = args[++i];
                    break;
                case "--cert" when i + 1 < args.Length:
                    certPath = args[++i];
                    break;
                case "--cert-password" when i + 1 < args.Length:
                    certPassword = args[++i];
                    break;
                case "--allow-ip" when i + 1 < args.Length:
                    allowIps.Add(args[++i]);
                    break;
                case "--log-level" when i + 1 < args.Length:
                    logLevel = args[++i];
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--version" or "-v":
                    showVersion = true;
                    break;
                case "--new-key":
                    newKey = true;
                    break;
                case "--new-cert":
                    newCert = true;
                    break;
            }
        }

        return new CliOptions
        {
            Command = command,
            Transport = transport,
            Host = host,
            Port = port,
            ApiKey = apiKey,
            CertPath = certPath,
            CertPassword = certPassword,
            AllowIps = allowIps,
            LogLevel = logLevel,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            NewKey = newKey,
            NewCert = newCert,
        };
    }

    public static void PrintHelp()
    {
        Console.Error.WriteLine("""
            WindowsMCP.NET — Windows Desktop Automation MCP Server

            Usage: WindowsMCP.NET.exe [command] [options]

            Commands:
              setup                         Run setup wizard
              info                          Show Claude Code config snippet

            Transport:
              --transport <stdio|http>      Transport mode (default: http)
              --host <host>                 Bind address (default: 0.0.0.0)
              --port <port>                 Port (default: 8000)

            Security (HTTP mode):
              --api-key <key>               API key (alt: WMCP_API_KEY env)
              --cert <path>                 TLS certificate path (.pfx)
              --cert-password <pw>          Certificate password
              --allow-ip <ip>               Allowed IPs (repeatable)

            Setup:
              --new-key                     Generate new API key (with setup)
              --new-cert                    Generate new certificate (with setup)

            General:
              --log-level <level>           Log level (default: Information)
              --version                     Show version
              --help                        Show help
            """);
    }
}
