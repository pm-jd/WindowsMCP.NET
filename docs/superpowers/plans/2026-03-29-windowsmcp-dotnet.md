# WindowsMCP.NET Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET C# MCP server for Windows desktop automation with 18 tools, remote HTTP transport, and portable single-file deployment.

**Architecture:** Monolithic single-project, attribute-based MCP tool registration via `[McpServerToolType]`/`[McpServerTool]`, four DI singleton services (Desktop, ScreenCapture, UiAutomation, UiTree), native Win32 via `[LibraryImport]`, dual transport (HTTP primary, stdio secondary).

**Tech Stack:** .NET 9 (`net9.0-windows`), ModelContextProtocol 1.2.x, ModelContextProtocol.AspNetCore, FlaUI.UIA3, FuzzySharp, ReverseMarkdown, System.Drawing.Common, Native AOT publish.

**Design Spec:** `docs/superpowers/specs/2026-03-29-windowsmcp-dotnet-design.md`

**Testing strategy:** xUnit. Native/Win32 code is tested via integration tests (require Windows desktop). Config, CLI parsing, and pure logic are unit-tested. Integration tests are marked with `[Trait("Category", "Integration")]` so they can be filtered in CI.

---

## File Structure

```
WindowsMCP.NET/
├── .github/workflows/build.yml
├── src/WindowsMCP.NET/
│   ├── Program.cs                          — Entry point, transport branching, DI setup
│   ├── WindowsMCP.NET.csproj               — Project file with AOT + packages
│   ├── Config/
│   │   ├── AppConfig.cs                    — Config model (record)
│   │   ├── ConfigManager.cs                — Load/save config.json, DPAPI for cert password
│   │   └── CliParser.cs                    — Manual CLI argument parsing
│   ├── Setup/
│   │   ├── SetupWizard.cs                  — Interactive first-run wizard
│   │   └── CertificateGenerator.cs         — Self-signed cert generation
│   ├── Security/
│   │   ├── ApiKeyMiddleware.cs             — ASP.NET Core middleware for Bearer auth
│   │   └── IpAllowlistMiddleware.cs        — IP-based access control
│   ├── Native/
│   │   ├── User32.cs                       — user32.dll P/Invoke (mouse, keyboard, window, clipboard)
│   │   ├── Kernel32.cs                     — kernel32.dll P/Invoke
│   │   ├── Shell32.cs                      — Shell/notification interop
│   │   └── Dxgi.cs                         — DXGI Desktop Duplication API
│   ├── Models/
│   │   ├── WindowInfo.cs                   — Window data record
│   │   ├── UiElementNode.cs                — UI tree node
│   │   └── AnnotatedTree.cs                — UI tree with labels + metadata
│   ├── Services/
│   │   ├── DesktopService.cs               — Window management
│   │   ├── ScreenCaptureService.cs         — DXGI + GDI+ fallback
│   │   ├── UiAutomationService.cs          — FlaUI wrapper
│   │   └── UiTreeService.cs                — Cached UI tree + label assignment
│   └── Tools/
│       ├── InputTools.cs                   — Click, Type, Scroll, Move, Shortcut, Wait
│       ├── SnapshotTools.cs                — Snapshot, Screenshot
│       ├── AppTools.cs                     — App (launch/switch/resize)
│       ├── SystemTools.cs                  — PowerShell, Process, Registry
│       ├── ClipboardTools.cs               — Clipboard (get/set)
│       ├── FileSystemTools.cs              — FileSystem (8 modes)
│       ├── NotificationTools.cs            — Toast notification
│       ├── MultiTools.cs                   — MultiSelect, MultiEdit
│       └── ScrapeTools.cs                  — Web scraping
├── tests/WindowsMCP.NET.Tests/
│   ├── WindowsMCP.NET.Tests.csproj
│   ├── Config/
│   │   ├── AppConfigTests.cs
│   │   └── CliParserTests.cs
│   ├── Services/
│   │   └── ScreenCaptureServiceTests.cs
│   └── Tools/
│       ├── FileSystemToolsTests.cs
│       └── ScrapeToolsTests.cs
├── WindowsMCP.NET.sln
├── Directory.Build.props
├── .gitignore
└── LICENSE
```

---

## Task 1: Project Scaffolding

**Files:**
- Create: `WindowsMCP.NET.sln`
- Create: `Directory.Build.props`
- Create: `src/WindowsMCP.NET/WindowsMCP.NET.csproj`
- Create: `src/WindowsMCP.NET/Program.cs`
- Create: `tests/WindowsMCP.NET.Tests/WindowsMCP.NET.Tests.csproj`
- Create: `.gitignore`
- Create: `LICENSE`

- [ ] **Step 1: Initialize git repository**

```bash
cd /c/work/source/mygit/windows-mcp
git init
```

- [ ] **Step 2: Create .gitignore**

Create `.gitignore` with standard .NET entries:

```
bin/
obj/
*.user
*.suo
.vs/
*.DotSettings.user
publish/
TestResults/
```

- [ ] **Step 3: Create LICENSE**

Create `LICENSE` with MIT license text, copyright `2026 WindowsMCP.NET Contributors`.

- [ ] **Step 4: Create Directory.Build.props**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Create src/WindowsMCP.NET/WindowsMCP.NET.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <AssemblyName>WindowsMCP.NET</AssemblyName>
    <RootNamespace>WindowsMcpNet</RootNamespace>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.2.0" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.2.0" />
    <PackageReference Include="FlaUI.UIA3" Version="4.0.0" />
    <PackageReference Include="FuzzySharp" Version="2.0.2" />
    <PackageReference Include="ReverseMarkdown" Version="4.6.0" />
    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
  </ItemGroup>
</Project>
```

Note: Use `Microsoft.NET.Sdk.Web` (not `Microsoft.NET.Sdk`) because we need ASP.NET Core for HTTP transport. The exact package versions should be verified at implementation time via `dotnet add package`.

- [ ] **Step 6: Create minimal Program.cs**

Create `src/WindowsMCP.NET/Program.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
```

- [ ] **Step 7: Create test project**

Create `tests/WindowsMCP.NET.Tests/WindowsMCP.NET.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\WindowsMCP.NET\WindowsMCP.NET.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Create solution and add projects**

```bash
cd /c/work/source/mygit/windows-mcp
dotnet new sln --name WindowsMCP.NET
dotnet sln add src/WindowsMCP.NET/WindowsMCP.NET.csproj
dotnet sln add tests/WindowsMCP.NET.Tests/WindowsMCP.NET.Tests.csproj
```

- [ ] **Step 9: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 10: Commit**

```bash
git add .gitignore LICENSE Directory.Build.props WindowsMCP.NET.sln src/ tests/
git commit -m "feat: scaffold WindowsMCP.NET project structure"
```

---

## Task 2: Config System

**Files:**
- Create: `src/WindowsMCP.NET/Config/AppConfig.cs`
- Create: `src/WindowsMCP.NET/Config/ConfigManager.cs`
- Create: `tests/WindowsMCP.NET.Tests/Config/AppConfigTests.cs`

- [ ] **Step 1: Write config model tests**

Create `tests/WindowsMCP.NET.Tests/Config/AppConfigTests.cs`:

```csharp
using System.Text.Json;
using WindowsMcpNet.Config;

namespace WindowsMcpNet.Tests.Config;

public class AppConfigTests
{
    [Fact]
    public void DefaultConfig_HasExpectedValues()
    {
        var config = new AppConfig();

        Assert.Equal("http", config.Transport);
        Assert.Equal("0.0.0.0", config.Host);
        Assert.Equal(8000, config.Port);
        Assert.Null(config.ApiKey);
        Assert.True(config.Https.Enabled);
        Assert.Empty(config.AllowedIps);
    }

    [Fact]
    public void Config_SerializesAndDeserializes()
    {
        var config = new AppConfig
        {
            Transport = "stdio",
            Port = 9000,
            ApiKey = "wmcp_testkey123"
        };

        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        var deserialized = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);

        Assert.NotNull(deserialized);
        Assert.Equal("stdio", deserialized.Transport);
        Assert.Equal(9000, deserialized.Port);
        Assert.Equal("wmcp_testkey123", deserialized.ApiKey);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~AppConfigTests"
```

Expected: FAIL — `AppConfig` and `AppConfigJsonContext` do not exist.

- [ ] **Step 3: Implement AppConfig**

Create `src/WindowsMCP.NET/Config/AppConfig.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMcpNet.Config;

public sealed class AppConfig
{
    public string Transport { get; set; } = "http";
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8000;
    public string? ApiKey { get; set; }
    public HttpsConfig Https { get; set; } = new();
    public List<string> AllowedIps { get; set; } = [];
    public string LogLevel { get; set; } = "Information";
}

public sealed class HttpsConfig
{
    public bool Enabled { get; set; } = true;
    public string CertPath { get; set; } = "cert.pfx";
    public string? CertPassword { get; set; }
}

[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppConfigJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~AppConfigTests"
```

Expected: 2 tests passed.

- [ ] **Step 5: Write ConfigManager tests**

Add to `tests/WindowsMCP.NET.Tests/Config/AppConfigTests.cs`:

```csharp
public class ConfigManagerTests
{
    [Fact]
    public void LoadConfig_ReturnsDefault_WhenFileNotExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager = new ConfigManager(tempDir);
            var config = manager.Load();

            Assert.Equal("http", config.Transport);
            Assert.Equal(8000, config.Port);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager = new ConfigManager(tempDir);
            var config = new AppConfig { Port = 9999, ApiKey = "wmcp_test" };
            manager.Save(config);

            var loaded = manager.Load();
            Assert.Equal(9999, loaded.Port);
            Assert.Equal("wmcp_test", loaded.ApiKey);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 6: Implement ConfigManager**

Create `src/WindowsMCP.NET/Config/ConfigManager.cs`:

```csharp
using System.Text.Json;

