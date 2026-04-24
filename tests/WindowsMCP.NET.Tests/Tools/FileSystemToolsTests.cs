using System.Text.Json;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class FileSystemToolsTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wmcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void WriteAndRead_RoundTrips()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        FileSystemTools.FileSystem("write", filePath, content: "Hello World");
        var result = FileSystemTools.FileSystem("read", filePath);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Write_Append()
    {
        var filePath = Path.Combine(_tempDir, "append.txt");
        FileSystemTools.FileSystem("write", filePath, content: "Line1");
        FileSystemTools.FileSystem("write", filePath, content: "\nLine2", append: true);
        var result = FileSystemTools.FileSystem("read", filePath);
        Assert.Equal("Line1\nLine2", result);
    }

    [Fact]
    public void List_ShowsFilesAndDirs()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        var result = FileSystemTools.FileSystem("list", _tempDir);
        Assert.Contains("subdir", result);
        Assert.Contains("a.txt", result);
    }

    [Fact]
    public void Copy_CopiesFile()
    {
        var src = Path.Combine(_tempDir, "src.txt");
        var dst = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(src, "copy me");
        FileSystemTools.FileSystem("copy", src, destination: dst);
        Assert.True(File.Exists(dst));
        Assert.Equal("copy me", File.ReadAllText(dst));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var filePath = Path.Combine(_tempDir, "delete_me.txt");
        File.WriteAllText(filePath, "");
        FileSystemTools.FileSystem("delete", filePath);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Search_FindsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "match.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "other.txt"), "");
        var result = FileSystemTools.FileSystem("search", _tempDir, pattern: "*.cs");
        Assert.Contains("match.cs", result);
        Assert.DoesNotContain("other.txt", result);
    }

    [Fact]
    public void Info_ReturnsFileDetails()
    {
        var filePath = Path.Combine(_tempDir, "info.txt");
        File.WriteAllText(filePath, "12345");
        var result = FileSystemTools.FileSystem("info", filePath);
        Assert.Contains("5", result);
        Assert.Contains("info.txt", result);
    }

    [Fact]
    public void Copy_Directory_CopiesRecursively()
    {
        var srcDir = Path.Combine(_tempDir, "srcdir");
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
        File.WriteAllText(Path.Combine(srcDir, "a.txt"), "aaa");
        File.WriteAllText(Path.Combine(srcDir, "sub", "b.txt"), "bbb");

        var dstDir = Path.Combine(_tempDir, "dstdir");
        var result = FileSystemTools.FileSystem("copy", srcDir, destination: dstDir);

        Assert.Contains("Copied", result);
        Assert.True(File.Exists(Path.Combine(dstDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dstDir, "sub", "b.txt")));
        Assert.Equal("bbb", File.ReadAllText(Path.Combine(dstDir, "sub", "b.txt")));
        Assert.True(Directory.Exists(srcDir)); // source still exists after copy
    }

    [Fact]
    public void Move_Directory_MovesRecursively()
    {
        var srcDir = Path.Combine(_tempDir, "movesrc");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "f.txt"), "move me");

        var dstDir = Path.Combine(_tempDir, "movedst");
        var result = FileSystemTools.FileSystem("move", srcDir, destination: dstDir);

        Assert.Contains("Moved", result);
        Assert.True(File.Exists(Path.Combine(dstDir, "f.txt")));
        Assert.False(Directory.Exists(srcDir));
    }

    [Fact]
    public void Read_NonExistentFile_ReturnsError()
    {
        var result = FileSystemTools.FileSystem("read", Path.Combine(_tempDir, "nope.txt"));
        Assert.StartsWith("[ERROR]", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copy_NonExistentSource_ReturnsError()
    {
        var result = FileSystemTools.FileSystem("copy",
            Path.Combine(_tempDir, "nope.txt"),
            destination: Path.Combine(_tempDir, "dst.txt"));
        Assert.StartsWith("[ERROR]", result);
    }

    [Fact]
    public void Copy_OverwriteFalse_ExistingDest_ReturnsError()
    {
        var src = Path.Combine(_tempDir, "src.txt");
        var dst = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(src, "a");
        File.WriteAllText(dst, "b");
        var result = FileSystemTools.FileSystem("copy", src, destination: dst, overwrite: false);
        Assert.StartsWith("[ERROR]", result);
        Assert.Contains("exist", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownMode_ReturnsError()
    {
        var result = FileSystemTools.FileSystem("bogus", _tempDir);
        Assert.StartsWith("[ERROR]", result);
        Assert.Contains("Unknown mode", result);
    }

    [Fact]
    public void ReadBase64_ReturnsBinaryAsBase64()
    {
        var filePath = Path.Combine(_tempDir, "binary.bin");
        byte[] data = { 0x00, 0xFF, 0x42, 0x89, 0xAB };
        File.WriteAllBytes(filePath, data);

        var result = FileSystemTools.FileSystem("read_base64", filePath);
        var decoded = Convert.FromBase64String(result);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void WriteBase64_WritesDecodedBytes()
    {
        var filePath = Path.Combine(_tempDir, "output.bin");
        byte[] data = { 0xDE, 0xAD, 0xBE, 0xEF };
        var b64 = Convert.ToBase64String(data);

        FileSystemTools.FileSystem("write_base64", filePath, content: b64);

        Assert.Equal(data, File.ReadAllBytes(filePath));
    }

    [Fact]
    public void ReadBase64_LargeFile_ReturnsError()
    {
        var filePath = Path.Combine(_tempDir, "large.bin");
        File.WriteAllBytes(filePath, new byte[2_000_000]);

        var result = FileSystemTools.FileSystem("read_base64", filePath);
        Assert.StartsWith("[ERROR]", result);
        Assert.Contains("too large", result, StringComparison.OrdinalIgnoreCase);
    }

    // --- format=json + pagination ---

    [Fact]
    public void List_FormatJson_ReturnsParseableEnvelope()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));

        var result = FileSystemTools.FileSystem("list", _tempDir, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(_tempDir, root.GetProperty("path").GetString());
        Assert.True(root.GetProperty("items").GetArrayLength() >= 2);
        Assert.False(root.GetProperty("has_more").GetBoolean());
        Assert.Equal(0, root.GetProperty("offset").GetInt32());
    }

    [Fact]
    public void List_LimitAndOffset_ReportsHasMoreAndNextOffset()
    {
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file{i:D2}.txt"), "");

        var result = FileSystemTools.FileSystem("list", _tempDir, limit: 2, offset: 0, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.True(root.GetProperty("has_more").GetBoolean());
        Assert.Equal(2, root.GetProperty("next_offset").GetInt32());
    }

    [Fact]
    public void List_OffsetSkipsEntries()
    {
        for (int i = 0; i < 3; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file{i:D2}.txt"), "");

        var result = FileSystemTools.FileSystem("list", _tempDir, limit: 10, offset: 2, format: "json");

        using var doc = JsonDocument.Parse(result);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Contains("file02.txt", items[0].GetProperty("path").GetString()!);
    }

    [Fact]
    public void List_MarkdownFooter_ShowsPaginationHint()
    {
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file{i:D2}.txt"), "");

        var result = FileSystemTools.FileSystem("list", _tempDir, limit: 2);
        Assert.Contains("has_more=true", result);
        Assert.Contains("offset=2", result);
    }

    [Fact]
    public void Search_FormatJson_OnlyFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "match.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "skip.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

        var result = FileSystemTools.FileSystem("search", _tempDir, pattern: "*.cs", format: "json");

        using var doc = JsonDocument.Parse(result);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Contains("match.cs", items[0].GetProperty("path").GetString()!);
    }

    [Fact]
    public void Info_File_FormatJson_HasSize()
    {
        var filePath = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(filePath, "hello");

        var result = FileSystemTools.FileSystem("info", filePath, format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("file", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("size").GetInt64());
    }

    [Fact]
    public void Info_Directory_FormatJson_HasFileCount()
    {
        File.WriteAllText(Path.Combine(_tempDir, "x.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "y.txt"), "");

        var result = FileSystemTools.FileSystem("info", _tempDir, format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("directory", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetInt32());
    }

    [Fact]
    public void Info_Missing_FormatJson_ReturnsTypeMissing()
    {
        var result = FileSystemTools.FileSystem("info",
            Path.Combine(_tempDir, "ghost.txt"), format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("missing", doc.RootElement.GetProperty("type").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
