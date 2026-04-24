using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class SystemToolsTests : IDisposable
{
    private readonly string _testKeyPath;

    public SystemToolsTests()
    {
        // Per-test scratch key under HKCU\Software so we never touch HKLM or other users.
        _testKeyPath = $"Software\\WindowsMcpNetTests\\{Guid.NewGuid():N}";
    }

    // --- Process.list ---

    [Fact]
    public void Process_List_FormatJson_HasItemsAndPagination()
    {
        var result = SystemTools.ProcessTool("list", limit: 3, offset: 0, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(root.GetProperty("count").GetInt32() <= 3);
        // System always has more than 3 processes — should report has_more.
        Assert.True(root.GetProperty("has_more").GetBoolean());
        Assert.Equal(0, root.GetProperty("offset").GetInt32());
    }

    [Fact]
    public void Process_List_OffsetSkips()
    {
        var first = SystemTools.ProcessTool("list", limit: 2, offset: 0, format: "json");
        var second = SystemTools.ProcessTool("list", limit: 2, offset: 2, format: "json");

        using var doc1 = JsonDocument.Parse(first);
        using var doc2 = JsonDocument.Parse(second);
        var pids1 = doc1.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("pid").GetInt32()).ToList();
        var pids2 = doc2.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("pid").GetInt32()).ToList();

        // Offset+limit should yield disjoint PIDs (assuming process list is stable enough).
        Assert.Empty(pids1.Intersect(pids2));
    }

    [Fact]
    public void Process_List_MarkdownDefault_HasFooter()
    {
        var result = SystemTools.ProcessTool("list", limit: 5);
        Assert.Contains("PID", result);
        Assert.Contains("has_more=", result);
    }

    // --- Registry.get/list/set ---

    [Fact]
    public void Registry_List_HKCUEnvironment_FormatJson()
    {
        // HKCU\Environment is a stable user-level key present on every Windows install.
        var result = SystemTools.RegistryTool("list", "HKCU\\Environment", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("HKCU\\Environment", root.GetProperty("key").GetString());
        Assert.True(root.GetProperty("values_total").GetInt32() >= 1);
        Assert.True(root.GetProperty("values").GetArrayLength() >= 1);
    }

    [Fact]
    public void Registry_Get_FormatJson_KnownKey()
    {
        // PATH is set on Environment for every user.
        var result = SystemTools.RegistryTool("get", "HKCU\\Environment",
            name: "PATH", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("HKCU\\Environment", root.GetProperty("key").GetString());
        Assert.Equal("PATH", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("exists").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("value").GetString()));
    }

    [Fact]
    public void Registry_Get_MissingValue_FormatJson_ExistsFalse()
    {
        var result = SystemTools.RegistryTool("get", "HKCU\\Environment",
            name: $"_does_not_exist_{Guid.NewGuid():N}", format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public void Registry_Set_DWord_LocaleInvariant()
    {
        // Reproduces the bug fix: int.Parse without InvariantCulture would fail on
        // locales where '.' is the thousands separator (e.g. de-DE for very large numbers).
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var setResult = SystemTools.RegistryTool("set", $"HKCU\\{_testKeyPath}",
                name: "DWordTest", value: "12345", type: "DWord");
            Assert.StartsWith("Set HKCU", setResult);

            // Verify with raw .NET API (independent of our tool):
            using var key = Registry.CurrentUser.OpenSubKey(_testKeyPath);
            Assert.NotNull(key);
            Assert.Equal(12345, (int)key!.GetValue("DWordTest")!);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void Registry_List_LimitedKey_HasMoreReportsCorrectly()
    {
        // Create a scratch key with 5 values, request limit=2 → has_more should be true.
        using (var key = Registry.CurrentUser.CreateSubKey(_testKeyPath)!)
        {
            for (int i = 0; i < 5; i++)
                key.SetValue($"v{i}", i);
        }

        var result = SystemTools.RegistryTool("list", $"HKCU\\{_testKeyPath}",
            limit: 2, offset: 0, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(5, root.GetProperty("values_total").GetInt32());
        Assert.Equal(2, root.GetProperty("values").GetArrayLength());
        Assert.True(root.GetProperty("values_has_more").GetBoolean());
    }

    public void Dispose()
    {
        // Best-effort cleanup of scratch key (if any test used it).
        try { Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false); }
        catch { }
        try { Registry.CurrentUser.DeleteSubKey("Software\\WindowsMcpNetTests", throwOnMissingSubKey: false); }
        catch { }
    }
}