namespace WindowsMcpNet.Config;

public sealed class ConfigManager
{
    private readonly string _configPath;

    public ConfigManager(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return new AppConfig();

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig)
               ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, json);
    }

    public bool Exists => File.Exists(_configPath);
}
```

- [ ] **Step 7: Run all config tests**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~Config"
```

Expected: 4 tests passed.

- [ ] **Step 8: Commit**

```bash
git add src/WindowsMCP.NET/Config/ tests/WindowsMCP.NET.Tests/Config/
git commit -m "feat: add config model and ConfigManager with JSON serialization"
```

---

## Task 3: CLI Parsing

**Files:**
- Create: `src/WindowsMCP.NET/Config/CliParser.cs`
- Create: `tests/WindowsMCP.NET.Tests/Config/CliParserTests.cs`

- [ ] **Step 1: Write CLI parser tests**

Create `tests/WindowsMCP.NET.Tests/Config/CliParserTests.cs`:

```csharp
using WindowsMcpNet.Config;

namespace WindowsMcpNet.Tests.Config;

public class CliParserTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsDefaults()
    {
        var result = CliParser.Parse([]);

        Assert.Null(result.Command);
        Assert.Null(result.Transport);
        Assert.Null(result.Port);
        Assert.False(result.ShowHelp);
        Assert.False(result.ShowVersion);
    }

    [Fact]
    public void Parse_SetupCommand_Detected()
    {
        var result = CliParser.Parse(["setup"]);
        Assert.Equal("setup", result.Command);
    }

    [Fact]
    public void Parse_SetupNewKey_Detected()
    {
        var result = CliParser.Parse(["setup", "--new-key"]);
        Assert.Equal("setup", result.Command);
        Assert.True(result.NewKey);
    }

    [Fact]
    public void Parse_InfoCommand_Detected()
    {
        var result = CliParser.Parse(["info"]);
        Assert.Equal("info", result.Command);
    }

    [Fact]
    public void Parse_TransportAndPort()
    {
        var result = CliParser.Parse(["--transport", "stdio", "--port", "9000"]);
        Assert.Equal("stdio", result.Transport);
        Assert.Equal(9000, result.Port);
    }

    [Fact]
    public void Parse_AllowIp_Multiple()
    {
        var result = CliParser.Parse(["--allow-ip", "10.0.0.1", "--allow-ip", "10.0.0.2"]);
        Assert.Equal(["10.0.0.1", "10.0.0.2"], result.AllowIps);
    }

    [Fact]
    public void Parse_Help()
    {
        var result = CliParser.Parse(["--help"]);
        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void Parse_Version()
    {
        var result = CliParser.Parse(["--version"]);
        Assert.True(result.ShowVersion);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~CliParserTests"
```

Expected: FAIL — `CliParser` does not exist.

- [ ] **Step 3: Implement CliParser**

Create `src/WindowsMCP.NET/Config/CliParser.cs`:

```csharp
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
                    port = int.Parse(args[++i]);
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
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~CliParserTests"
```

Expected: 8 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/WindowsMCP.NET/Config/CliParser.cs tests/WindowsMCP.NET.Tests/Config/CliParserTests.cs
git commit -m "feat: add CLI argument parser"
```

---

## Task 4: Setup Wizard & Certificate Generator

**Files:**
- Create: `src/WindowsMCP.NET/Setup/CertificateGenerator.cs`
- Create: `src/WindowsMCP.NET/Setup/SetupWizard.cs`

- [ ] **Step 1: Implement CertificateGenerator**

Create `src/WindowsMCP.NET/Setup/CertificateGenerator.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WindowsMcpNet.Setup;

public static class CertificateGenerator
{
    public static (string certPath, string password) Generate(string baseDirectory)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var certPath = Path.Combine(baseDirectory, "cert.pfx");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=WindowsMCP.NET",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Dns.GetHostName());
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);

        foreach (var ip in GetLocalIpAddresses())
        {
            sanBuilder.AddIpAddress(ip);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(2));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(certPath, pfxBytes);

        return (certPath, password);
    }

    private static IEnumerable<IPAddress> GetLocalIpAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(ua => ua.Address);
    }
}
```

- [ ] **Step 2: Implement SetupWizard**

Create `src/WindowsMCP.NET/Setup/SetupWizard.cs`:

```csharp
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

        Console.WriteLine();
        Console.WriteLine("  Add this to your Claude Code settings:");
        Console.WriteLine();
        Console.WriteLine("  \"windows-mcp-dotnet\": {");
        Console.WriteLine("    \"type\": \"streamable-http\",");
        Console.WriteLine($"    \"url\": \"{scheme}://{host}:{config.Port}/mcp\",");
        Console.WriteLine("    \"headers\": {");
        Console.WriteLine($"      \"Authorization\": \"Bearer {config.ApiKey}\"");
        Console.WriteLine("    }");
        Console.WriteLine("  }");
        Console.WriteLine();
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "wmcp_" + Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];
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
```

- [ ] **Step 3: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Setup/
git commit -m "feat: add setup wizard with API key and certificate generation"
```

---

## Task 5: Security Middleware

**Files:**
- Create: `src/WindowsMCP.NET/Security/ApiKeyMiddleware.cs`
- Create: `src/WindowsMCP.NET/Security/IpAllowlistMiddleware.cs`

- [ ] **Step 1: Implement ApiKeyMiddleware**

Create `src/WindowsMCP.NET/Security/ApiKeyMiddleware.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace WindowsMcpNet.Security;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, string apiKey)
    {
        _next = next;
        _apiKey = apiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing or invalid Authorization header. Expected: Bearer <api-key>");
            return;
        }

        var providedKey = authHeader["Bearer ".Length..].Trim();
        if (!string.Equals(providedKey, _apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 2: Implement IpAllowlistMiddleware**

Create `src/WindowsMCP.NET/Security/IpAllowlistMiddleware.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Http;

namespace WindowsMcpNet.Security;

public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedIps;

    public IpAllowlistMiddleware(RequestDelegate next, IEnumerable<string> allowedIps)
    {
        _next = next;
        _allowedIps = new HashSet<string>(allowedIps);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_allowedIps.Count == 0)
        {
            await _next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (remoteIp is null || !_allowedIps.Contains(remoteIp))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync($"IP {remoteIp} is not in the allowlist.");
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Security/
git commit -m "feat: add API key and IP allowlist middleware"
```

---

## Task 6: Program.cs — Full Entry Point with Dual Transport

**Files:**
- Modify: `src/WindowsMCP.NET/Program.cs`

- [ ] **Step 1: Implement full Program.cs**

Replace `src/WindowsMCP.NET/Program.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WindowsMcpNet.Config;
using WindowsMcpNet.Security;
using WindowsMcpNet.Services;
using WindowsMcpNet.Setup;

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
    return;
}

// info command
if (cliOptions.Command == "info")
{
    if (!configManager.Exists)
    {
        Console.Error.WriteLine("No config found. Run 'WindowsMCP.NET.exe setup' first.");
        return;
    }
    SetupWizard.PrintConfigSnippet(configManager.Load());
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
    builder.Services
        .AddMcpServer(o =>
        {
            o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
        })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

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
            kestrel.ListenAnyIP(config.Port, listenOptions =>
            {
                listenOptions.UseHttps(certPath, config.Https.CertPassword);
            });
        });
    }
    else
    {
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(config.Port);
        });
    }

    builder.Services
        .AddMcpServer(o =>
        {
            o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
        })
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    if (config.AllowedIps.Count > 0)
        app.UseMiddleware<IpAllowlistMiddleware>(config.AllowedIps);

    app.UseMiddleware<ApiKeyMiddleware>(config.ApiKey!);
    app.MapMcp();

    Console.Error.WriteLine($"WindowsMCP.NET v{version}");
    Console.Error.WriteLine($"Listening on {(config.Https.Enabled ? "https" : "http")}://{config.Host}:{config.Port}");

    await app.RunAsync();
}
```

- [ ] **Step 2: Create placeholder services so it compiles**

Create `src/WindowsMCP.NET/Services/DesktopService.cs`:

```csharp
namespace WindowsMcpNet.Services;

public sealed class DesktopService;
```

Create `src/WindowsMCP.NET/Services/ScreenCaptureService.cs`:

```csharp
namespace WindowsMcpNet.Services;

public sealed class ScreenCaptureService;
```

Create `src/WindowsMCP.NET/Services/UiAutomationService.cs`:

```csharp
namespace WindowsMcpNet.Services;

public sealed class UiAutomationService;
```

Create `src/WindowsMCP.NET/Services/UiTreeService.cs`:

```csharp
namespace WindowsMcpNet.Services;

public sealed class UiTreeService;
```

- [ ] **Step 3: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Program.cs src/WindowsMCP.NET/Services/
git commit -m "feat: implement dual-transport entry point with HTTP/stdio branching"
```

---

## Task 7: Native Layer — User32

**Files:**
- Create: `src/WindowsMCP.NET/Native/User32.cs`
- Create: `src/WindowsMCP.NET/Native/Kernel32.cs`

- [ ] **Step 1: Implement User32 P/Invoke**

