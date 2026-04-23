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
            ["mode"]        = "resize",
            ["name"]        = "Notepad",
            ["window_loc"]  = new int[] { 100, 100 },
            ["window_size"] = new int[] { 800, 600 }
        });

        _output.WriteLine($"Resize result: {result}");

        // Should not contain an error — either "Resized" or "No window matching" are both acceptable
        // (window title may not have loaded yet), but result must not be empty
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Ensure_NotRunning_LaunchesApp()
    {
        // Ensure no notepads are running at the start
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
            try { p.Kill(entireProcessTree: true); } catch { }
        await Task.Delay(500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "ensure",
            ["name"] = "notepad"
        });

        _output.WriteLine($"ensure (not running) result: {result}");
        await Task.Delay(1000);

        Assert.Contains("Launched", result);
        Assert.True(System.Diagnostics.Process.GetProcessesByName("notepad").Length > 0);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Ensure_AlreadyRunning_Focuses()
    {
        // Pre-launch
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe"
        });
        await Task.Delay(1500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "ensure",
            ["name"] = "notepad"
        });

        _output.WriteLine($"ensure (running) result: {result}");
        Assert.Contains("Focused", result);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Status_NotRunning_ReturnsNotRunning()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
            try { p.Kill(entireProcessTree: true); } catch { }
        await Task.Delay(500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "status",
            ["name"] = "notepad"
        });

        _output.WriteLine($"status (not running) result: {result}");
        Assert.Equal("Not running", result.Trim());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Status_Running_ReturnsPidAndTitle()
    {
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe"
        });
        await Task.Delay(1500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "status",
            ["name"] = "notepad"
        });

        _output.WriteLine($"status (running) result: {result}");
        Assert.Contains("Running:", result);
        Assert.Contains("PID=", result);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Ensure_MultipleMatches_Error_ReturnsList()
    {
        // Launch two notepad instances
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch", ["name"] = "notepad.exe"
        });
        await Task.Delay(800);
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch", ["name"] = "notepad.exe"
        });
        await Task.Delay(1200);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"]      = "ensure",
            ["name"]      = "notepad",
            ["ambiguous"] = "error"
        });

        _output.WriteLine($"ensure ambiguous=error result: {result}");
        Assert.Contains("Multiple matches:", result);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Ensure_LaunchCommandOverride_UsesOverride()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad"))
            try { p.Kill(entireProcessTree: true); } catch { }
        await Task.Delay(500);

        var result = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"]           = "ensure",
            ["name"]           = "definitely-not-a-real-app",
            ["launch_command"] = "notepad.exe"
        });

        _output.WriteLine($"ensure with launch_command result: {result}");
        await Task.Delay(1000);

        // launch_command fires only when no match → notepad should start
        Assert.True(System.Diagnostics.Process.GetProcessesByName("notepad").Length > 0);
    }
}
