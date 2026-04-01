# FileSystem Tool: Error Handling & Feature Improvements

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix swallowed error messages in FileSystem tool, add directory copy/move, and add base64 binary transfer mode for cross-machine file operations.

**Architecture:** Wrap the main tool dispatch in try-catch to surface meaningful .NET exception messages to MCP clients. Extend copy/move to handle directories recursively. Add `read_base64`/`write_base64` modes for binary file transfer.

**Tech Stack:** C# / .NET 9, ModelContextProtocol SDK v1.2.0, xUnit

---

### Task 1: Error Handling — Make Exceptions Visible to MCP Clients

**Problem:** The MCP SDK catches all exceptions from tool methods and returns a generic "An error occurred invoking 'FileSystem'" message. Clients never see what actually went wrong.

**Solution:** Catch exceptions inside the tool method itself and return error strings prefixed with `[ERROR]` so they reach the client as normal text content, not as SDK-swallowed exceptions.

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/FileSystemTools.cs:27-40`
- Test: `tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs`

**Step 1: Write failing tests for error scenarios**

Add these tests to `FileSystemToolsTests.cs`:

```csharp
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
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: 4 new tests FAIL (currently these throw exceptions instead of returning `[ERROR]` strings)

**Step 3: Wrap the main switch in try-catch**

In `FileSystemTools.cs`, replace the `FileSystem` method body (lines 28-39) with:

```csharp
try
{
    return mode.ToLowerInvariant() switch
    {
        "read"         => ReadFile(path, encoding, offset, limit),
        "write"        => WriteFile(path, content, encoding, append),
        "copy"         => CopyFile(path, destination, overwrite),
        "move"         => MoveFile(path, destination, overwrite),
        "delete"       => DeleteFile(path),
        "list"         => ListDirectory(path, pattern, recursive, show_hidden),
        "search"       => SearchFiles(path, pattern, recursive, show_hidden),
        "info"         => GetInfo(path),
        _              => $"[ERROR] Unknown mode '{mode}'. Use: read, write, copy, move, delete, list, search, info."
    };
}
catch (Exception ex)
{
    return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
}
```

Note: The `_ =>` case now returns a string instead of throwing, so it's also caught by the pattern.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: ALL tests PASS (existing + new)

**Step 5: Commit**

```bash
git add src/WindowsMCP.NET/Tools/FileSystemTools.cs tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs
git commit -m "fix: return meaningful error messages from FileSystem tool instead of generic SDK errors"
```

---

### Task 2: Directory Copy & Move Support

**Problem:** `File.Copy()` / `File.Move()` only work on individual files. Attempting to copy a directory silently fails or errors generically.

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/FileSystemTools.cs:77-101` (CopyFile, MoveFile methods)
- Test: `tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs`

**Step 1: Write failing tests**

```csharp
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
    // Source still exists after copy
    Assert.True(Directory.Exists(srcDir));
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
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: 2 new tests FAIL

**Step 3: Extend CopyFile and MoveFile to handle directories**

Replace `CopyFile` (line 77-88):

```csharp
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
```

Replace `MoveFile` (line 90-101):

```csharp
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
```

**Step 4: Run tests**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: ALL tests PASS

**Step 5: Commit**

```bash
git add src/WindowsMCP.NET/Tools/FileSystemTools.cs tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs
git commit -m "feat: support directory copy and move in FileSystem tool"
```

---

### Task 3: Base64 Binary Transfer Mode

**Problem:** `read` and `write` are text-only (encoding-based). Binary files (images, executables, archives) can't be transferred through the MCP protocol. This blocks cross-machine file transfer for non-text files.

**Solution:** Add `read_base64` and `write_base64` modes. `read_base64` returns file content as a Base64 string. `write_base64` decodes Base64 content and writes raw bytes.

**Files:**
- Modify: `src/WindowsMCP.NET/Tools/FileSystemTools.cs`
- Test: `tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs`

**Step 1: Write failing tests**

```csharp
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
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: 3 new tests FAIL

**Step 3: Implement read_base64 and write_base64**

Add two new cases in the switch statement:

```csharp
"read_base64"  => ReadFileBase64(path),
"write_base64" => WriteFileBase64(path, content),
```

Add the private methods:

```csharp
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
```

Update the tool description (line 13):

```csharp
[Description("File system operations. mode: read, write, read_base64, write_base64, copy, move, delete, list, search, info.")]
```

Also update the mode parameter description (line 15):

```csharp
[Description("Mode: read, write, read_base64, write_base64, copy, move, delete, list, search, info")] string mode,
```

**Step 4: Run tests**

Run: `dotnet test tests/WindowsMCP.NET.Tests --filter "FileSystemToolsTests" -v n`
Expected: ALL tests PASS

**Step 5: Commit**

```bash
git add src/WindowsMCP.NET/Tools/FileSystemTools.cs tests/WindowsMCP.NET.Tests/Tools/FileSystemToolsTests.cs
git commit -m "feat: add read_base64/write_base64 modes for binary file transfer"
```

---

### Task 4: Final Validation & Cleanup

**Step 1: Run full test suite**

Run: `dotnet test -v n`
Expected: ALL tests pass

**Step 2: Build release**

Run: `dotnet build src/WindowsMCP.NET -c Release`
Expected: Build succeeds

**Step 3: Cleanup test files on swentw3**

Delete the test files created during analysis:
- `C:\tmp\mcp_copy_test.txt`
- `C:\tmp\mcp_copy_test_dest.txt`
- `C:\tmp\subfolder_test\`

**Step 4: Commit all remaining changes**

If any final tweaks were needed, commit them.

---

## Summary of Changes

| Change | Impact |
|--------|--------|
| Try-catch in `FileSystem()` | All tool errors now return `[ERROR] ExceptionType: message` instead of being swallowed |
| Directory copy | `mode=copy` with a directory path copies all files recursively |
| Directory move | `mode=move` with a directory path moves the entire tree |
| `read_base64` | Returns file bytes as Base64 string (for binary transfer) |
| `write_base64` | Writes Base64-decoded bytes to file (for binary transfer) |
| Error string format | All errors start with `[ERROR]` — easy to detect programmatically |
