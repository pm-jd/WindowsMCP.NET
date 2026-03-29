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

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