Create `src/WindowsMCP.NET/Native/User32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace WindowsMcpNet.Native;

internal static partial class User32
{
    // --- Mouse & Keyboard Input ---

    [LibraryImport("user32.dll")]
    internal static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    // --- Window Management ---

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveWindow(nint hWnd, int x, int y, int width, int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowTextW(nint hWnd, Span<char> lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    // --- Clipboard ---

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    internal static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll")]
    internal static partial nint SetClipboardData(uint uFormat, nint hMem);

    // --- Virtual Key mapping ---

    [LibraryImport("user32.dll")]
    internal static partial short VkKeyScanW(char ch);

    [LibraryImport("user32.dll")]
    internal static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    // --- Delegates ---

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    // --- Constants ---

    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_MAXIMIZE = 3;

    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;

    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const uint CF_UNICODETEXT = 13;
}

// --- Structs ---

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public INPUT_UNION U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct INPUT_UNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}
```

- [ ] **Step 2: Implement Kernel32 P/Invoke**

Create `src/WindowsMCP.NET/Native/Kernel32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace WindowsMcpNet.Native;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    internal const uint GMEM_MOVEABLE = 0x0002;
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Native/User32.cs src/WindowsMCP.NET/Native/Kernel32.cs
git commit -m "feat: add User32 and Kernel32 P/Invoke declarations"
```

---

## Task 8: Models

**Files:**
- Create: `src/WindowsMCP.NET/Models/WindowInfo.cs`
- Create: `src/WindowsMCP.NET/Models/UiElementNode.cs`
- Create: `src/WindowsMCP.NET/Models/AnnotatedTree.cs`

- [ ] **Step 1: Create models**

Create `src/WindowsMCP.NET/Models/WindowInfo.cs`:

```csharp
namespace WindowsMcpNet.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    uint ProcessId,
    int X, int Y,
    int Width, int Height,
    bool IsVisible);
```

Create `src/WindowsMCP.NET/Models/UiElementNode.cs`:

```csharp
namespace WindowsMcpNet.Models;

public sealed class UiElementNode
{
    public required string Name { get; init; }
    public required string ControlType { get; init; }
    public string? AutomationId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public bool IsInteractive { get; init; }
    public string? Label { get; set; }
    public List<UiElementNode> Children { get; init; } = [];
}
```

Create `src/WindowsMCP.NET/Models/AnnotatedTree.cs`:

```csharp
namespace WindowsMcpNet.Models;

public sealed class AnnotatedTree
{
    public required List<UiElementNode> Roots { get; init; }
    public required Dictionary<string, UiElementNode> LabelMap { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var root in Roots)
            AppendNode(sb, root, indent: 0);
        return sb.ToString();
    }

    private static void AppendNode(System.Text.StringBuilder sb, UiElementNode node, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var label = node.Label is not null ? $"[{node.Label}] " : "";
        sb.AppendLine($"{prefix}{label}{node.ControlType}: \"{node.Name}\" ({node.X},{node.Y} {node.Width}x{node.Height})");
        foreach (var child in node.Children)
            AppendNode(sb, child, indent + 1);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Models/
git commit -m "feat: add data models (WindowInfo, UiElementNode, AnnotatedTree)"
```

---

## Task 9: DesktopService

**Files:**
- Modify: `src/WindowsMCP.NET/Services/DesktopService.cs`

- [ ] **Step 1: Implement DesktopService**

Replace `src/WindowsMCP.NET/Services/DesktopService.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using FuzzySharp;
using Microsoft.Extensions.Logging;
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

            Span<char> titleBuf = stackalloc char[256];
            var titleLen = User32.GetWindowTextW(hWnd, titleBuf, 256);
            if (titleLen == 0) return true;

            var title = titleBuf[..titleLen].ToString();
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

        Span<char> titleBuf = stackalloc char[256];
        var titleLen = User32.GetWindowTextW(hWnd, titleBuf, 256);
        var title = titleLen > 0 ? titleBuf[..titleLen].ToString() : "";

        User32.GetWindowThreadProcessId(hWnd, out var pid);
        User32.GetWindowRect(hWnd, out var rect);

        return new WindowInfo(hWnd, title, pid,
            rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top, true);
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

    public WindowInfo? LaunchApp(string name)
    {
        _logger.LogInformation("Launching app: {Name}", name);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = name,
            UseShellExecute = true,
        });

        if (process is null) return null;

        // Wait briefly for window to appear
        process.WaitForInputIdle(3000);
        Thread.Sleep(500);

        // Find window belonging to this process
        var windows = ListWindows();
        return windows.FirstOrDefault(w => w.ProcessId == (uint)process.Id);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/DesktopService.cs
git commit -m "feat: implement DesktopService (window list, switch, resize, launch)"
```

---

## Task 10: ScreenCaptureService

**Files:**
- Modify: `src/WindowsMCP.NET/Services/ScreenCaptureService.cs`

- [ ] **Step 1: Implement ScreenCaptureService with GDI+ (fallback first)**

Replace `src/WindowsMCP.NET/Services/ScreenCaptureService.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;

namespace WindowsMcpNet.Services;

public sealed class ScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public byte[] CaptureScreen(int? displayIndex = null)
    {
        try
        {
            return CaptureWithDxgi(displayIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DXGI capture failed, falling back to GDI+");
            return CaptureWithGdi(displayIndex);
        }
    }

    private byte[] CaptureWithGdi(int? displayIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = displayIndex.HasValue && displayIndex.Value < screens.Length
            ? screens[displayIndex.Value]
            : System.Windows.Forms.Screen.PrimaryScreen
              ?? throw new InvalidOperationException("No screen found.");

        var bounds = screen.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private byte[] CaptureWithDxgi(int? displayIndex)
    {
        // DXGI Desktop Duplication will be implemented in a later task.
        // For now, delegate to GDI+ so the tool chain works end-to-end.
        _logger.LogDebug("DXGI not yet implemented, using GDI+");
        return CaptureWithGdi(displayIndex);
    }

    public byte[] AnnotateScreenshot(byte[] pngBytes, IReadOnlyList<(int X, int Y, string Label)> annotations)
    {
        using var ms = new MemoryStream(pngBytes);
        using var bitmap = new Bitmap(ms);
        using var graphics = Graphics.FromImage(bitmap);

        var font = new Font("Arial", 10, FontStyle.Bold);
        var bgBrush = new SolidBrush(Color.FromArgb(200, Color.Red));
        var textBrush = new SolidBrush(Color.White);

        foreach (var (x, y, label) in annotations)
        {
            var textSize = graphics.MeasureString(label, font);
            var rect = new RectangleF(x - 2, y - textSize.Height - 2, textSize.Width + 4, textSize.Height + 2);
            graphics.FillRectangle(bgBrush, rect);
            graphics.DrawString(label, font, textBrush, x, y - textSize.Height - 1);
        }

        using var outMs = new MemoryStream();
        bitmap.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }
}
```

Note: This uses `System.Windows.Forms.Screen` for screen enumeration. The csproj needs `<UseWindowsForms>true</UseWindowsForms>`. Add it in the PropertyGroup. DXGI will be implemented as a future enhancement task; GDI+ provides full functionality for now.

- [ ] **Step 2: Add UseWindowsForms to csproj**

In `src/WindowsMCP.NET/WindowsMCP.NET.csproj`, add to PropertyGroup:

```xml
<UseWindowsForms>true</UseWindowsForms>
```

- [ ] **Step 3: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 4: Commit**

```bash
git add src/WindowsMCP.NET/Services/ScreenCaptureService.cs src/WindowsMCP.NET/WindowsMCP.NET.csproj
git commit -m "feat: implement ScreenCaptureService with GDI+ and annotation overlay"
```

---

## Task 11: UiAutomationService

**Files:**
- Modify: `src/WindowsMCP.NET/Services/UiAutomationService.cs`

- [ ] **Step 1: Implement UiAutomationService**

Replace `src/WindowsMCP.NET/Services/UiAutomationService.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public sealed class UiAutomationService : IDisposable
{
    private readonly ILogger<UiAutomationService> _logger;
    private readonly UIA3Automation _automation;

    public UiAutomationService(ILogger<UiAutomationService> logger)
    {
        _logger = logger;
        _automation = new UIA3Automation();
    }

    public List<UiElementNode> GetDesktopTree(int maxDepth = 5)
    {
        var desktop = _automation.GetDesktop();
        var children = desktop.FindAllChildren();
        return children.Select(c => BuildNode(c, 0, maxDepth)).ToList();
    }

    public AutomationElement? FindElementByName(string name, int minScore = 60)
    {
        var desktop = _automation.GetDesktop();
        var allElements = desktop.FindAllDescendants();

        AutomationElement? best = null;
        int bestScore = 0;

        foreach (var el in allElements)
        {
            try
            {
                var elName = el.Name;
                if (string.IsNullOrEmpty(elName)) continue;

                var score = Fuzz.PartialRatio(name.ToLowerInvariant(), elName.ToLowerInvariant());
                if (score > bestScore)
                {
                    bestScore = score;
                    best = el;
                }
            }
            catch
            {
                // Some elements throw when accessing properties — skip them.
            }
        }

        return bestScore >= minScore ? best : null;
    }

    public List<AutomationElement> FindElements(
        string? automationId = null,
        string? name = null,
        ControlType? controlType = null)
    {
        var desktop = _automation.GetDesktop();
        var conditions = new List<ConditionBase>();
        var cf = _automation.ConditionFactory;

        if (automationId is not null) conditions.Add(cf.ByAutomationId(automationId));
        if (name is not null) conditions.Add(cf.ByName(name));
        if (controlType.HasValue) conditions.Add(cf.ByControlType(controlType.Value));

        var combined = conditions.Count switch
        {
            0 => TrueCondition.Default,
            1 => conditions[0],
            _ => new AndCondition(conditions.ToArray())
        };

        return desktop.FindAllDescendants(combined).ToList();
    }

    public (int X, int Y)? GetClickablePoint(AutomationElement element)
    {
        try
        {
            if (element.TryGetClickablePoint(out var point))
                return ((int)point.X, (int)point.Y);

            var rect = element.BoundingRectangle;
            if (!rect.IsEmpty)
                return ((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get clickable point");
        }
        return null;
    }

    private static UiElementNode BuildNode(AutomationElement element, int depth, int maxDepth)
    {
        var rect = element.BoundingRectangle;
        var node = new UiElementNode
        {
            Name = element.Name ?? "",
            ControlType = element.ControlType.ToString(),
            AutomationId = element.AutomationId,
            X = (int)rect.X,
            Y = (int)rect.Y,
            Width = (int)rect.Width,
            Height = (int)rect.Height,
            IsInteractive = IsInteractiveType(element.ControlType),
        };

        if (depth < maxDepth)
        {
            try
            {
                var children = element.FindAllChildren();
                node.Children.AddRange(children.Select(c => BuildNode(c, depth + 1, maxDepth)));
            }
            catch
            {
                // Some elements throw when accessing children.
            }
        }

        return node;
    }

    private static bool IsInteractiveType(ControlType type) =>
        type == ControlType.Button ||
        type == ControlType.CheckBox ||
        type == ControlType.ComboBox ||
        type == ControlType.Edit ||
        type == ControlType.Hyperlink ||
        type == ControlType.ListItem ||
        type == ControlType.MenuItem ||
        type == ControlType.RadioButton ||
        type == ControlType.Slider ||
        type == ControlType.Tab ||
        type == ControlType.TabItem ||
        type == ControlType.TreeItem;

    public void Dispose() => _automation.Dispose();
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/UiAutomationService.cs
git commit -m "feat: implement UiAutomationService with FlaUI and fuzzy search"
```

