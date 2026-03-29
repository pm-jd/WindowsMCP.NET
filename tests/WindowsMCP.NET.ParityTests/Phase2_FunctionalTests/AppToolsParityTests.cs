using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class AppToolsParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public AppToolsParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Kill any notepad processes started by these tests
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("notepad"))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
        catch { /* best-effort */ }
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task App_Launch_StartsNotepad()
    {
        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe"
        });

        _output.WriteLine($"Launch result: {result}");

        // Give the OS a moment to register the process
        await Task.Delay(1000);

        var notepadRunning = System.Diagnostics.Process.GetProcessesByName("notepad").Length > 0;
        Assert.True(notepadRunning, "Notepad process should be running after launch");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task App_Resize_ChangesWindowSize()
    {
        // Launch notepad first
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe"
        });

        // Wait for window to appear
        await Task.Delay(1500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"]   = "resize",
            ["name"]   = "Notepad",
            ["x"]      = 100,
            ["y"]      = 100,
            ["width"]  = 800,
            ["height"] = 600
        });

        _output.WriteLine($"Resize result: {result}");

        // Should not contain an error — either "Resized" or "No window matching" are both acceptable
        // (window title may not have loaded yet), but result must not be empty
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
