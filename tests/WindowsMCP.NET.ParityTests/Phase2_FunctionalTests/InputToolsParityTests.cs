using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Collection("McpServer")]
public class InputToolsParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public InputToolsParityTests(McpServerFixture fixture, ITestOutputHelper output)
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
            foreach (var proc in Process.GetProcessesByName("notepad"))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
        catch { /* best-effort */ }
        await Task.CompletedTask;
    }

    // P/Invoke for GetCursorPos
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Move_SetsCursorPosition()
    {
        const int targetX = 300;
        const int targetY = 200;

        var result = await _client.CallToolTextAsync("Move", new Dictionary<string, object?>
        {
            ["x"] = targetX,
            ["y"] = targetY
        });

        _output.WriteLine($"Move result: {result}");

        // Verify cursor position via Win32
        GetCursorPos(out var pt);
        _output.WriteLine($"Cursor position after Move: ({pt.X}, {pt.Y})");

        // Allow a small tolerance (DPI scaling can cause minor offsets)
        Assert.InRange(pt.X, targetX - 5, targetX + 5);
        Assert.InRange(pt.Y, targetY - 5, targetY + 5);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Wait_TakesExpectedTime()
    {
        const int waitMs = 500;
        const int toleranceMs = 200; // allow generous tolerance for test environment

        var sw = Stopwatch.StartNew();
        var result = await _client.CallToolTextAsync("Wait", new Dictionary<string, object?>
        {
            ["ms"] = waitMs
        });
        sw.Stop();

        _output.WriteLine($"Wait result: {result}");
        _output.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms");

        Assert.True(sw.ElapsedMilliseconds >= waitMs - toleranceMs,
            $"Wait should take at least ~{waitMs}ms, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Type_InNotepad_ProducesText()
    {
        const string testText = "parity_type_test";

        // Launch notepad
        await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe"
        });

        // Wait for notepad to open and be ready
        await Task.Delay(2000);

        // Click roughly center of screen to focus notepad's text area
        // (Use Move first to ensure focus is reasonable)
        await _client.CallToolTextAsync("Move", new Dictionary<string, object?>
        {
            ["x"] = 640,
            ["y"] = 400
        });

        // Click to focus notepad
        await _client.CallToolAsync("Click", new Dictionary<string, object?>
        {
            ["x"] = 640,
            ["y"] = 400
        });
        await Task.Delay(300);

        // Type the test text
        var typeResult = await _client.CallToolTextAsync("Type", new Dictionary<string, object?>
        {
            ["text"] = testText
        });
        _output.WriteLine($"Type result: {typeResult}");
        await Task.Delay(300);

        // Select all (ctrl+a) and copy (ctrl+c) to get text into clipboard
        await _client.CallToolTextAsync("Shortcut", new Dictionary<string, object?>
        {
            ["keys"] = "ctrl+a"
        });
        await Task.Delay(200);

        await _client.CallToolTextAsync("Shortcut", new Dictionary<string, object?>
        {
            ["keys"] = "ctrl+c"
        });
        await Task.Delay(300);

        // Read clipboard to verify typed text
        var clipboardContent = await _client.CallToolTextAsync("Clipboard", new Dictionary<string, object?>
        {
            ["mode"] = "get"
        });
        _output.WriteLine($"Clipboard after typing: {clipboardContent}");

        Assert.Contains(testText, clipboardContent);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Shortcut_Executes()
    {
        // Use a safe shortcut that doesn't change application state destructively.
        // ctrl+shift+esc opens Task Manager but we'll use a simpler shortcut for parity test.
        // We'll press 'escape' by itself — it should be accepted without error.
        var result = await _client.CallToolTextAsync("Shortcut", new Dictionary<string, object?>
        {
            ["keys"] = "esc"
        });

        _output.WriteLine($"Shortcut result: {result}");
        Assert.False(string.IsNullOrWhiteSpace(result));
        // Should contain "Sent shortcut: esc"
        Assert.Contains("esc", result, StringComparison.OrdinalIgnoreCase);
    }
}