---

## Task 12: UiTreeService

**Files:**
- Modify: `src/WindowsMCP.NET/Services/UiTreeService.cs`

- [ ] **Step 1: Implement UiTreeService**

Replace `src/WindowsMCP.NET/Services/UiTreeService.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Logging;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public sealed class UiTreeService
{
    private readonly UiAutomationService _uiAutomation;
    private readonly ILogger<UiTreeService> _logger;

    private AnnotatedTree? _cache;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(2);

    public UiTreeService(UiAutomationService uiAutomation, ILogger<UiTreeService> logger)
    {
        _uiAutomation = uiAutomation;
        _logger = logger;
    }

    public AnnotatedTree BuildAnnotatedTree()
    {
        if (_cache is not null && DateTimeOffset.UtcNow - _cache.Timestamp < _cacheTtl)
        {
            _logger.LogDebug("Returning cached UI tree");
            return _cache;
        }

        _logger.LogDebug("Building fresh UI tree");
        var roots = _uiAutomation.GetDesktopTree();
        var labelMap = new Dictionary<string, UiElementNode>();
        var counter = 1;

        AssignLabels(roots, labelMap, ref counter);

        _cache = new AnnotatedTree
        {
            Roots = roots,
            LabelMap = labelMap,
            Timestamp = DateTimeOffset.UtcNow,
        };

        _logger.LogInformation("UI tree built with {Count} interactive elements", labelMap.Count);
        return _cache;
    }

    public (int X, int Y)? ResolveLabel(string label)
    {
        var tree = _cache ?? BuildAnnotatedTree();

        if (!tree.LabelMap.TryGetValue(label, out var node))
            return null;

        // Return center of the element
        return (node.X + node.Width / 2, node.Y + node.Height / 2);
    }

    public void InvalidateCache()
    {
        _cache = null;
        _logger.LogDebug("UI tree cache invalidated");
    }

    public List<(int X, int Y, string Label)> GetAnnotationPoints()
    {
        var tree = _cache ?? BuildAnnotatedTree();
        return tree.LabelMap
            .Select(kvp => (kvp.Value.X + kvp.Value.Width / 2, kvp.Value.Y, kvp.Key))
            .ToList();
    }

    private static void AssignLabels(List<UiElementNode> nodes, Dictionary<string, UiElementNode> labelMap, ref int counter)
    {
        foreach (var node in nodes)
        {
            if (node.IsInteractive)
            {
                var label = counter.ToString();
                node.Label = label;
                labelMap[label] = node;
                counter++;
            }
            AssignLabels(node.Children, labelMap, ref counter);
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Services/UiTreeService.cs
git commit -m "feat: implement UiTreeService with caching and label assignment"
```

---

## Task 13: InputTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/InputTools.cs`

- [ ] **Step 1: Implement InputTools**

Create `src/WindowsMCP.NET/Tools/InputTools.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class InputTools
{
    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Perform a mouse click at coordinates [x,y] or a UI element label. Button: left, right, middle. Clicks: 0 (move only), 1 (single), 2 (double).")]
    public static string Click(
        UiTreeService uiTree,
        [Description("Coordinates [x, y]")] int[]? loc = null,
        [Description("UI element label from Snapshot")] string? label = null,
        [Description("Mouse button: left, right, middle")] string button = "left",
        [Description("Number of clicks: 0, 1, or 2")] int clicks = 1)
    {
        var (x, y) = ResolvePosition(uiTree, loc, label);

        User32.SetCursorPos(x, y);
        Thread.Sleep(50);

        if (clicks == 0) return $"Moved mouse to ({x}, {y}).";

        var (down, up) = button.ToLowerInvariant() switch
        {
            "left" => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
            "right" => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
            "middle" => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
            _ => throw new ArgumentException($"Unknown button: {button}")
        };

        for (int i = 0; i < clicks; i++)
        {
            SendMouseEvent(down);
            SendMouseEvent(up);
            if (i < clicks - 1) Thread.Sleep(50);
        }

        return $"Clicked {button} {clicks}x at ({x}, {y}).";
    }

    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Type text at a specific location or the current cursor position. Optionally clear the field first.")]
    public static string Type(
        UiTreeService uiTree,
        [Description("Text to type")] string text,
        [Description("Coordinates [x, y]")] int[]? loc = null,
        [Description("UI element label from Snapshot")] string? label = null,
        [Description("Clear existing text first")] bool clear = false,
        [Description("Press Enter after typing")] bool pressEnter = false)
    {
        if (loc is not null || label is not null)
        {
            var (x, y) = ResolvePosition(uiTree, loc, label);
            User32.SetCursorPos(x, y);
            Thread.Sleep(50);
            SendMouseEvent(User32.MOUSEEVENTF_LEFTDOWN);
            SendMouseEvent(User32.MOUSEEVENTF_LEFTUP);
            Thread.Sleep(100);
        }

        if (clear)
        {
            SendKeyCombo(0x11, 0x41); // Ctrl+A
            Thread.Sleep(50);
            SendKey(0x2E); // Delete
            Thread.Sleep(50);
        }

        foreach (var ch in text)
        {
            var inputs = new INPUT[2];
            inputs[0].Type = User32.INPUT_KEYBOARD;
            inputs[0].U.ki.wScan = ch;
            inputs[0].U.ki.dwFlags = User32.KEYEVENTF_UNICODE;

            inputs[1].Type = User32.INPUT_KEYBOARD;
            inputs[1].U.ki.wScan = ch;
            inputs[1].U.ki.dwFlags = User32.KEYEVENTF_UNICODE | User32.KEYEVENTF_KEYUP;

            User32.SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        if (pressEnter)
        {
            Thread.Sleep(50);
            SendKey(0x0D); // VK_RETURN
        }

        return $"Typed {text.Length} characters.";
    }

    [McpServerTool(Destructive = true),
     Description("Scroll content vertically or horizontally at the given position.")]
    public static string Scroll(
        [Description("Coordinates [x, y] to scroll at")] int[]? loc = null,
        [Description("Direction: up, down, left, right")] string direction = "down",
        [Description("Scroll type: vertical, horizontal")] string type = "vertical",
        [Description("Number of scroll increments")] int wheelTimes = 3)
    {
        if (loc is not null)
        {
            User32.SetCursorPos(loc[0], loc[1]);
            Thread.Sleep(50);
        }

        var scrollAmount = direction is "down" or "right" ? -120 : 120;

        for (int i = 0; i < wheelTimes; i++)
        {
            var input = new INPUT { Type = User32.INPUT_MOUSE };
            input.U.mi.mouseData = unchecked((uint)scrollAmount);
            input.U.mi.dwFlags = User32.MOUSEEVENTF_WHEEL;
            User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
            Thread.Sleep(50);
        }

        return $"Scrolled {direction} {wheelTimes} times.";
    }

    [McpServerTool(Destructive = true),
     Description("Move the mouse cursor to coordinates or a UI label. Optionally drag (hold left button).")]
    public static string Move(
        UiTreeService uiTree,
        [Description("Coordinates [x, y]")] int[]? loc = null,
        [Description("UI element label from Snapshot")] string? label = null,
        [Description("Hold left mouse button while moving (drag)")] bool drag = false)
    {
        var (x, y) = ResolvePosition(uiTree, loc, label);

        if (drag)
            SendMouseEvent(User32.MOUSEEVENTF_LEFTDOWN);

        User32.SetCursorPos(x, y);

        if (drag)
        {
            Thread.Sleep(100);
            SendMouseEvent(User32.MOUSEEVENTF_LEFTUP);
        }

        return $"Moved to ({x}, {y}){(drag ? " with drag" : "")}.";
    }

    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Execute a keyboard shortcut. Format: modifier+key (e.g. 'ctrl+c', 'alt+tab', 'ctrl+shift+s', 'win+r').")]
    public static string Shortcut(
        [Description("Keyboard shortcut string (e.g. 'ctrl+c', 'alt+f4')")] string shortcut)
    {
        var keys = shortcut.ToLowerInvariant().Split('+');
        var vkCodes = keys.Select(MapKeyName).ToList();

        // Press all keys down
        foreach (var vk in vkCodes)
            SendKeyDown(vk);

        Thread.Sleep(50);

        // Release in reverse order
        for (int i = vkCodes.Count - 1; i >= 0; i--)
            SendKeyUp(vkCodes[i]);

        return $"Executed shortcut: {shortcut}";
    }

    [McpServerTool(ReadOnly = true, Idempotent = true),
     Description("Wait/pause for a specified duration in seconds.")]
    public static string Wait(
        [Description("Duration in seconds")] double duration = 1.0)
    {
        Thread.Sleep(TimeSpan.FromSeconds(duration));
        return $"Waited {duration:F1} seconds.";
    }

    // --- Helpers ---

    private static (int X, int Y) ResolvePosition(UiTreeService uiTree, int[]? loc, string? label)
    {
        if (loc is { Length: >= 2 })
            return (loc[0], loc[1]);

        if (label is not null)
        {
            var resolved = uiTree.ResolveLabel(label);
            if (resolved.HasValue) return resolved.Value;
            throw new ArgumentException($"Element with label '{label}' not found. Run 'Snapshot' first to get current labels.");
        }

        throw new ArgumentException("Either 'loc' or 'label' must be provided.");
    }

    private static void SendMouseEvent(uint flags)
    {
        var input = new INPUT { Type = User32.INPUT_MOUSE };
        input.U.mi.dwFlags = flags;
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vk)
    {
        SendKeyDown(vk);
        SendKeyUp(vk);
    }

    private static void SendKeyDown(ushort vk)
    {
        var input = new INPUT { Type = User32.INPUT_KEYBOARD };
        input.U.ki.wVk = vk;
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyUp(ushort vk)
    {
        var input = new INPUT { Type = User32.INPUT_KEYBOARD };
        input.U.ki.wVk = vk;
        input.U.ki.dwFlags = User32.KEYEVENTF_KEYUP;
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyCombo(ushort modifier, ushort key)
    {
        SendKeyDown(modifier);
        SendKey(key);
        SendKeyUp(modifier);
    }

    private static ushort MapKeyName(string name) => name switch
    {
        "ctrl" or "control" => 0x11,
        "alt" => 0x12,
        "shift" => 0x10,
        "win" or "windows" or "super" => 0x5B,
        "tab" => 0x09,
        "enter" or "return" => 0x0D,
        "esc" or "escape" => 0x1B,
        "space" => 0x20,
        "backspace" or "back" => 0x08,
        "delete" or "del" => 0x2E,
        "home" => 0x24,
        "end" => 0x23,
        "pageup" or "pgup" => 0x21,
        "pagedown" or "pgdn" => 0x22,
        "up" => 0x26,
        "down" => 0x28,
        "left" => 0x25,
        "right" => 0x27,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
        "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
        "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        _ when name.Length == 1 => (ushort)(char.ToUpperInvariant(name[0])),
        _ => throw new ArgumentException($"Unknown key: {name}")
    };
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Tools/InputTools.cs
git commit -m "feat: implement InputTools (Click, Type, Scroll, Move, Shortcut, Wait)"
```

