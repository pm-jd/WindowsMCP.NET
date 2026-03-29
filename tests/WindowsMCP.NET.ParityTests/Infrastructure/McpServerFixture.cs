using ModelContextProtocol.Client;
using Xunit;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed class McpServerFixture : IAsyncLifetime
{
    private McpClient? _client;
    public McpClient Client => _client ?? throw new InvalidOperationException("Not initialized");
    public string ServerType { get; private set; } = "dotnet";

    public async Task InitializeAsync()
    {
        ServerType = Environment.GetEnvironmentVariable("PARITY_SERVER") ?? "dotnet";

        StdioClientTransportOptions transportOptions;
        if (ServerType == "python")
        {
            var cmd = Environment.GetEnvironmentVariable("PYTHON_MCP_CMD") ?? "uvx";
            transportOptions = new StdioClientTransportOptions
            {
                Name = "windows-mcp-python",
                Command = cmd,
                Arguments = cmd == "uvx" ? ["windows-mcp"] : [],
            };
        }
        else
        {
            var exePath = Environment.GetEnvironmentVariable("DOTNET_MCP_PATH") ?? FindDotnetExe();
            transportOptions = new StdioClientTransportOptions
            {
                Name = "windows-mcp-dotnet",
                Command = exePath,
                Arguments = ["--transport", "stdio"],
            };
        }

        var clientTransport = new StdioClientTransport(transportOptions);
        _client = await McpClient.CreateAsync(clientTransport);
    }

    public async Task DisposeAsync()
    {
        if (_client is IAsyncDisposable d) await d.DisposeAsync();
        _client = null;
    }

    private static string FindDotnetExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "WindowsMCP.NET", "bin", "Debug", "net9.0-windows", "win-x64", "WindowsMCP.NET.exe"),
            Path.Combine(repoRoot, "src", "WindowsMCP.NET", "bin", "Release", "net9.0-windows", "win-x64", "WindowsMCP.NET.exe"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("WindowsMCP.NET.exe not found. Build first or set DOTNET_MCP_PATH.");
    }
}
