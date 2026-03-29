using System.Reflection;
using WindowsMcpNet.Config;
using WindowsMcpNet.Security;
using WindowsMcpNet.Services;
using WindowsMcpNet.Setup;

// Detect double-click launch (no args, not piped stdin)
var isInteractive = args.Length == 0 && !Console.IsInputRedirected;

try
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    var baseDirectory = AppContext.BaseDirectory;
    var configManager = new ConfigManager(baseDirectory);
    var cliOptions = CliParser.Parse(args);

    // --help
    if (cliOptions.ShowHelp)
    {
        CliParser.PrintHelp();
        return;
    }

    // --version
    if (cliOptions.ShowVersion)
    {
        Console.WriteLine($"WindowsMCP.NET v{version}");
        return;
    }

    // setup command
    if (cliOptions.Command == "setup")
    {
        var wizard = new SetupWizard(configManager, baseDirectory);
        wizard.Run(newKey: cliOptions.NewKey, newCert: cliOptions.NewCert);
        if (isInteractive) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadLine(); }
        return;
    }

    // info command
    if (cliOptions.Command == "info")
    {
        if (!configManager.Exists)
        {
            Console.Error.WriteLine("No config found. Run 'WindowsMCP.NET.exe setup' first.");
        }
        else
        {
            SetupWizard.PrintConfigSnippet(configManager.Load());
        }
        if (isInteractive) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadLine(); }
        return;
    }

    // Load or create config
    var config = configManager.Exists ? configManager.Load() : new AppConfig();

    // Apply CLI overrides
    var transport = cliOptions.Transport ?? config.Transport;
    if (cliOptions.Port.HasValue) config.Port = cliOptions.Port.Value;
    if (cliOptions.Host is not null) config.Host = cliOptions.Host;
    if (cliOptions.ApiKey is not null) config.ApiKey = cliOptions.ApiKey;
    config.ApiKey ??= Environment.GetEnvironmentVariable("WMCP_API_KEY");
    if (cliOptions.AllowIps.Count > 0) config.AllowedIps = cliOptions.AllowIps;

    // First-run: auto-setup for HTTP mode
    if (transport == "http" && !configManager.Exists)
    {
        var wizard = new SetupWizard(configManager, baseDirectory);
        config = wizard.Run();
    }

    // Validate HTTP mode requirements
    if (transport == "http" && string.IsNullOrEmpty(config.ApiKey))
    {
        Console.Error.WriteLine("HTTP mode requires an API key. Set via --api-key, WMCP_API_KEY env, or run setup.");
        if (isInteractive) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadLine(); }
        return;
    }

    void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(config);
        services.AddSingleton<DesktopService>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<UiAutomationService>();
        services.AddSingleton<UiTreeService>();
    }

    if (transport == "stdio")
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        RegisterServices(builder.Services);
#pragma warning disable IL2026
        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
#pragma warning restore IL2026

        await builder.Build().RunAsync();
    }
    else
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddConsole();
        RegisterServices(builder.Services);

        if (config.Https.Enabled)
        {
            var certPath = Path.IsPathRooted(config.Https.CertPath)
                ? config.Https.CertPath
                : Path.Combine(baseDirectory, config.Https.CertPath);

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                if (config.Host == "0.0.0.0")
                    kestrel.ListenAnyIP(config.Port, listenOptions =>
                    {
                        listenOptions.UseHttps(certPath, config.Https.CertPassword);
                    });
                else
                    kestrel.Listen(System.Net.IPAddress.Parse(config.Host), config.Port, listenOptions =>
                    {
                        listenOptions.UseHttps(certPath, config.Https.CertPassword);
                    });
            });
        }
        else
        {
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                if (config.Host == "0.0.0.0")
                    kestrel.ListenAnyIP(config.Port);
                else
                    kestrel.Listen(System.Net.IPAddress.Parse(config.Host), config.Port);
            });
        }

#pragma warning disable IL2026
        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
            })
            .WithHttpTransport(options =>
            {
                options.Stateless = true;
            })
            .WithToolsFromAssembly();
#pragma warning restore IL2026

        var app = builder.Build();

        if (config.AllowedIps.Count > 0)
            app.UseMiddleware<IpAllowlistMiddleware>(config.AllowedIps.AsEnumerable());

        app.UseMiddleware<ApiKeyMiddleware>(config.ApiKey!);
        app.MapMcp();

        Console.Error.WriteLine($"WindowsMCP.NET v{version}");
        Console.Error.WriteLine($"Listening on {(config.Https.Enabled ? "https" : "http")}://{config.Host}:{config.Port}");
        Console.Error.WriteLine("Press Ctrl+C to stop.");

        if (isInteractive)
            TrayIconManager.HideConsole();

        var displayHost = config.Host == "0.0.0.0"
            ? (System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                             && ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.Address.ToString() ?? System.Net.Dns.GetHostName())
            : config.Host;
        var url = $"{(config.Https.Enabled ? "https" : "http")}://{displayHost}:{config.Port}";
        using var trayIcon = new TrayIconManager(url, config.ApiKey, () =>
        {
            app.Lifetime.StopApplication();
        });
        trayIcon.Show();

        await app.RunAsync();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nFatal error: {ex.Message}");
    if (isInteractive) { Console.WriteLine("\nPress Enter to exit..."); Console.ReadLine(); }
    Environment.Exit(1);
}