---

## Task 14: SnapshotTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/SnapshotTools.cs`

- [ ] **Step 1: Implement SnapshotTools**

Create `src/WindowsMCP.NET/Tools/SnapshotTools.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class SnapshotTools
{
    [McpServerTool(ReadOnly = true, Idempotent = true),
     Description("Capture the full desktop state: screenshot with annotated UI element labels and the UI element tree. Use this to understand what's on screen before interacting.")]
    public static IList<Content> Snapshot(
        ScreenCaptureService screenCapture,
        UiTreeService uiTree,
        [Description("Include screenshot image")] bool useVision = true,
        [Description("Include UI element tree text")] bool useDom = true,
        [Description("Annotate screenshot with element labels")] bool useAnnotation = true,
        [Description("Display indices to capture")] int[]? display = null)
    {
        var result = new List<Content>();

        // Always rebuild tree when Snapshot is called
        uiTree.InvalidateCache();
        var tree = uiTree.BuildAnnotatedTree();

        if (useVision)
        {
            var displayIndex = display is { Length: > 0 } ? display[0] : (int?)null;
            var pngBytes = screenCapture.CaptureScreen(displayIndex);

            if (useAnnotation)
            {
                var annotations = uiTree.GetAnnotationPoints();
                pngBytes = screenCapture.AnnotateScreenshot(pngBytes, annotations);
            }

            result.Add(new ImageContent
            {
                Data = Convert.ToBase64String(pngBytes),
                MimeType = "image/png",
            });
        }

        if (useDom)
        {
            result.Add(new TextContent
            {
                Text = tree.ToText(),
            });
        }

        return result;
    }

    [McpServerTool(ReadOnly = true, Idempotent = true),
     Description("Take a fast screenshot of the desktop. Does not build the full UI tree (use Snapshot for that). Optionally annotates with previously cached labels.")]
    public static IList<Content> Screenshot(
        ScreenCaptureService screenCapture,
        UiTreeService uiTree,
        [Description("Annotate with cached element labels")] bool useAnnotation = true,
        [Description("Display indices to capture")] int[]? display = null)
    {
        var displayIndex = display is { Length: > 0 } ? display[0] : (int?)null;
        var pngBytes = screenCapture.CaptureScreen(displayIndex);

        if (useAnnotation)
        {
            var annotations = uiTree.GetAnnotationPoints();
            if (annotations.Count > 0)
                pngBytes = screenCapture.AnnotateScreenshot(pngBytes, annotations);
        }

        return
        [
            new ImageContent
            {
                Data = Convert.ToBase64String(pngBytes),
                MimeType = "image/png",
            }
        ];
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.sln
```

- [ ] **Step 3: Commit**

```bash
git add src/WindowsMCP.NET/Tools/SnapshotTools.cs
git commit -m "feat: implement SnapshotTools (Snapshot with UI tree, Screenshot fast-capture)"
```

---

## Task 15: AppTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/AppTools.cs`

- [ ] **Step 1: Implement AppTools**

Create `src/WindowsMCP.NET/Tools/AppTools.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class AppTools
{
    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Manage applications. Modes: 'launch' (start an app), 'switch' (bring window to front by name), 'resize' (move/resize a window).")]
    public static string App(
        DesktopService desktop,
        [Description("Mode: launch, switch, resize")] string mode,
        [Description("Application name or window title")] string name,
        [Description("Window position [x, y] (for resize mode)")] int[]? windowLoc = null,
        [Description("Window size [width, height] (for resize mode)")] int[]? windowSize = null)
    {
        return mode.ToLowerInvariant() switch
        {
            "launch" => LaunchApp(desktop, name),
            "switch" => SwitchApp(desktop, name),
            "resize" => ResizeApp(desktop, name, windowLoc, windowSize),
            _ => throw new ArgumentException($"Unknown mode: {mode}. Use 'launch', 'switch', or 'resize'.")
        };
    }

    private static string LaunchApp(DesktopService desktop, string name)
    {
        var window = desktop.LaunchApp(name);
        return window is not null
            ? $"Launched '{name}'. Window: \"{window.Title}\" at ({window.X},{window.Y}) {window.Width}x{window.Height}"
            : $"Launched '{name}' but could not find its window.";
    }

    private static string SwitchApp(DesktopService desktop, string name)
    {
        var window = desktop.SwitchToWindow(name);
        return window is not null
            ? $"Switched to window: \"{window.Title}\""
            : $"No window matching '{name}' found.";
    }

    private static string ResizeApp(DesktopService desktop, string name, int[]? windowLoc, int[]? windowSize)
    {
        var window = desktop.SwitchToWindow(name);
        if (window is null) return $"No window matching '{name}' found.";

        var x = windowLoc is { Length: >= 2 } ? windowLoc[0] : window.X;
        var y = windowLoc is { Length: >= 2 } ? windowLoc[1] : window.Y;
        var w = windowSize is { Length: >= 2 } ? windowSize[0] : window.Width;
        var h = windowSize is { Length: >= 2 } ? windowSize[1] : window.Height;

        desktop.ResizeWindow(window.Handle, x, y, w, h);
        return $"Resized \"{window.Title}\" to ({x},{y}) {w}x{h}.";
    }
}
```

- [ ] **Step 2: Verify build and commit**

```bash
dotnet build WindowsMCP.NET.sln
git add src/WindowsMCP.NET/Tools/AppTools.cs
git commit -m "feat: implement AppTools (launch, switch, resize)"
```

---

## Task 16: SystemTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/SystemTools.cs`

- [ ] **Step 1: Implement SystemTools**

