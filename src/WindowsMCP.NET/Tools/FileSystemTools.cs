using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class FileSystemTools
{
    private const int MaxReadBytes = 1_000_000; // 1 MB text limit

    [McpServerTool(Name = "FileSystem", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("File system operations. mode: read, write, copy, move, delete, list, search, info.")]
    public static string FileSystem(
        [Description("Mode: read, write, copy, move, delete, list, search, info")] string mode,
        [Description("Primary path (file or directory)")] string path,
        [Description("Destination path (for copy/move)")] string? destination = null,
        [Description("Text content to write (for mode=write)")] string? content = null,
        [Description("Search pattern (for mode=list/search, e.g. '*.txt')")] string? pattern = null,
        [Description("Recursive search (for mode=list/search)")] bool recursive = false,
        [Description("Encoding for read/write (utf8 default)")] string encoding = "utf8",
        [Description("Append instead of overwrite (for mode=write)")] bool append = false)
    {
        return mode.ToLowerInvariant() switch
        {
            "read"   => ReadFile(path, encoding),
            "write"  => WriteFile(path, content, encoding, append),
            "copy"   => CopyFile(path, destination),
            "move"   => MoveFile(path, destination),
            "delete" => DeleteFile(path),
            "list"   => ListDirectory(path, pattern, recursive),
            "search" => SearchFiles(path, pattern, recursive),
            "info"   => GetInfo(path),
            _ => throw new ArgumentException($"Unknown mode '{mode}'. Use: read, write, copy, move, delete, list, search, info.")
        };
    }

    private static string ReadFile(string path, string enc)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {path}");
        if (fileInfo.Length > MaxReadBytes)
            throw new InvalidOperationException($"File too large ({fileInfo.Length:N0} bytes). Max is {MaxReadBytes:N0} bytes.");

        return File.ReadAllText(path, GetEncoding(enc));
    }

    private static string WriteFile(string path, string? content, string enc, bool append)
    {
        if (content is null)
            throw new ArgumentException("'content' is required for mode=write.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (append)
            File.AppendAllText(path, content, GetEncoding(enc));
        else
            File.WriteAllText(path, content, GetEncoding(enc));

        return $"{(append ? "Appended" : "Written")} {content.Length} character(s) to {path}";
    }

    private static string CopyFile(string source, string? dest)
    {
        if (dest is null)
            throw new ArgumentException("'destination' is required for mode=copy.");

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(source, dest, overwrite: true);
        return $"Copied {source} → {dest}";
    }

    private static string MoveFile(string source, string? dest)
    {
        if (dest is null)
            throw new ArgumentException("'destination' is required for mode=move.");

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Move(source, dest, overwrite: true);
        return $"Moved {source} → {dest}";
    }

    private static string DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return $"Deleted file: {path}";
        }
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return $"Deleted directory: {path}";
        }
        return $"Path not found: {path}";
    }

    private static string ListDirectory(string path, string? pattern, bool recursive)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pat = pattern ?? "*";

        var entries = Directory.GetFileSystemEntries(path, pat, searchOption)
            .OrderBy(e => e)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Contents of: {path}");
        sb.AppendLine($"Pattern: {pat}{(recursive ? " (recursive)" : "")}");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
                sb.AppendLine($"[DIR]  {entry}");
            else
            {
                var fi = new FileInfo(entry);
                sb.AppendLine($"[FILE] {entry}  ({fi.Length:N0} bytes, {fi.LastWriteTime:yyyy-MM-dd HH:mm})");
            }
        }

        sb.AppendLine($"\nTotal: {entries.Count} item(s)");
        return sb.ToString().TrimEnd();
    }

    private static string SearchFiles(string path, string? pattern, bool recursive)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pat = pattern ?? "*";

        var files = Directory.GetFiles(path, pat, searchOption)
            .OrderBy(f => f)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Search in: {path}");
        sb.AppendLine($"Pattern: {pat}{(recursive ? " (recursive)" : "")}");
        sb.AppendLine();

        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            sb.AppendLine($"{file}  ({fi.Length:N0} bytes)");
        }

        sb.AppendLine($"\nFound: {files.Count} file(s)");
        return sb.ToString().TrimEnd();
    }

    private static string GetInfo(string path)
    {
        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            return $"Type:         File\n" +
                   $"Path:         {fi.FullName}\n" +
                   $"Size:         {fi.Length:N0} bytes\n" +
                   $"Created:      {fi.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Modified:     {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Accessed:     {fi.LastAccessTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Attributes:   {fi.Attributes}";
        }
        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            var fileCount = di.GetFiles("*", SearchOption.AllDirectories).Length;
            var dirCount  = di.GetDirectories("*", SearchOption.AllDirectories).Length;
            return $"Type:         Directory\n" +
                   $"Path:         {di.FullName}\n" +
                   $"Files:        {fileCount:N0}\n" +
                   $"Subdirs:      {dirCount:N0}\n" +
                   $"Created:      {di.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Modified:     {di.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Attributes:   {di.Attributes}";
        }
        return $"Path not found: {path}";
    }

    private static Encoding GetEncoding(string enc) =>
        enc.ToLowerInvariant() switch
        {
            "utf8" or "utf-8"   => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf16" or "utf-16" => Encoding.Unicode,
            "ascii"             => Encoding.ASCII,
            "latin1"            => Encoding.Latin1,
            _ => Encoding.UTF8,
        };
}
