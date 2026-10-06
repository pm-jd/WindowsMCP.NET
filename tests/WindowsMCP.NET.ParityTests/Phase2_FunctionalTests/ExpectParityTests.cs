using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

/// <summary>
/// Desktop tests for Expect against a real application. Same Notepad handling as
/// <see cref="ObserveParityTests"/>: every test launches its own Notepad and kills only the notepad
/// processes that did not exist before it started; elements are picked by control type or id, never by
/// (localized) name.
/// </summary>
[Collection("McpServer")]
public class ExpectParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;
    private HashSet<int> _preexistingNotepads = [];
    private bool _launched;

    public ExpectParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public ValueTask InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
        _preexistingNotepads = NotepadPids(); // snapshot before anything can fail
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        KillNewNotepads();
        return ValueTask.CompletedTask;
    }

    private static HashSet<int> NotepadPids()
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("notepad"))
        {
            pids.Add(p.Id);
            p.Dispose();
        }
        return pids;
    }

    private void KillNewNotepads()
    {
        if (!_launched)
            return; // this test never launched Notepad: kill nothing

        foreach (var p in Process.GetProcessesByName("notepad"))
        {
            try
            {
                if (!_preexistingNotepads.Contains(p.Id))
                    p.Kill(entireProcessTree: true);
            }
            catch { /* best-effort */ }
            finally { p.Dispose(); }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement StructuredOf(CallToolResult result) =>
        result.StructuredContent ?? throw new InvalidOperationException("No structured content in result");

    private async Task<JsonElement> ObserveNotepadAsync()
    {
        var result = await _client.CallToolAsync("Observe", new Dictionary<string, object?>
        {
            ["scope"] = "process",
            ["process"] = "notepad",
            ["format"] = "json",
        });
        return StructuredOf(result);
    }

    /// <summary>
    /// Launches Notepad and waits for a Notepad process that was NOT in the start-of-test snapshot.
    /// Returns the observation only when it contains exclusively windows of that new process and the
    /// foreground window is one of them (ids resolve in the window they were observed in, but
    /// Observe(scope=process) looks at the first Notepad process in z-order, so the test must be sure
    /// that this is its own instance before it acts on anything). Returns null — and the caller must not
    /// touch anything — when no new process appears (single-instance reuse) or the new instance is
    /// not what Observe/Click would see first.
    /// </summary>
    private async Task<JsonElement?> LaunchNotepadAsync()
    {
        _launched = true;
        var launch = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe",
        });
        _output.WriteLine($"App launch: {launch}");

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500, Ct);
            var fresh = NotepadPids().Where(pid => !_preexistingNotepads.Contains(pid)).ToList();
            if (fresh.Count == 0)
                continue;

            JsonElement obs;
            try { obs = await ObserveNotepadAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { continue; }

            var windows = obs.GetProperty("windows").EnumerateArray().ToList();
            if (windows.Count == 0 || FindFirst(obs, "Document", "Edit") is null)
                continue;
            if (windows.Any(w => !fresh.Contains(w.GetProperty("pid").GetInt32())))
                continue; // Observe is looking at a pre-existing Notepad: keep waiting
            if (!windows.Any(w => w.GetProperty("foreground").GetBoolean()))
                continue;

            return obs;
        }

        _output.WriteLine("Skipped: no new Notepad process became observable (single-instance reuse or an existing Notepad is in front); nothing was touched.");
        return null;
    }

    private static string? FindFirst(JsonElement observation, params string[] types)
    {
        foreach (var element in observation.GetProperty("elements").EnumerateArray())
        {
            var type = element.GetProperty("type").GetString();
            if (types.Contains(type))
                return element.GetProperty("id").GetString();
        }
        return null;
    }

    private Task<CallToolResult> ExpectAsync(object[] conditions, int timeoutMs = 0) =>
        _client.CallToolAsync("Expect", new Dictionary<string, object?>
        {
            ["conditions"] = conditions,
            ["timeout_ms"] = timeoutMs,
            ["scope"] = "process",
            ["process"] = "notepad",
            ["format"] = "json",
        });

    private static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Expect_WindowOpenAndElementEnabled_Pass()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        var title = obs.GetProperty("windows")[0].GetProperty("title").GetString();
        var id = FindFirst(obs, "Document", "Edit");
        Assert.NotNull(id);

        var result = await ExpectAsync(
        [
            new Dictionary<string, object?> { ["window"] = title, ["state"] = "open" },
            new Dictionary<string, object?> { ["element"] = id, ["state"] = "enabled" },
        ]);
        _output.WriteLine(TextOf(result));

        Assert.NotEqual(true, result.IsError);
        var json = StructuredOf(result);
        Assert.Equal("pass", json.GetProperty("result").GetString());
        Assert.Equal(1, json.GetProperty("observations").GetInt32());
        Assert.Equal(id, json.GetProperty("conditions")[1].GetProperty("elements")[0].GetString());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Expect_MissingWindow_Fail_IsNotAnError()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is null) return;

        var result = await ExpectAsync(
            [new Dictionary<string, object?> { ["window"] = $"no-such-window-{Guid.NewGuid():N}" }]);
        _output.WriteLine(TextOf(result));

        Assert.NotEqual(true, result.IsError);
        var json = StructuredOf(result);
        Assert.Equal("fail", json.GetProperty("result").GetString());
        Assert.False(json.GetProperty("timed_out").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Expect_ValueAfterType()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        var id = FindFirst(obs, "Document", "Edit");
        Assert.NotNull(id);

        var text = $"expect_parity_{Guid.NewGuid():N}"[..21];
        var typed = await _client.CallToolTextAsync("Type", new Dictionary<string, object?>
        {
            ["element"] = id,
            ["text"] = text,
            ["clear"] = true,
        });
        _output.WriteLine($"Type result: {typed}");
        if (!typed.Contains("effect: value_verified"))
        {
            _output.WriteLine("Skipped: this Notepad's text area does not expose its value to UI Automation.");
            return;
        }

        var result = await ExpectAsync(
        [
            new Dictionary<string, object?> { ["element"] = id, ["value"] = text },
            new Dictionary<string, object?> { ["element"] = id, ["value_contains"] = "no-such-text-in-the-field" },
        ]);
        _output.WriteLine(TextOf(result));

        var conditions = StructuredOf(result).GetProperty("conditions");
        Assert.Equal("pass", conditions[0].GetProperty("result").GetString());
        Assert.Equal("fail", conditions[1].GetProperty("result").GetString());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Expect_Timeout_ObservesRepeatedly_AndReportsTheTimeout()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is null) return;

        var result = await ExpectAsync(
            [new Dictionary<string, object?> { ["window"] = $"no-such-window-{Guid.NewGuid():N}" }], timeoutMs: 2500);
        _output.WriteLine(TextOf(result));

        var json = StructuredOf(result);
        Assert.Equal("fail", json.GetProperty("result").GetString());
        Assert.True(json.GetProperty("timed_out").GetBoolean());
        Assert.True(json.GetProperty("observations").GetInt32() >= 2);
        Assert.InRange(json.GetProperty("elapsed_ms").GetInt64(), 1000, 12_000);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task Expect_InvalidCondition_IsError()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        var result = await _client.CallToolAsync("Expect", new Dictionary<string, object?>
        {
            ["conditions"] = new object[] { new Dictionary<string, object?> { ["window"] = "A", ["state"] = "visible" } },
        });

        Assert.True(result.IsError);
        Assert.StartsWith("[ERROR] ArgumentException: condition 1: unknown state 'visible'", TextOf(result));
    }
}