Create `src/WindowsMCP.NET/Tools/SystemTools.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Execute a PowerShell command and return its output.")]
    public static async Task<string> PowerShell(
        [Description("PowerShell command to execute")] string command,
        [Description("Timeout in seconds")] int timeout = 30)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var readOut = process.StandardOutput.ReadToEndAsync(cts.Token);
        var readErr = process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);

        stdout.Append(await readOut);
        stderr.Append(await readErr);

        var result = stdout.ToString();
        if (stderr.Length > 0)
            result += $"\n[STDERR]\n{stderr}";

        return string.IsNullOrWhiteSpace(result) ? "(no output)" : result;
    }

    [McpServerTool,
     Description("List or kill running processes. Modes: 'list' (enumerate running processes), 'kill' (terminate a process by name or PID).")]
    public static string Process(
        [Description("Mode: list, kill")] string mode,
        [Description("Process name filter (for list/kill)")] string? name = null,
        [Description("Process ID (for kill)")] int? pid = null,
        [Description("Sort by: memory, cpu, name")] string sortBy = "memory",
        [Description("Max results (for list)")] int limit = 20,
        [Description("Force kill")] bool force = false)
    {
        return mode.ToLowerInvariant() switch
        {
            "list" => ListProcesses(name, sortBy, limit),
            "kill" => KillProcess(name, pid, force),
            _ => throw new ArgumentException($"Unknown mode: {mode}. Use 'list' or 'kill'.")
        };
    }

    [McpServerTool,
     Description("Read, write, delete, or list Windows Registry entries.")]
    public static string Registry(
        [Description("Mode: get, set, delete, list")] string mode,
        [Description("Registry path (e.g. HKLM\\SOFTWARE\\...)")] string path,
        [Description("Value name")] string? name = null,
        [Description("Value to set")] string? value = null,
        [Description("Value type: String, DWord, QWord, Binary, MultiString, ExpandString")] string type = "String")
    {
        return mode.ToLowerInvariant() switch
        {
            "get" => RegistryGet(path, name),
            "set" => RegistrySet(path, name, value, type),
            "delete" => RegistryDelete(path, name),
            "list" => RegistryList(path),
            _ => throw new ArgumentException($"Unknown mode: {mode}. Use 'get', 'set', 'delete', or 'list'.")
        };
    }

    // --- Process helpers ---

    private static string ListProcesses(string? name, string sortBy, int limit)
    {
        var processes = System.Diagnostics.Process.GetProcesses().AsEnumerable();

        if (!string.IsNullOrEmpty(name))
            processes = processes.Where(p => p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));

        var sorted = sortBy.ToLowerInvariant() switch
        {
            "memory" => processes.OrderByDescending(SafeMemory),
            "name" => processes.OrderBy(p => p.ProcessName),
            _ => processes.OrderByDescending(SafeMemory),
        };

        var list = sorted.Take(limit).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"| PID | Name | Memory (MB) |");
        sb.AppendLine($"|-----|------|-------------|");
        foreach (var p in list)
            sb.AppendLine($"| {p.Id} | {p.ProcessName} | {SafeMemory(p) / (1024 * 1024):F1} |");

        return sb.ToString();
    }

    private static long SafeMemory(System.Diagnostics.Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }

    private static string KillProcess(string? name, int? pid, bool force)
    {
        if (pid.HasValue)
        {
            var p = System.Diagnostics.Process.GetProcessById(pid.Value);
            if (force) p.Kill(true); else p.Kill();
            return $"Killed process {p.ProcessName} (PID {pid}).";
        }

        if (!string.IsNullOrEmpty(name))
        {
            var procs = System.Diagnostics.Process.GetProcessesByName(name);
            foreach (var p in procs)
            {
                if (force) p.Kill(true); else p.Kill();
            }
            return $"Killed {procs.Length} process(es) named '{name}'.";
        }

        throw new ArgumentException("Provide 'name' or 'pid' to kill a process.");
    }

    // --- Registry helpers ---

    private static RegistryKey OpenBaseKey(string path, out string subPath)
    {
        var parts = path.Split('\\', 2);
        var hive = parts[0].ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Microsoft.Win32.Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Microsoft.Win32.Registry.Users,
            _ => throw new ArgumentException($"Unknown registry hive: {parts[0]}")
        };
        subPath = parts.Length > 1 ? parts[1] : "";
        return hive;
    }

    private static string RegistryGet(string path, string? name)
    {
        var baseKey = OpenBaseKey(path, out var subPath);
        using var key = baseKey.OpenSubKey(subPath)
            ?? throw new ArgumentException($"Registry key not found: {path}");
        var value = key.GetValue(name ?? "");
        return value?.ToString() ?? "(null)";
    }

    private static string RegistrySet(string path, string? name, string? value, string type)
    {
        var baseKey = OpenBaseKey(path, out var subPath);
        using var key = baseKey.CreateSubKey(subPath);
        var kind = type.ToLowerInvariant() switch
        {
            "string" => RegistryValueKind.String,
            "dword" => RegistryValueKind.DWord,
            "qword" => RegistryValueKind.QWord,
            "binary" => RegistryValueKind.Binary,
            "multistring" => RegistryValueKind.MultiString,
            "expandstring" => RegistryValueKind.ExpandString,
            _ => RegistryValueKind.String,
        };

        object typedValue = kind switch
        {
            RegistryValueKind.DWord => int.Parse(value ?? "0"),
            RegistryValueKind.QWord => long.Parse(value ?? "0"),
            _ => value ?? ""
        };

        key.SetValue(name ?? "", typedValue, kind);
        return $"Set {path}\\{name} = {value} ({type}).";
    }

    private static string RegistryDelete(string path, string? name)
    {
        var baseKey = OpenBaseKey(path, out var subPath);
        using var key = baseKey.OpenSubKey(subPath, writable: true)
            ?? throw new ArgumentException($"Registry key not found: {path}");

        if (name is not null)
        {
            key.DeleteValue(name);
            return $"Deleted value '{name}' from {path}.";
        }

        baseKey.DeleteSubKeyTree(subPath);
        return $"Deleted key {path} and all subkeys.";
    }

    private static string RegistryList(string path)
    {
        var baseKey = OpenBaseKey(path, out var subPath);
        using var key = baseKey.OpenSubKey(subPath)
            ?? throw new ArgumentException($"Registry key not found: {path}");

        var sb = new StringBuilder();
        sb.AppendLine($"Subkeys of {path}:");
        foreach (var sub in key.GetSubKeyNames())
            sb.AppendLine($"  [Key] {sub}");
        foreach (var val in key.GetValueNames())
            sb.AppendLine($"  {val} = {key.GetValue(val)} ({key.GetValueKind(val)})");

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify build and commit**

```bash
dotnet build WindowsMCP.NET.sln
git add src/WindowsMCP.NET/Tools/SystemTools.cs
git commit -m "feat: implement SystemTools (PowerShell, Process, Registry)"
```

---

## Task 17: ClipboardTools, FileSystemTools, NotificationTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/ClipboardTools.cs`
- Create: `src/WindowsMCP.NET/Tools/FileSystemTools.cs`
- Create: `src/WindowsMCP.NET/Tools/NotificationTools.cs`

- [ ] **Step 1: Implement ClipboardTools**

Create `src/WindowsMCP.NET/Tools/ClipboardTools.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ClipboardTools
{
    [McpServerTool,
     Description("Get or set the Windows clipboard text content. Mode: 'get' (read clipboard), 'set' (write to clipboard).")]
    public static string Clipboard(
        [Description("Mode: get, set")] string mode = "get",
        [Description("Text to copy (for set mode)")] string? text = null)
    {
        return mode.ToLowerInvariant() switch
        {
            "get" => GetClipboard(),
            "set" => SetClipboard(text ?? throw new ArgumentException("'text' is required for set mode.")),
            _ => throw new ArgumentException($"Unknown mode: {mode}. Use 'get' or 'set'.")
        };
    }

    private static string GetClipboard()
    {
        if (!User32.OpenClipboard(nint.Zero))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            var handle = User32.GetClipboardData(User32.CF_UNICODETEXT);
            if (handle == nint.Zero) return "(clipboard empty or not text)";

            var ptr = Kernel32.GlobalLock(handle);
            if (ptr == nint.Zero) return "(failed to lock clipboard data)";

            try
            {
                return Marshal.PtrToStringUni(ptr) ?? "";
            }
            finally
            {
                Kernel32.GlobalUnlock(handle);
            }
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    private static string SetClipboard(string text)
    {
        if (!User32.OpenClipboard(nint.Zero))
            throw new InvalidOperationException("Failed to open clipboard.");

        try
        {
            User32.EmptyClipboard();
            var bytes = (text.Length + 1) * 2; // UTF-16 + null
            var hMem = Kernel32.GlobalAlloc(Kernel32.GMEM_MOVEABLE, (nuint)bytes);
            var ptr = Kernel32.GlobalLock(hMem);
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0); // null terminator
            Kernel32.GlobalUnlock(hMem);
            User32.SetClipboardData(User32.CF_UNICODETEXT, hMem);
            return $"Clipboard set ({text.Length} chars).";
        }
        finally
        {
            User32.CloseClipboard();
        }
    }
}
```

- [ ] **Step 2: Implement FileSystemTools**

