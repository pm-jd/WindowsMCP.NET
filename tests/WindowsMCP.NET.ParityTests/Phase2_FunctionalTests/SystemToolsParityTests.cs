using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class SystemToolsParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    // Temp directories created during tests — cleaned up in DisposeAsync
    private readonly List<string> _tempDirs = new();

    public SystemToolsParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task PowerShell_Echo_ReturnsOutput()
    {
        var result = await _client.CallToolTextAsync("PowerShell", new Dictionary<string, object?>
        {
            ["command"] = "Write-Output 'parity_test_hello'"
        });

        _output.WriteLine($"Output: {result}");
        Assert.Contains("parity_test_hello", result);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task PowerShell_ErrorCommand_ReturnsError()
    {
        var result = await _client.CallToolTextAsync("PowerShell", new Dictionary<string, object?>
        {
            ["command"] = "Get-Item 'C:\\ThisPathDoesNotExist_ParityTest_12345' -ErrorAction Stop"
        });

        _output.WriteLine($"Output: {result}");
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task Process_List_ReturnsProcesses()
    {
        var result = await _client.CallToolTextAsync("Process", new Dictionary<string, object?>
        {
            ["mode"] = "list"
        });

        _output.WriteLine($"Output (first 500 chars): {result[..Math.Min(500, result.Length)]}");

        // Should contain at least one of these always-running Windows processes
        var commonProcesses = new[] { "System", "svchost", "explorer", "lsass" };
        var found = commonProcesses.Any(p => result.Contains(p, StringComparison.OrdinalIgnoreCase));
        Assert.True(found, $"Expected at least one common process in output. Got:\n{result[..Math.Min(200, result.Length)]}");
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task Registry_Get_ReturnsWindowsVersion()
    {
        var result = await _client.CallToolTextAsync("Registry", new Dictionary<string, object?>
        {
            ["mode"]      = "get",
            ["key"]       = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            ["valueName"] = "ProductName"
        });

        _output.WriteLine($"Output: {result}");
        Assert.Contains("Windows", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task FileSystem_WriteAndRead_Roundtrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "parity_test_" + Guid.NewGuid().ToString("N")[..8]);
        _tempDirs.Add(tempDir);
        Directory.CreateDirectory(tempDir);

        var testFile = Path.Combine(tempDir, "roundtrip.txt");
        var testContent = $"parity_roundtrip_{Guid.NewGuid():N}";

        // Write
        var writeResult = await _client.CallToolTextAsync("FileSystem", new Dictionary<string, object?>
        {
            ["mode"]    = "write",
            ["path"]    = testFile,
            ["content"] = testContent
        });
        _output.WriteLine($"Write result: {writeResult}");
        Assert.Contains("Written", writeResult, StringComparison.OrdinalIgnoreCase);

        // Read back
        var readResult = await _client.CallToolTextAsync("FileSystem", new Dictionary<string, object?>
        {
            ["mode"] = "read",
            ["path"] = testFile
        });
        _output.WriteLine($"Read result: {readResult}");
        Assert.Equal(testContent, readResult);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task FileSystem_List_ShowsFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "parity_test_" + Guid.NewGuid().ToString("N")[..8]);
        _tempDirs.Add(tempDir);
        Directory.CreateDirectory(tempDir);

        // Create a couple of test files directly (not via MCP)
        var fileName1 = "parity_alpha.txt";
        var fileName2 = "parity_beta.txt";
        File.WriteAllText(Path.Combine(tempDir, fileName1), "alpha");
        File.WriteAllText(Path.Combine(tempDir, fileName2), "beta");

        var result = await _client.CallToolTextAsync("FileSystem", new Dictionary<string, object?>
        {
            ["mode"] = "list",
            ["path"] = tempDir
        });

        _output.WriteLine($"List result: {result}");
        Assert.Contains(fileName1, result);
        Assert.Contains(fileName2, result);
    }
}
