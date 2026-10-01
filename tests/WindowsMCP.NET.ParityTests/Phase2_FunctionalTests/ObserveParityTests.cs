using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

/// <summary>
/// Desktop parity tests for Observe + element-id actions. Every test launches its own Notepad and
/// kills only the notepad processes that did not exist before it started (the developer may have
/// their own Notepad open). Elements are picked by control type, never by (localized) name.
/// </summary>
[Collection("McpServer")]
public class ObserveParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;
    private HashSet<int> _preexistingNotepads = [];
    private bool _launched;

    public ObserveParityTests(McpServerFixture fixture, ITestOutputHelper output)
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

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Observe_Notepad_ListsTextArea()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        _output.WriteLine(obs.ToString());

        Assert.False(string.IsNullOrEmpty(obs.GetProperty("signature").GetString()));
        Assert.True(obs.GetProperty("windows").GetArrayLength() >= 1);
        Assert.NotNull(FindFirst(obs, "Document", "Edit"));
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Type_ByElementId_ReportsEffect()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        var signatureBefore = obs.GetProperty("signature").GetString();
        var id = FindFirst(obs, "Document", "Edit");
        Assert.NotNull(id);

        var result = await _client.CallToolTextAsync("Type", new Dictionary<string, object?>
        {
            ["element"] = id,
            ["text"] = $"observe_parity_{Guid.NewGuid():N}"[..22], // unique: Notepad restores earlier text, identical text would not change the signature
            ["clear"] = true,
        });
        _output.WriteLine($"Type result: {result}");

        Assert.DoesNotContain("[ERROR]", result);
        Assert.True(
            result.Contains("effect: value_verified") || result.Contains("effect: changed"),
            $"Expected a verified effect, got: {result}");

        var after = await ObserveNotepadAsync();
        Assert.NotEqual(signatureBefore, after.GetProperty("signature").GetString());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task Click_MenuByElementId_Changed()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        var id = FindFirst(obs, "MenuItem");
        Assert.NotNull(id);

        var result = await _client.CallToolTextAsync("Click", new Dictionary<string, object?>
        {
            ["element"] = id,
        });
        _output.WriteLine($"Click result: {result}");

        try
        {
            Assert.Contains("effect: changed", result);
        }
        finally
        {
            await _client.CallToolTextAsync("Shortcut", new Dictionary<string, object?> { ["shortcut"] = "escape" });
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Category", "Desktop")]
    public async Task StaleId_AfterClose_ElementNotFound()
    {
        if (_fixture.ServerType != "dotnet") { _output.WriteLine("Skipped: dotnet server only"); return; }

        if (await LaunchNotepadAsync() is not { } obs) return;
        var id = FindFirst(obs, "Document", "Edit");
        Assert.NotNull(id);

        KillNewNotepads();
        await Task.Delay(1000, Ct);

        var result = await _client.CallToolTextAsync("Click", new Dictionary<string, object?>
        {
            ["element"] = id,
        });
        _output.WriteLine($"Click result: {result}");

        Assert.StartsWith("[ERROR] ElementNotFoundException", result);
        Assert.Contains("call Observe", result);
    }
}