Create `src/WindowsMCP.NET/Tools/FileSystemTools.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class FileSystemTools
{
    [McpServerTool,
     Description("File and directory operations. Modes: read, write, copy, move, delete, list, search, info.")]
    public static string FileSystem(
        [Description("Mode: read, write, copy, move, delete, list, search, info")] string mode,
        [Description("File or directory path")] string path,
        [Description("Destination path (copy, move)")] string? destination = null,
        [Description("Content to write")] string? content = null,
        [Description("Search pattern (search mode)")] string? pattern = null,
        [Description("Recurse into subdirectories")] bool recursive = false,
        [Description("Append instead of overwrite (write mode)")] bool append = false,
        [Description("Overwrite existing files (copy, move)")] bool overwrite = false,
        [Description("Read offset in bytes")] int offset = 0,
        [Description("Read limit in bytes (0 = all)")] int limit = 0,
        [Description("File encoding")] string encoding = "utf-8")
    {
        return mode.ToLowerInvariant() switch
        {
            "read" => ReadFile(path, offset, limit, encoding),
            "write" => WriteFile(path, content ?? "", append, encoding),
            "copy" => CopyItem(path, destination ?? throw new ArgumentException("'destination' required"), overwrite),
            "move" => MoveItem(path, destination ?? throw new ArgumentException("'destination' required"), overwrite),
            "delete" => DeleteItem(path, recursive),
            "list" => ListDirectory(path, recursive),
            "search" => SearchFiles(path, pattern ?? "*", recursive),
            "info" => GetInfo(path),
            _ => throw new ArgumentException($"Unknown mode: {mode}")
        };
    }

    private static string ReadFile(string path, int offset, int limit, string encoding)
    {
        var enc = Encoding.GetEncoding(encoding);
        var text = File.ReadAllText(path, enc);
        if (offset > 0) text = text[Math.Min(offset, text.Length)..];
        if (limit > 0) text = text[..Math.Min(limit, text.Length)];
        return text;
    }

    private static string WriteFile(string path, string content, bool append, string encoding)
    {
        var enc = Encoding.GetEncoding(encoding);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);

        if (append)
            File.AppendAllText(path, content, enc);
        else
            File.WriteAllText(path, content, enc);

        return $"Written {content.Length} chars to {path}.";
    }

    private static string CopyItem(string src, string dst, bool overwrite)
    {
        if (File.Exists(src))
        {
            var dir = Path.GetDirectoryName(dst);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Copy(src, dst, overwrite);
            return $"Copied file {src} -> {dst}.";
        }

        if (Directory.Exists(src))
        {
            CopyDirectory(src, dst, overwrite);
            return $"Copied directory {src} -> {dst}.";
        }

        throw new FileNotFoundException($"Source not found: {src}");
    }

    private static void CopyDirectory(string src, string dst, bool overwrite)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)), overwrite);
    }

    private static string MoveItem(string src, string dst, bool overwrite)
    {
        if (File.Exists(src))
        {
            var dir = Path.GetDirectoryName(dst);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Move(src, dst, overwrite);
            return $"Moved file {src} -> {dst}.";
        }

        if (Directory.Exists(src))
        {
            Directory.Move(src, dst);
            return $"Moved directory {src} -> {dst}.";
        }

        throw new FileNotFoundException($"Source not found: {src}");
    }

    private static string DeleteItem(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return $"Deleted file {path}.";
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
            return $"Deleted directory {path}.";
        }

        throw new FileNotFoundException($"Not found: {path}");
    }

    private static string ListDirectory(string path, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var sb = new StringBuilder();

        foreach (var dir in Directory.GetDirectories(path, "*", option))
            sb.AppendLine($"[DIR]  {dir}");
        foreach (var file in Directory.GetFiles(path, "*", option))
        {
            var info = new FileInfo(file);
            sb.AppendLine($"[FILE] {file} ({info.Length:N0} bytes)");
        }

        return sb.Length > 0 ? sb.ToString() : "(empty directory)";
    }

    private static string SearchFiles(string path, string pattern, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(path, pattern, option);
        var sb = new StringBuilder();
        sb.AppendLine($"Found {files.Length} file(s) matching '{pattern}':");
        foreach (var f in files.Take(100))
            sb.AppendLine($"  {f}");
        if (files.Length > 100) sb.AppendLine($"  ... and {files.Length - 100} more");
        return sb.ToString();
    }

    private static string GetInfo(string path)
    {
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return $"File: {fi.FullName}\nSize: {fi.Length:N0} bytes\nCreated: {fi.CreationTime}\nModified: {fi.LastWriteTime}\nReadOnly: {fi.IsReadOnly}";
        }

        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            return $"Directory: {di.FullName}\nCreated: {di.CreationTime}\nModified: {di.LastWriteTime}\nFiles: {di.GetFiles().Length}\nSubdirs: {di.GetDirectories().Length}";
        }

        throw new FileNotFoundException($"Not found: {path}");
    }
}
```

- [ ] **Step 3: Implement NotificationTools**

Create `src/WindowsMCP.NET/Tools/NotificationTools.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class NotificationTools
{
    [McpServerTool(Destructive = true, OpenWorld = true),
     Description("Send a Windows toast notification with a title and message.")]
    public static string Notification(
        [Description("Notification title")] string title,
        [Description("Notification message")] string message)
    {
        // Use PowerShell for toast notification (avoids COM interop complexity and AOT issues)
        var escapedTitle = title.Replace("'", "''");
        var escapedMessage = message.Replace("'", "''");

        var script = $"""
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
            $xml = @'
            <toast>
              <visual>
                <binding template='ToastGeneric'>
                  <text>{escapedTitle}</text>
                  <text>{escapedMessage}</text>
                </binding>
              </visual>
            </toast>
            '@
            $doc = New-Object Windows.Data.Xml.Dom.XmlDocument
            $doc.LoadXml($xml)
            $toast = [Windows.UI.Notifications.ToastNotification]::new($doc)
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('WindowsMCP.NET').Show($toast)
            """;

        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        process?.WaitForExit(5000);

        return $"Notification sent: {title}";
    }
}
```

- [ ] **Step 4: Verify build and commit**

```bash
dotnet build WindowsMCP.NET.sln
git add src/WindowsMCP.NET/Tools/ClipboardTools.cs src/WindowsMCP.NET/Tools/FileSystemTools.cs src/WindowsMCP.NET/Tools/NotificationTools.cs
git commit -m "feat: implement ClipboardTools, FileSystemTools, NotificationTools"
```

---

## Task 18: MultiTools & ScrapeTools

**Files:**
- Create: `src/WindowsMCP.NET/Tools/MultiTools.cs`
- Create: `src/WindowsMCP.NET/Tools/ScrapeTools.cs`

- [ ] **Step 1: Implement MultiTools**

Create `src/WindowsMCP.NET/Tools/MultiTools.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using ModelContextProtocol.Server;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class MultiTools
{
    [McpServerTool(Destructive = true),
     Description("Select multiple UI elements by clicking them with Ctrl held. Provide either coordinates or labels from Snapshot.")]
    public static string MultiSelect(
        UiTreeService uiTree,
        [Description("List of [x, y] coordinates")] int[][]? locs = null,
        [Description("List of UI element labels")] string[]? labels = null,
        [Description("Hold Ctrl while clicking")] bool pressCtrl = true)
    {
        var points = ResolveMultiplePositions(uiTree, locs, labels);

        if (pressCtrl) SendKeyDown(0x11); // Ctrl

        foreach (var (x, y) in points)
        {
            User32.SetCursorPos(x, y);
            Thread.Sleep(50);
            SendClick();
            Thread.Sleep(100);
        }

        if (pressCtrl) SendKeyUp(0x11);

        return $"Selected {points.Count} elements.";
    }

    [McpServerTool(Destructive = true),
     Description("Edit multiple input fields. Provide either locs as [[x,y,text],...] or labels as [[label,text],...].")]
    public static string MultiEdit(
        UiTreeService uiTree,
        [Description("List of [x, y, text] entries")] string[][]? locs = null,
        [Description("List of [label, text] entries")] string[][]? labels = null)
    {
        int count = 0;

        if (locs is not null)
        {
            foreach (var entry in locs)
            {
                if (entry.Length < 3) continue;
                var x = int.Parse(entry[0]);
                var y = int.Parse(entry[1]);
                var text = entry[2];
                ClickAndType(x, y, text);
                count++;
            }
        }
        else if (labels is not null)
        {
            foreach (var entry in labels)
            {
                if (entry.Length < 2) continue;
                var pos = uiTree.ResolveLabel(entry[0])
                    ?? throw new ArgumentException($"Label '{entry[0]}' not found.");
                ClickAndType(pos.X, pos.Y, entry[1]);
                count++;
            }
        }
        else
        {
            throw new ArgumentException("Provide either 'locs' or 'labels'.");
        }

        return $"Edited {count} fields.";
    }

    // --- Helpers ---

    private static List<(int X, int Y)> ResolveMultiplePositions(UiTreeService uiTree, int[][]? locs, string[]? labels)
    {
        if (locs is not null)
            return locs.Where(l => l.Length >= 2).Select(l => (l[0], l[1])).ToList();

        if (labels is not null)
            return labels.Select(label =>
                uiTree.ResolveLabel(label)
                ?? throw new ArgumentException($"Label '{label}' not found.")
            ).ToList();

        throw new ArgumentException("Provide either 'locs' or 'labels'.");
    }

    private static void ClickAndType(int x, int y, string text)
    {
        User32.SetCursorPos(x, y);
        Thread.Sleep(50);
        SendClick();
        Thread.Sleep(100);

        // Select all and delete
        SendKeyDown(0x11); // Ctrl
        SendKeyPress(0x41); // A
        SendKeyUp(0x11);
        Thread.Sleep(30);
        SendKeyPress(0x2E); // Delete
        Thread.Sleep(30);

        // Type text
        foreach (var ch in text)
        {
            var inputs = new INPUT[2];
            inputs[0].Type = User32.INPUT_KEYBOARD;
            inputs[0].U.ki.wScan = ch;
            inputs[0].U.ki.dwFlags = User32.KEYEVENTF_UNICODE;
            inputs[1].Type = User32.INPUT_KEYBOARD;
            inputs[1].U.ki.wScan = ch;
            inputs[1].U.ki.dwFlags = User32.KEYEVENTF_UNICODE | User32.KEYEVENTF_KEYUP;
            User32.SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static void SendClick()
    {
        var down = new INPUT { Type = User32.INPUT_MOUSE };
        down.U.mi.dwFlags = User32.MOUSEEVENTF_LEFTDOWN;
        var up = new INPUT { Type = User32.INPUT_MOUSE };
        up.U.mi.dwFlags = User32.MOUSEEVENTF_LEFTUP;
        User32.SendInput(2, [down, up], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyDown(ushort vk)
    {
        var input = new INPUT { Type = User32.INPUT_KEYBOARD };
        input.U.ki.wVk = vk;
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyUp(ushort vk)
    {
        var input = new INPUT { Type = User32.INPUT_KEYBOARD };
        input.U.ki.wVk = vk;
        input.U.ki.dwFlags = User32.KEYEVENTF_KEYUP;
        User32.SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyPress(ushort vk)
    {
        SendKeyDown(vk);
        SendKeyUp(vk);
    }
}
```

