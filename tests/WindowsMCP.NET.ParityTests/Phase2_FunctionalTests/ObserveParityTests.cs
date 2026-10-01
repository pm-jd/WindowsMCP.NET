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

    public ObserveParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public ValueTask InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
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

    /// <summary>Launches Notepad (recording pre-existing PIDs first) and waits until Observe sees a window.</summary>
    private async Task<JsonElement> LaunchNotepadAsync()
    {
        _preexistingNotepads = NotepadPids();
        var launch = await _client.CallToolTextAsync("App", new Dictionary<string, object?>
        {
            ["mode"] = "launch",
            ["name"] = "notepad.exe",
        });
        _output.WriteLine($"App launch: {launch}");

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500, Ct);
            var obs = await ObserveNotepadAsync();
            if (obs.GetProperty("windows").GetArrayLength() > 0 && FindFirst(obs, "Document", "Edit") is not null)
                return obs;
        }
        throw new InvalidOperationException("Notepad window did not appear in Observe within 15 s");
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

        var obs = await LaunchNotepadAsync();
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

        var obs = await LaunchNotepadAsync();
        var signatureBefore = obs.GetProperty("signature").GetString();
        var id = FindFirst(obs, "Document", "Edit");
        Assert.NotNull(id);

        var result = await _client.CallToolTextAsync("Type", new Dictionary<string, object?>
        {
            ["element"] = id,
            ["text"] = "observe_parity",
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

        var obs = await LaunchNotepadAsync();
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

        var obs = await LaunchNotepadAsync();
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
