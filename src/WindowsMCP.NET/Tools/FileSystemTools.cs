using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class FileSystemTools
{
    private const int MaxReadBytes = 1_000_000; // 1 MB text limit

    [McpServerTool(Name = "FileSystem", Destructive = true, OpenWorld = true, ReadOnly = false)]
    [Description("File system operations. mode: read, write, read_base64, write_base64, copy, move, delete, list, search, info.")]
    public static string FileSystem(
        [Description("Mode: read, write, read_base64, write_base64, copy, move, delete, list, search, info")] string mode,
        [Description("Primary path (file or directory)")] string path,
        [Description("Destination path (for copy/move)")] string? destination = null,
        [Description("Text content to write (for mode=write), or Base64 data (for mode=write_base64)")] string? content = null,
        [Description("Search pattern (for mode=list/search, e.g. '*.txt')")] string? pattern = null,
        [Description("Recursive search (for mode=list/search)")] bool recursive = false,
        [Description("Encoding for read/write (utf8 default)")] string encoding = "utf8",
        [Description("Append instead of overwrite (for mode=write)")] bool append = false,
        [Description("Line offset for read (0-based, for paging)")] int offset = 0,
        [Description("Maximum lines to return for read (0 = all)")] int limit = 0,
        [Description("Overwrite destination if it exists (for copy/move)")] bool overwrite = true,
        [Description("Include hidden files and directories in list/search results")] bool show_hidden = false)
    {
        try
        {
            return mode.ToLowerInvariant() switch
            {
                "read"   => ReadFile(path, encoding, offset, limit),
                "write"  => WriteFile(path, content, encoding, append),
                "copy"   => CopyFile(path, destination, overwrite),
                "move"   => MoveFile(path, destination, overwrite),
                "delete" => DeleteFile(path),
                "list"   => ListDirectory(path, pattern, recursive, show_hidden),
                "search" => SearchFiles(path, pattern, recursive, show_hidden),
                "read_base64"  => ReadFileBase64(path),
                "write_base64" => WriteFileBase64(path, content),
                "info"   => GetInfo(path),
                _        => $"[ERROR] Unknown mode '{mode}'. Use: read, write, read_base64, write_base64, copy, move, delete, list, search, info."
            };
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ReadFile(string path, string enc, int offset, int limit)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {path}");
        if (fileInfo.Length > MaxReadBytes)
            throw new InvalidOperationException($"File too large ({fileInfo.Length:N0} bytes). Max is {MaxReadBytes:N0} bytes.");

        if (offset == 0 && limit == 0)
            return File.ReadAllText(path, GetEncoding(enc));

        var lines = File.ReadAllLines(path, GetEncoding(enc));
        var slice = (offset > 0 ? lines.Skip(offset) : lines);
        if (limit > 0)
            slice = slice.Take(limit);
        return string.Join(Environment.NewLine, slice);
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

    private static string ReadFileBase64(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {path}");
        if (fileInfo.Length > MaxReadBytes)
            throw new InvalidOperationException($"File too large ({fileInfo.Length:N0} bytes). Max is {MaxReadBytes:N0} bytes.");

        var bytes = File.ReadAllBytes(path);
        return Convert.ToBase64String(bytes);
    }

    private static string WriteFileBase64(string path, string? content)
    {
        if (content is null)
            throw new ArgumentException("'content' is required for mode=write_base64 (Base64-encoded data).");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var bytes = Convert.FromBase64String(content);
        File.WriteAllBytes(path, bytes);
        return $"Written {bytes.Length} byte(s) to {path}";
    }

    private static string CopyFile(string source, string? dest, bool overwrite)
    {
        if (dest is null)
            throw new ArgumentException("'destination' is required for mode=copy.");

        if (Directory.Exists(source))
            return CopyDirectory(source, dest, overwrite);

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(source, dest, overwrite: overwrite);
        return $"Copied {source} → {dest}";
    }

    private static string CopyDirectory(string source, string dest, bool overwrite)
    {
        var srcDir = new DirectoryInfo(source);
        Directory.CreateDirectory(dest);
        int count = 0;

        foreach (var file in srcDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file.FullName);
            var destFile = Path.Combine(dest, relativePath);
            var destFileDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destFileDir))
                Directory.CreateDirectory(destFileDir);
            file.CopyTo(destFile, overwrite);
            count++;
        }

        return $"Copied directory {source} → {dest} ({count} file(s))";
    }

    private static string MoveFile(string source, string? dest, bool overwrite)
    {
        if (dest is null)
            throw new ArgumentException("'destination' is required for mode=move.");

        if (Directory.Exists(source))
        {
            Directory.Move(source, dest);
            return $"Moved directory {source} → {dest}";
        }

        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Move(source, dest, overwrite: overwrite);
        return $"Moved {source} → {dest}";
    }

    private static void ValidateNotProtected(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetPathRoot(Environment.SystemDirectory),
        };
        foreach (var root in protectedRoots)
        {
            if (!string.IsNullOrEmpty(root) && full.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to delete protected system path: {path}");
        }
    }

    private static string DeleteFile(string path)
    {
        ValidateNotProtected(path);
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

    private static string ListDirectory(string path, string? pattern, bool recursive, bool showHidden)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pat = pattern ?? "*";

        var entries = Directory.GetFileSystemEntries(path, pat, searchOption)
            .Where(e => showHidden || !IsHidden(e))
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

    private static string SearchFiles(string path, string? pattern, bool recursive, bool showHidden)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pat = pattern ?? "*";

        var files = Directory.GetFiles(path, pat, searchOption)
            .Where(f => showHidden || !IsHidden(f))
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
            var fileCount = di.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Count();
            var dirCount  = di.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).Count();
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

    private static bool IsHidden(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
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