- [ ] **Step 2: Implement ScrapeTools**

Create `src/WindowsMCP.NET/Tools/ScrapeTools.cs`:

```csharp
using System.ComponentModel;
using System.Net.Http;
using ModelContextProtocol.Server;
using ReverseMarkdown;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ScrapeTools
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "WindowsMCP.NET/0.1" }
        }
    };

    [McpServerTool(ReadOnly = true, OpenWorld = true),
     Description("Fetch a web page and return its content as Markdown. Optionally filter by a query.")]
    public static async Task<string> Scrape(
        [Description("URL to fetch")] string url,
        [Description("Optional text query to filter relevant content")] string? query = null)
    {
        var response = await HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var converter = new Converter(new ReverseMarkdown.Config
        {
            RemoveComments = true,
            SmartHrefHandling = true,
            GithubFlavored = true,
        });

        var markdown = converter.Convert(html);

        // Basic content filtering by query
        if (!string.IsNullOrWhiteSpace(query))
        {
            var lines = markdown.Split('\n');
            var relevant = lines.Where(line =>
                line.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (relevant.Count > 0)
            {
                return $"Filtered results for '{query}':\n\n{string.Join('\n', relevant)}";
            }
        }

        // Truncate very long pages
        if (markdown.Length > 50_000)
            markdown = markdown[..50_000] + "\n\n...(truncated)";

        return markdown;
    }
}
```

- [ ] **Step 3: Verify build and commit**

```bash
dotnet build WindowsMCP.NET.sln
git add src/WindowsMCP.NET/Tools/MultiTools.cs src/WindowsMCP.NET/Tools/ScrapeTools.cs
git commit -m "feat: implement MultiTools (MultiSelect, MultiEdit) and ScrapeTools"
```

---

## Task 19: Integration Test — FileSystemTools

**Files:**
- Create: `tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs`

- [ ] **Step 1: Write FileSystemTools tests**

Create `tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs`:

```csharp
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Tests.Tools;

public class FileSystemToolsTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wmcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void WriteAndRead_RoundTrips()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        FileSystemTools.FileSystem("write", filePath, content: "Hello World");
        var result = FileSystemTools.FileSystem("read", filePath);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Write_Append()
    {
        var filePath = Path.Combine(_tempDir, "append.txt");
        FileSystemTools.FileSystem("write", filePath, content: "Line1");
        FileSystemTools.FileSystem("write", filePath, content: "\nLine2", append: true);
        var result = FileSystemTools.FileSystem("read", filePath);
        Assert.Equal("Line1\nLine2", result);
    }

    [Fact]
    public void List_ShowsFilesAndDirs()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        var result = FileSystemTools.FileSystem("list", _tempDir);
        Assert.Contains("subdir", result);
        Assert.Contains("a.txt", result);
    }

    [Fact]
    public void Copy_CopiesFile()
    {
        var src = Path.Combine(_tempDir, "src.txt");
        var dst = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(src, "copy me");
        FileSystemTools.FileSystem("copy", src, destination: dst);
        Assert.True(File.Exists(dst));
        Assert.Equal("copy me", File.ReadAllText(dst));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var filePath = Path.Combine(_tempDir, "delete_me.txt");
        File.WriteAllText(filePath, "");
        FileSystemTools.FileSystem("delete", filePath);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Search_FindsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "match.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "other.txt"), "");
        var result = FileSystemTools.FileSystem("search", _tempDir, pattern: "*.cs");
        Assert.Contains("match.cs", result);
        Assert.DoesNotContain("other.txt", result);
    }

    [Fact]
    public void Info_ReturnsFileDetails()
    {
        var filePath = Path.Combine(_tempDir, "info.txt");
        File.WriteAllText(filePath, "12345");
        var result = FileSystemTools.FileSystem("info", filePath);
        Assert.Contains("5", result); // size in bytes
        Assert.Contains("info.txt", result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/WindowsMCP.NET.Tests/ --filter "FullyQualifiedName~FileSystemToolsTests"
```

Expected: 7 tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs
git commit -m "test: add FileSystemTools integration tests"
```

---

## Task 20: GitHub Actions CI

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Create CI workflow**

Create `.github/workflows/build.yml`:

```yaml
name: Build & Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET 9
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'

    - name: Restore
      run: dotnet restore WindowsMCP.NET.sln

    - name: Build
      run: dotnet build WindowsMCP.NET.sln --configuration Release --no-restore

    - name: Test
      run: dotnet test WindowsMCP.NET.sln --configuration Release --no-build --filter "Category!=Integration"

    - name: Publish (AOT)
      run: dotnet publish src/WindowsMCP.NET/WindowsMCP.NET.csproj -c Release -r win-x64

    - name: Upload artifact
      uses: actions/upload-artifact@v4
      with:
        name: WindowsMCP.NET
        path: src/WindowsMCP.NET/bin/Release/net9.0-windows/win-x64/publish/
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci: add GitHub Actions build and test workflow"
```

---

## Task 21: README & Final Polish

**Files:**
- Create: `README.md`

- [ ] **Step 1: Create README**

Create `README.md`:

```markdown
# WindowsMCP.NET

A .NET MCP (Model Context Protocol) server for Windows desktop automation. Remote-first with Streamable HTTP transport, ships as a portable single-file executable.

Inspired by [Windows-MCP](https://github.com/CursorTouch/Windows-MCP) (Python), reimplemented in C# with enhanced UI Automation via [FlaUI](https://github.com/FlaUI/FlaUI).

## Features

- **18 MCP Tools** for full Windows desktop control
- **Remote-first** — Streamable HTTP with API key auth and HTTPS
- **Portable** — Single `.exe`, no .NET runtime needed
- **UI Automation** — FlaUI (UIA3) for reliable element interaction
- **Easy Setup** — Interactive wizard, 3-step deployment

## Quick Start

### 1. Download

Grab the latest `WindowsMCP.NET.exe` from [Releases](../../releases).

### 2. Run

```bash
WindowsMCP.NET.exe
```

The setup wizard runs automatically on first start:
- Generates an API key
- Creates a self-signed HTTPS certificate
- Shows the Claude Code config snippet

### 3. Connect

Add the displayed JSON to your Claude Code settings:

```json
{
  "mcpServers": {
    "windows-mcp-dotnet": {
      "type": "streamable-http",
      "url": "https://YOUR-PC:8000/mcp",
      "headers": {
        "Authorization": "Bearer wmcp_your_key_here"
      }
    }
  }
}
```

## Tools

| Category | Tools |
|----------|-------|
| **Input** | Click, Type, Scroll, Move, Shortcut, Wait |
| **Screen** | Snapshot (screenshot + UI tree), Screenshot |
| **Apps** | App (launch, switch, resize) |
| **System** | PowerShell, Process, Registry |
| **Data** | Clipboard, FileSystem |
| **UI** | MultiSelect, MultiEdit |
| **Other** | Notification, Scrape |

## CLI

```bash
WindowsMCP.NET.exe                    # Start server (HTTP)
WindowsMCP.NET.exe --transport stdio  # Start in stdio mode
WindowsMCP.NET.exe setup              # Run setup wizard
WindowsMCP.NET.exe setup --new-key    # Generate new API key
WindowsMCP.NET.exe info               # Show config snippet
```

## Building from Source

```bash
dotnet build WindowsMCP.NET.sln
dotnet publish src/WindowsMCP.NET/WindowsMCP.NET.csproj -c Release -r win-x64
```

## License

MIT
```

- [ ] **Step 2: Verify full build and tests**

```bash
dotnet build WindowsMCP.NET.sln
dotnet test WindowsMCP.NET.sln
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add README with quickstart and tool overview"
```

---

## Self-Review Results

**Spec coverage check:**
- All 18 tools implemented: Click, Type, Scroll, Move, Shortcut, Wait, Snapshot, Screenshot, App, PowerShell, Process, Registry, Clipboard, FileSystem, Notification, MultiSelect, MultiEdit, Scrape
- 4 services: DesktopService, ScreenCaptureService, UiAutomationService, UiTreeService
- Native layer: User32, Kernel32 (Shell32 covered via PowerShell in NotificationTools, Dxgi deferred to GDI+ fallback)
- Config + CLI parsing
- Setup wizard + certificate generation
- Security middleware (API key + IP allowlist)
- Dual transport (HTTP + stdio)
- GitHub Actions CI
- README

**Deferred items (noted in spec as future):**
- DXGI Desktop Duplication (GDI+ works as fallback, DXGI can be added later)
- Rate limiting middleware (mentioned in spec, can be added via `app.UseRateLimiter()` when needed)
- DPAPI for cert password encryption (config stores plaintext for now — functional but can be hardened later)

**Placeholder scan:** No TBDs, TODOs, or incomplete sections found.

**Type consistency check:**
- `UiTreeService.ResolveLabel()` returns `(int X, int Y)?` — used consistently in InputTools, MultiTools
- `ScreenCaptureService.CaptureScreen()` returns `byte[]` — used consistently in SnapshotTools
- `DesktopService` methods match usage in AppTools
- `AppConfig` / `ConfigManager` / `CliOptions` used consistently in Program.cs and SetupWizard
- `AnnotatedTree.ToText()` used in SnapshotTools, `GetAnnotationPoints()` used for overlay
