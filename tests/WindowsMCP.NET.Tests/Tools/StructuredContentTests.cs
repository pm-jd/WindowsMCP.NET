using System.Text.Json;
using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

/// <summary>
/// format=json tools return the envelope twice: as text (for text-only clients) and as
/// structuredContent (MCP 2025-06-18+), so agents can consume it without re-parsing text.
/// Errors carry IsError directly instead of relying on the [ERROR] prefix filter alone.
/// </summary>
public class StructuredContentTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"wmcp_sc_{Guid.NewGuid():N}");

    public StructuredContentTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "bb");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void FileSystem_ListJson_ReturnsStructuredContentAndText()
    {
        var result = FileSystemTools.FileSystem(FileSystemMode.List, _tempDir, format: OutputFormat.Json);

        Assert.NotNull(result.StructuredContent);
        Assert.Equal(2, result.StructuredContent!.Value.GetProperty("count").GetInt32());
        Assert.Equal(2, JsonDocument.Parse(result.Text()).RootElement.GetProperty("count").GetInt32());
        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void FileSystem_ListMarkdown_HasNoStructuredContent()
    {
        var result = FileSystemTools.FileSystem(FileSystemMode.List, _tempDir);

        Assert.Null(result.StructuredContent);
        Assert.Contains("[FILE]", result.Text());
    }

    [Fact]
    public void FileSystem_Error_SetsIsErrorAndKeepsErrorText()
    {
        var result = FileSystemTools.FileSystem(FileSystemMode.Read, Path.Combine(_tempDir, "missing.txt"));

        Assert.True(result.IsError);
        Assert.StartsWith("[ERROR] FileNotFoundException", result.Text());
    }

    [Fact]
    public void Process_ListJson_StructuredContentMatchesItems()
    {
        var result = SystemTools.ProcessTool(ProcessMode.List, limit: 3, format: OutputFormat.Json);

        var sc = result.StructuredContent!.Value;
        Assert.Equal(sc.GetProperty("items").GetArrayLength(), sc.GetProperty("count").GetInt32());
        Assert.Equal("memory", sc.GetProperty("sort_by").GetString());
    }

    [Fact]
    public void Registry_GetJson_ReturnsStructuredContent()
    {
        var result = SystemTools.RegistryTool(RegistryMode.Get, @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            name: "ProductName", format: OutputFormat.Json);

        Assert.True(result.StructuredContent!.Value.GetProperty("exists").GetBoolean());
        Assert.Equal("ProductName", result.StructuredContent.Value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task App_StatusJson_ReturnsStructuredContent()
    {
        var ds = new WindowsMcpNet.Services.DesktopService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsMcpNet.Services.DesktopService>.Instance);

        var result = await AppTools.App(ds, mode: AppMode.Status, name: $"no_such_app_{Guid.NewGuid():N}",
            format: OutputFormat.Json, ct: TestContext.Current.CancellationToken);

        Assert.False(result.StructuredContent!.Value.GetProperty("running").GetBoolean());
    }
}
