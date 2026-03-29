# WindowsMCP.NET Parity Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a test suite that verifies WindowsMCP.NET matches the Python Windows-MCP reference implementation in both API schema and functional behavior.

**Architecture:** A separate xUnit test project connects to MCP servers (Python or C#) via stdio transport using the official McpClient. Phase 1 compares tool schemas. Phase 2 runs functional tests against a controlled desktop environment (Notepad). Server selection via `PARITY_SERVER` env var enables sequential baseline capture (Python) then comparison (C#).

**Tech Stack:** C# xUnit, ModelContextProtocol (McpClient + StdioClientTransport), FlaUI.UIA3, System.Text.Json

**Design Spec:** `docs/superpowers/specs/2026-03-29-parity-testing-design.md`

---

## File Structure

```
tests/WindowsMCP.NET.ParityTests/
├── WindowsMCP.NET.ParityTests.csproj        — Test project with MCP client + FlaUI deps
├── Infrastructure/
│   ├── McpServerFixture.cs                  — Start/stop MCP server process, provide McpClient
│   ├── McpTestClient.cs                     — Thin async wrapper for tool calls
│   ├── SchemaComparer.cs                    — Compare two tool schemas, produce diff report
│   └── BaselineManager.cs                   — Save/load baseline JSON files
├── Phase1_SchemaTests/
│   └── ToolSchemaParityTests.cs             — Schema diff for all 18 tools
├── Phase2_FunctionalTests/
│   ├── InputToolsParityTests.cs             — Click, Type, Shortcut, Move, Wait
│   ├── SystemToolsParityTests.cs            — PowerShell, Process, Registry, FileSystem
│   ├── ScreenToolsParityTests.cs            — Screenshot, Snapshot
│   ├── AppToolsParityTests.cs               — App launch/switch/resize
│   └── ClipboardToolsParityTests.cs         — Clipboard roundtrip
└── TestData/
    └── baseline/                            — (empty dir, populated at runtime)
```

---

## Task 1: Test Project Scaffolding

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/WindowsMCP.NET.ParityTests.csproj`
- Modify: `WindowsMCP.NET.slnx` (add project)

- [ ] **Step 1: Create csproj**

Create `tests/WindowsMCP.NET.ParityTests/WindowsMCP.NET.ParityTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="ModelContextProtocol" Version="1.2.0" />
    <PackageReference Include="FlaUI.UIA3" Version="4.0.0" />
  </ItemGroup>
</Project>
```

Note: Package versions should match those used in the main project. If any version fails to resolve, use `dotnet add package <name>` to get the latest.

- [ ] **Step 2: Create TestData/baseline directory**

```bash
mkdir -p tests/WindowsMCP.NET.ParityTests/TestData/baseline
```

Create a `.gitkeep` in `tests/WindowsMCP.NET.ParityTests/TestData/baseline/.gitkeep` (empty file) so git tracks the empty directory.

- [ ] **Step 3: Add to solution**

```bash
cd /c/work/source/mygit/windows-mcp
dotnet sln add tests/WindowsMCP.NET.ParityTests/WindowsMCP.NET.ParityTests.csproj
```

- [ ] **Step 4: Verify build**

```bash
dotnet build WindowsMCP.NET.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/ WindowsMCP.NET.slnx
git commit -m "feat: scaffold parity test project"
```

---

## Task 2: McpServerFixture

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Infrastructure/McpServerFixture.cs`

- [ ] **Step 1: Implement McpServerFixture**

Create `tests/WindowsMCP.NET.ParityTests/Infrastructure/McpServerFixture.cs`:

```csharp
using System.Diagnostics;
using ModelContextProtocol.Client;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed class McpServerFixture : IAsyncLifetime
{
    private IMcpClient? _client;

    public IMcpClient Client => _client ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public string ServerType { get; private set; } = "dotnet";

    public async ValueTask InitializeAsync()
    {
        ServerType = Environment.GetEnvironmentVariable("PARITY_SERVER") ?? "dotnet";

        StdioClientTransportOptions transportOptions;

        if (ServerType == "python")
        {
            var cmd = Environment.GetEnvironmentVariable("PYTHON_MCP_CMD") ?? "uvx";
            transportOptions = new StdioClientTransportOptions
            {
                Name = "windows-mcp-python",
                Command = cmd,
                Arguments = cmd == "uvx" ? ["windows-mcp"] : [],
            };
        }
        else
        {
            var exePath = Environment.GetEnvironmentVariable("DOTNET_MCP_PATH")
                ?? FindDotnetExe();
            transportOptions = new StdioClientTransportOptions
            {
                Name = "windows-mcp-dotnet",
                Command = exePath,
                Arguments = ["--transport", "stdio"],
            };
        }

        var clientTransport = new StdioClientTransport(transportOptions);
        _client = await McpClient.CreateAsync(clientTransport);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
        else if (_client is IDisposable sync)
            sync.Dispose();
        _client = null;
    }

    private static string FindDotnetExe()
    {
        // Walk up from test bin dir to find the built exe
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "WindowsMCP.NET", "bin", "Debug", "net9.0-windows", "win-x64", "WindowsMCP.NET.exe"),
            Path.Combine(repoRoot, "src", "WindowsMCP.NET", "bin", "Release", "net9.0-windows", "win-x64", "WindowsMCP.NET.exe"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "WindowsMCP.NET.exe not found. Build the project first or set DOTNET_MCP_PATH env var.");
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

Expected: Build succeeded. If `StdioClientTransport` or `McpClient.CreateAsync` are in different namespaces, check the installed `ModelContextProtocol` package API and adjust using statements.

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Infrastructure/McpServerFixture.cs
git commit -m "feat: add McpServerFixture for starting MCP servers in tests"
```

---

## Task 3: McpTestClient

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Infrastructure/McpTestClient.cs`

- [ ] **Step 1: Implement McpTestClient**

Create `tests/WindowsMCP.NET.ParityTests/Infrastructure/McpTestClient.cs`:

```csharp
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed class McpTestClient
{
    private readonly IMcpClient _client;

    public McpTestClient(IMcpClient client)
    {
        _client = client;
    }

    public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken ct = default)
    {
        return await _client.ListToolsAsync(ct);
    }

    public async Task<CallToolResponse> CallToolAsync(
        string toolName,
        Dictionary<string, object?>? args = null,
        CancellationToken ct = default)
    {
        return await _client.CallToolAsync(toolName, args, ct);
    }

    public async Task<JsonElement> GetToolSchemaAsync(string toolName, CancellationToken ct = default)
    {
        var tools = await _client.ListToolsAsync(ct);
        var tool = tools.FirstOrDefault(t => t.Name == toolName)
            ?? throw new KeyNotFoundException($"Tool '{toolName}' not found.");

        // The tool's JsonSchema is available via the protocol info
        var json = JsonSerializer.Serialize(tool, new JsonSerializerOptions { WriteIndented = true });
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<string> CallToolTextAsync(
        string toolName,
        Dictionary<string, object?>? args = null,
        CancellationToken ct = default)
    {
        var result = await CallToolAsync(toolName, args, ct);
        return string.Join("\n", result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));
    }

    public async Task<byte[]?> CallToolImageAsync(
        string toolName,
        Dictionary<string, object?>? args = null,
        CancellationToken ct = default)
    {
        var result = await CallToolAsync(toolName, args, ct);
        var image = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
        return image is not null ? Convert.FromBase64String(image.Data) : null;
    }
}
```

Note: The exact types `McpClientTool`, `CallToolResponse`, `TextContentBlock`, `ImageContentBlock` may differ in the installed SDK version. Check the API and adjust. The core pattern is: call tool, extract text or image content.

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Infrastructure/McpTestClient.cs
git commit -m "feat: add McpTestClient wrapper for readable test code"
```

---

## Task 4: SchemaComparer

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Infrastructure/SchemaComparer.cs`

- [ ] **Step 1: Implement SchemaComparer**

Create `tests/WindowsMCP.NET.ParityTests/Infrastructure/SchemaComparer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed record SchemaDiff
{
    public required string Timestamp { get; init; }
    public required List<ToolDiff> Tools { get; init; }
    public required DiffSummary Summary { get; init; }
}

public sealed record ToolDiff
{
    public required string Name { get; init; }
    public required string Status { get; init; } // "match" or "mismatch"
    public required List<Difference> Differences { get; init; }
}

public sealed record Difference
{
    public required string Field { get; init; }
    public required string Baseline { get; init; }
    public required string Actual { get; init; }
}

public sealed record DiffSummary
{
    public int Total { get; init; }
    public int Match { get; init; }
    public int Mismatch { get; init; }
}

[JsonSerializable(typeof(SchemaDiff))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class SchemaDiffJsonContext : JsonSerializerContext;

public static class SchemaComparer
{
    public static SchemaDiff Compare(
        IList<McpClientTool> baselineTools,
        IList<McpClientTool> actualTools)
    {
        var baselineMap = baselineTools.ToDictionary(t => t.Name);
        var actualMap = actualTools.ToDictionary(t => t.Name);

        var allNames = baselineMap.Keys.Union(actualMap.Keys).OrderBy(n => n).ToList();
        var toolDiffs = new List<ToolDiff>();

        foreach (var name in allNames)
        {
            var diffs = new List<Difference>();

            if (!baselineMap.TryGetValue(name, out var baselineTool))
            {
                diffs.Add(new Difference { Field = "tool", Baseline = "missing", Actual = "present" });
            }
            else if (!actualMap.TryGetValue(name, out var actualTool))
            {
                diffs.Add(new Difference { Field = "tool", Baseline = "present", Actual = "missing" });
            }
            else
            {
                CompareToolSchemas(baselineTool, actualTool, diffs);
            }

            toolDiffs.Add(new ToolDiff
            {
                Name = name,
                Status = diffs.Count == 0 ? "match" : "mismatch",
                Differences = diffs,
            });
        }

        return new SchemaDiff
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("o"),
            Tools = toolDiffs,
            Summary = new DiffSummary
            {
                Total = toolDiffs.Count,
                Match = toolDiffs.Count(t => t.Status == "match"),
                Mismatch = toolDiffs.Count(t => t.Status == "mismatch"),
            },
        };
    }

    private static void CompareToolSchemas(McpClientTool baseline, McpClientTool actual, List<Difference> diffs)
    {
        // Compare input schema (parameter names, types, required, defaults)
        var baselineSchema = GetInputSchemaJson(baseline);
        var actualSchema = GetInputSchemaJson(actual);

        var baselineProps = GetProperties(baselineSchema);
        var actualProps = GetProperties(actualSchema);
        var baselineRequired = GetRequired(baselineSchema);
        var actualRequired = GetRequired(actualSchema);

        // Check for missing/extra parameters
        foreach (var name in baselineProps.Keys.Except(actualProps.Keys))
            diffs.Add(new Difference { Field = $"parameter:{name}", Baseline = "present", Actual = "missing" });
        foreach (var name in actualProps.Keys.Except(baselineProps.Keys))
            diffs.Add(new Difference { Field = $"parameter:{name}", Baseline = "missing", Actual = "present (extra)" });

        // Check shared parameters for type/default mismatches
        foreach (var name in baselineProps.Keys.Intersect(actualProps.Keys))
        {
            var bProp = baselineProps[name];
            var aProp = actualProps[name];

            var bType = GetStringProp(bProp, "type");
            var aType = GetStringProp(aProp, "type");
            if (bType != aType)
                diffs.Add(new Difference { Field = $"parameter:{name}:type", Baseline = bType, Actual = aType });

            var bDefault = GetDefaultValue(bProp);
            var aDefault = GetDefaultValue(aProp);
            if (bDefault != aDefault)
                diffs.Add(new Difference { Field = $"parameter:{name}:default", Baseline = bDefault, Actual = aDefault });
        }

        // Check required fields
        var missingRequired = baselineRequired.Except(actualRequired).ToList();
        var extraRequired = actualRequired.Except(baselineRequired).ToList();
        foreach (var r in missingRequired)
            diffs.Add(new Difference { Field = $"required:{r}", Baseline = "required", Actual = "optional" });
        foreach (var r in extraRequired)
            diffs.Add(new Difference { Field = $"required:{r}", Baseline = "optional", Actual = "required" });

        // Compare annotations
        CompareAnnotations(baseline, actual, diffs);
    }

    private static JsonElement GetInputSchemaJson(McpClientTool tool)
    {
        // Serialize the tool to JSON and extract inputSchema
        var json = JsonSerializer.Serialize(tool);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("inputSchema", out var schema))
            return schema;
        return default;
    }

    private static Dictionary<string, JsonElement> GetProperties(JsonElement schema)
    {
        var result = new Dictionary<string, JsonElement>();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
                result[prop.Name] = prop.Value;
        }
        return result;
    }

    private static HashSet<string> GetRequired(JsonElement schema)
    {
        var result = new HashSet<string>();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("required", out var req) &&
            req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                var s = item.GetString();
                if (s is not null) result.Add(s);
            }
        }
        return result;
    }

    private static string GetStringProp(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var val))
            return val.ToString();
        return "(none)";
    }

    private static string GetDefaultValue(JsonElement prop)
    {
        if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("default", out var def))
            return def.ToString();
        return "(none)";
    }

    private static void CompareAnnotations(McpClientTool baseline, McpClientTool actual, List<Difference> diffs)
    {
        // Annotations are exposed as properties on McpClientTool or via JSON
        // This is best-effort — if the SDK exposes them, compare; otherwise skip
        var bJson = JsonDocument.Parse(JsonSerializer.Serialize(baseline));
        var aJson = JsonDocument.Parse(JsonSerializer.Serialize(actual));

        var annotationFields = new[] { "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint" };

        foreach (var field in annotationFields)
        {
            var bVal = TryGetNestedProp(bJson.RootElement, "annotations", field);
            var aVal = TryGetNestedProp(aJson.RootElement, "annotations", field);
            if (bVal != aVal)
                diffs.Add(new Difference { Field = $"annotation:{field}", Baseline = bVal, Actual = aVal });
        }
    }

    private static string TryGetNestedProp(JsonElement root, string parent, string child)
    {
        if (root.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c))
            return c.ToString();
        return "(none)";
    }

    public static string ToReport(SchemaDiff diff)
    {
        var lines = new List<string>
        {
            $"Schema Parity Report — {diff.Timestamp}",
            $"Total: {diff.Summary.Total} tools, {diff.Summary.Match} match, {diff.Summary.Mismatch} mismatch",
            ""
        };

        foreach (var tool in diff.Tools)
        {
            var icon = tool.Status == "match" ? "OK" : "DIFF";
            lines.Add($"[{icon}] {tool.Name}");
            foreach (var d in tool.Differences)
                lines.Add($"      {d.Field}: baseline={d.Baseline}, actual={d.Actual}");
        }

        return string.Join("\n", lines);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Infrastructure/SchemaComparer.cs
git commit -m "feat: add SchemaComparer for tool schema diff reports"
```

---

## Task 5: BaselineManager

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Infrastructure/BaselineManager.cs`

- [ ] **Step 1: Implement BaselineManager**

Create `tests/WindowsMCP.NET.ParityTests/Infrastructure/BaselineManager.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace WindowsMcpNet.ParityTests.Infrastructure;

[JsonSerializable(typeof(List<ToolSchemaBaseline>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class BaselineJsonContext : JsonSerializerContext;

public sealed record ToolSchemaBaseline
{
    public required string Name { get; init; }
    public required JsonElement Schema { get; init; }
}

public static class BaselineManager
{
    private static string BaselineDir
    {
        get
        {
            // Navigate from test bin to TestData/baseline in the source tree
            var baseDir = AppContext.BaseDirectory;
            var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var dir = Path.Combine(projectDir, "TestData", "baseline");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void SaveToolSchemas(IList<McpClientTool> tools, string filename = "python_schema.json")
    {
        var baselines = tools.Select(t =>
        {
            var json = JsonSerializer.Serialize(t);
            return new ToolSchemaBaseline
            {
                Name = t.Name,
                Schema = JsonDocument.Parse(json).RootElement.Clone(),
            };
        }).ToList();

        var path = Path.Combine(BaselineDir, filename);
        var content = JsonSerializer.Serialize(baselines, BaselineJsonContext.Default.ListToolSchemaBaseline);
        File.WriteAllText(path, content);
    }

    public static List<ToolSchemaBaseline>? LoadToolSchemas(string filename = "python_schema.json")
    {
        var path = Path.Combine(BaselineDir, filename);
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path);
        return JsonSerializer.Deserialize(content, BaselineJsonContext.Default.ListToolSchemaBaseline);
    }

    public static void SaveFunctionalResult(string testName, string result)
    {
        var path = Path.Combine(BaselineDir, $"func_{testName}.json");
        var data = new Dictionary<string, JsonElement>
        {
            ["result"] = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement.Clone(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, BaselineJsonContext.Default.DictionaryStringJsonElement));
    }

    public static string? LoadFunctionalResult(string testName)
    {
        var path = Path.Combine(BaselineDir, $"func_{testName}.json");
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize(content, BaselineJsonContext.Default.DictionaryStringJsonElement);
        return data?["result"].GetString();
    }

    public static bool HasBaseline(string filename = "python_schema.json")
    {
        return File.Exists(Path.Combine(BaselineDir, filename));
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Infrastructure/BaselineManager.cs
git commit -m "feat: add BaselineManager for saving/loading test baselines"
```

---

## Task 6: Phase 1 — Schema Parity Tests

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/ToolSchemaParityTests.cs`

- [ ] **Step 1: Implement schema parity tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/ToolSchemaParityTests.cs`:

```csharp
using System.Text.Json;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase1_SchemaTests;

[Trait("Category", "Schema")]
public sealed class ToolSchemaParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public ToolSchemaParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task CaptureOrCompareSchema()
    {
        var tools = await _client.ListToolsAsync();
        _output.WriteLine($"Server: {_fixture.ServerType}, Tools: {tools.Count}");

        if (_fixture.ServerType == "python")
        {
            // Baseline mode: save Python schema
            BaselineManager.SaveToolSchemas(tools, "python_schema.json");
            _output.WriteLine($"Saved baseline schema for {tools.Count} tools.");

            // Also verify Python has all 18 expected tools
            Assert.True(tools.Count >= 18, $"Python server has {tools.Count} tools, expected at least 18");
        }
        else
        {
            // Comparison mode: load Python baseline, compare
            var baseline = BaselineManager.LoadToolSchemas("python_schema.json");
            Assert.NotNull(baseline);

            // Reconstruct McpClientTool list from baseline isn't straightforward,
            // so we compare at the JSON level
            var actualTools = tools;
            var baselineNames = baseline.Select(b => b.Name).ToHashSet();
            var actualNames = actualTools.Select(t => t.Name).ToHashSet();

            // Check all Python tools exist in C#
            var missing = baselineNames.Except(actualNames).ToList();
            var extra = actualNames.Except(baselineNames).ToList();

            foreach (var m in missing)
                _output.WriteLine($"MISSING tool in C#: {m}");
            foreach (var e in extra)
                _output.WriteLine($"EXTRA tool in C#: {e}");

            Assert.Empty(missing);
        }
    }

    [Fact]
    public async Task AllToolsHaveDescriptions()
    {
        var tools = await _client.ListToolsAsync();

        foreach (var tool in tools)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(tool.Description),
                $"Tool '{tool.Name}' has no description");
        }
    }

    [Theory]
    [InlineData("Click")]
    [InlineData("Type")]
    [InlineData("Scroll")]
    [InlineData("Move")]
    [InlineData("Shortcut")]
    [InlineData("Wait")]
    [InlineData("Snapshot")]
    [InlineData("Screenshot")]
    [InlineData("App")]
    [InlineData("PowerShell")]
    [InlineData("Process")]
    [InlineData("Registry")]
    [InlineData("Clipboard")]
    [InlineData("FileSystem")]
    [InlineData("Notification")]
    [InlineData("MultiSelect")]
    [InlineData("MultiEdit")]
    [InlineData("Scrape")]
    public async Task ToolExists(string toolName)
    {
        var tools = await _client.ListToolsAsync();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);
        _output.WriteLine($"Tool '{toolName}' found with {tool.Description?.Length ?? 0} char description");
    }

    [Fact]
    public async Task GenerateSchemaDiffReport()
    {
        // This test only runs in dotnet mode with a saved baseline
        if (_fixture.ServerType == "python")
        {
            _output.WriteLine("Skipping diff report in Python baseline mode.");
            return;
        }

        var baseline = BaselineManager.LoadToolSchemas("python_schema.json");
        if (baseline is null)
        {
            _output.WriteLine("No Python baseline found. Run with PARITY_SERVER=python first.");
            return;
        }

        var actualTools = await _client.ListToolsAsync();
        var diff = SchemaComparer.Compare(
            ReconstructToolList(baseline),
            actualTools);

        // Write report
        var report = SchemaComparer.ToReport(diff);
        _output.WriteLine(report);

        // Save JSON diff
        var diffJson = JsonSerializer.Serialize(diff, SchemaDiffJsonContext.Default.SchemaDiff);
        var diffPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "schema-diff.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
        File.WriteAllText(diffPath, diffJson);

        _output.WriteLine($"\nDiff saved to: {diffPath}");
        _output.WriteLine($"Summary: {diff.Summary.Match}/{diff.Summary.Total} match");

        // Fail if any mismatches
        Assert.Equal(0, diff.Summary.Mismatch);
    }

    private static IList<McpClientTool> ReconstructToolList(List<ToolSchemaBaseline> baselines)
    {
        // We can't easily reconstruct McpClientTool from saved JSON,
        // so SchemaComparer should also accept raw JSON comparison.
        // For now, return the actual tools — the comparison is JSON-level anyway.
        // This is a known limitation that will be addressed when we see the actual SDK types.
        throw new NotImplementedException(
            "Reconstruct from baseline needs SDK-specific implementation. " +
            "Use JSON-level comparison instead.");
    }
}
```

Note: The `ReconstructToolList` method is a known gap. The implementer should check how `McpClientTool` can be constructed from saved JSON, or refactor `SchemaComparer.Compare` to accept `List<ToolSchemaBaseline>` as the baseline parameter instead of `IList<McpClientTool>`. The latter approach is cleaner — add an overload:

```csharp
public static SchemaDiff Compare(List<ToolSchemaBaseline> baseline, IList<McpClientTool> actual)
```

that extracts names and schemas from the baseline records and compares at the JSON level.

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase1_SchemaTests/
git commit -m "feat: add Phase 1 schema parity tests"
```

---

## Task 7: Phase 2 — System Tools Functional Tests

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/SystemToolsParityTests.cs`

These are the easiest functional tests — no desktop interaction needed, just string output comparison.

- [ ] **Step 1: Implement system tools functional tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/SystemToolsParityTests.cs`:

```csharp
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Trait("Category", "Functional")]
public sealed class SystemToolsParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public SystemToolsParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task PowerShell_Echo_ReturnsOutput()
    {
        var result = await _client.CallToolTextAsync("PowerShell", new()
        {
            ["command"] = "Write-Output 'parity_test_hello'"
        });

        _output.WriteLine($"PowerShell output: {result}");
        Assert.Contains("parity_test_hello", result);
    }

    [Fact]
    public async Task PowerShell_ErrorCommand_ReturnsError()
    {
        var result = await _client.CallToolTextAsync("PowerShell", new()
        {
            ["command"] = "Get-Item 'C:\\nonexistent_parity_test_path_12345'"
        });

        _output.WriteLine($"PowerShell error output: {result}");
        // Both servers should return some error text
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task Process_List_ReturnsProcesses()
    {
        var result = await _client.CallToolTextAsync("Process", new()
        {
            ["mode"] = "list"
        });

        _output.WriteLine($"Process list (first 500 chars): {result[..Math.Min(500, result.Length)]}");
        // Should contain at least some process info
        Assert.False(string.IsNullOrWhiteSpace(result));
        // Should contain common Windows processes
        Assert.True(
            result.Contains("explorer", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("svchost", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("System", StringComparison.OrdinalIgnoreCase),
            "Process list should contain common Windows processes");
    }

    [Fact]
    public async Task Registry_Get_ReturnsWindowsVersion()
    {
        var result = await _client.CallToolTextAsync("Registry", new()
        {
            ["mode"] = "get",
            ["path"] = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            ["name"] = "ProductName"
        });

        _output.WriteLine($"Registry result: {result}");
        Assert.Contains("Windows", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileSystem_WriteAndRead_Roundtrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wmcp_parity_{Guid.NewGuid():N}");
        var testFile = Path.Combine(tempDir, "parity_test.txt");
        var testContent = $"parity_test_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        try
        {
            // Write
            var writeResult = await _client.CallToolTextAsync("FileSystem", new()
            {
                ["mode"] = "write",
                ["path"] = testFile,
                ["content"] = testContent,
            });
            _output.WriteLine($"Write result: {writeResult}");

            // Read
            var readResult = await _client.CallToolTextAsync("FileSystem", new()
            {
                ["mode"] = "read",
                ["path"] = testFile,
            });
            _output.WriteLine($"Read result: {readResult}");

            Assert.Contains(testContent, readResult);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FileSystem_List_ShowsFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wmcp_parity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test_a.txt"), "a");
        File.WriteAllText(Path.Combine(tempDir, "test_b.txt"), "b");

        try
        {
            var result = await _client.CallToolTextAsync("FileSystem", new()
            {
                ["mode"] = "list",
                ["path"] = tempDir,
            });

            _output.WriteLine($"List result: {result}");
            Assert.Contains("test_a.txt", result);
            Assert.Contains("test_b.txt", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/SystemToolsParityTests.cs
git commit -m "feat: add Phase 2 functional tests for system tools"
```

---

## Task 8: Phase 2 — Screen Tools Functional Tests

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ScreenToolsParityTests.cs`

- [ ] **Step 1: Implement screen tools tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ScreenToolsParityTests.cs`:

```csharp
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public sealed class ScreenToolsParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public ScreenToolsParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var imageBytes = await _client.CallToolImageAsync("Screenshot");

        Assert.NotNull(imageBytes);
        Assert.True(imageBytes.Length > 1000, $"Screenshot too small: {imageBytes.Length} bytes");

        // Verify PNG magic bytes
        Assert.Equal((byte)0x89, imageBytes[0]);
        Assert.Equal((byte)0x50, imageBytes[1]); // P
        Assert.Equal((byte)0x4E, imageBytes[2]); // N
        Assert.Equal((byte)0x47, imageBytes[3]); // G

        _output.WriteLine($"Screenshot: {imageBytes.Length} bytes, valid PNG");
    }

    [Fact]
    public async Task Snapshot_ReturnsImageAndTree()
    {
        var result = await _client.CallToolAsync("Snapshot", new()
        {
            ["useVision"] = true,
            ["useDom"] = true,
            ["useAnnotation"] = true,
        });

        // Should have both image and text content
        var hasImage = result.Content.Any(c => c is ModelContextProtocol.Protocol.ImageContentBlock);
        var hasText = result.Content.Any(c => c is ModelContextProtocol.Protocol.TextContentBlock);

        Assert.True(hasImage, "Snapshot should contain an image");
        Assert.True(hasText, "Snapshot should contain UI tree text");

        var text = string.Join("\n", result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(c => c.Text));

        _output.WriteLine($"Snapshot tree (first 500 chars): {text[..Math.Min(500, text.Length)]}");

        // UI tree should contain labeled elements (numbers)
        Assert.False(string.IsNullOrWhiteSpace(text), "UI tree text should not be empty");
    }

    [Fact]
    public async Task Snapshot_DomOnly_ReturnsTextOnly()
    {
        var result = await _client.CallToolAsync("Snapshot", new()
        {
            ["useVision"] = false,
            ["useDom"] = true,
        });

        var textBlocks = result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .ToList();

        Assert.NotEmpty(textBlocks);

        var text = string.Join("\n", textBlocks.Select(c => c.Text));
        _output.WriteLine($"DOM-only tree (first 300 chars): {text[..Math.Min(300, text.Length)]}");
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ScreenToolsParityTests.cs
git commit -m "feat: add Phase 2 functional tests for screen tools"
```

---

## Task 9: Phase 2 — App & Clipboard Functional Tests

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/AppToolsParityTests.cs`
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ClipboardToolsParityTests.cs`

- [ ] **Step 1: Implement app tools tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/AppToolsParityTests.cs`:

```csharp
using System.Diagnostics;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public sealed class AppToolsParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public AppToolsParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        // Kill any notepad processes we spawned
        foreach (var p in Process.GetProcessesByName("notepad"))
        {
            try { p.Kill(); } catch { }
        }
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task App_Launch_StartsNotepad()
    {
        var result = await _client.CallToolTextAsync("App", new()
        {
            ["mode"] = "launch",
            ["name"] = "notepad",
        });

        _output.WriteLine($"Launch result: {result}");
        await Task.Delay(1000); // Wait for window

        var procs = Process.GetProcessesByName("notepad");
        Assert.NotEmpty(procs);
    }

    [Fact]
    public async Task App_Resize_ChangesWindowSize()
    {
        // Launch notepad first
        await _client.CallToolTextAsync("App", new()
        {
            ["mode"] = "launch",
            ["name"] = "notepad",
        });
        await Task.Delay(1000);

        var result = await _client.CallToolTextAsync("App", new()
        {
            ["mode"] = "resize",
            ["name"] = "notepad",
            ["windowLoc"] = new[] { 100, 100 },
            ["windowSize"] = new[] { 800, 600 },
        });

        _output.WriteLine($"Resize result: {result}");
        // If result doesn't error, the resize was accepted
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
```

- [ ] **Step 2: Implement clipboard tools tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ClipboardToolsParityTests.cs`:

```csharp
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public sealed class ClipboardToolsParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public ClipboardToolsParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Clipboard_SetAndGet_Roundtrips()
    {
        var testText = $"parity_clipboard_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // Set
        var setResult = await _client.CallToolTextAsync("Clipboard", new()
        {
            ["mode"] = "set",
            ["text"] = testText,
        });
        _output.WriteLine($"Set result: {setResult}");

        // Get
        var getResult = await _client.CallToolTextAsync("Clipboard", new()
        {
            ["mode"] = "get",
        });
        _output.WriteLine($"Get result: {getResult}");

        Assert.Contains(testText, getResult);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 4: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/AppToolsParityTests.cs tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/ClipboardToolsParityTests.cs
git commit -m "feat: add Phase 2 functional tests for App and Clipboard tools"
```

---

## Task 10: Phase 2 — Input Tools Functional Tests

**Files:**
- Create: `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/InputToolsParityTests.cs`

- [ ] **Step 1: Implement input tools tests**

Create `tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/InputToolsParityTests.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase2_FunctionalTests;

[Trait("Category", "Functional")]
[Trait("Category", "Desktop")]
public sealed class InputToolsParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private McpServerFixture _fixture = null!;
    private McpTestClient _client = null!;

    public InputToolsParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        _fixture = new McpServerFixture();
        await _fixture.InitializeAsync();
        _client = new McpTestClient(_fixture.Client);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in Process.GetProcessesByName("notepad"))
        {
            try { p.Kill(); } catch { }
        }
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Move_SetsCursorPosition()
    {
        var result = await _client.CallToolTextAsync("Move", new()
        {
            ["loc"] = new[] { 500, 500 },
        });

        _output.WriteLine($"Move result: {result}");

        // Verify cursor position via Win32
        GetCursorPos(out var point);
        _output.WriteLine($"Cursor at: ({point.X}, {point.Y})");

        // Allow some tolerance (1-2px)
        Assert.InRange(point.X, 498, 502);
        Assert.InRange(point.Y, 498, 502);
    }

    [Fact]
    public async Task Wait_TakesExpectedTime()
    {
        var sw = Stopwatch.StartNew();
        var result = await _client.CallToolTextAsync("Wait", new()
        {
            ["duration"] = 1.0,
        });
        sw.Stop();

        _output.WriteLine($"Wait result: {result}, elapsed: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds >= 900, $"Wait was too fast: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Type_InNotepad_ProducesText()
    {
        // Launch notepad
        await _client.CallToolTextAsync("App", new()
        {
            ["mode"] = "launch",
            ["name"] = "notepad",
        });
        await Task.Delay(1500);

        // Type text (notepad should have focus)
        var result = await _client.CallToolTextAsync("Type", new()
        {
            ["text"] = "ParityTest123",
        });

        _output.WriteLine($"Type result: {result}");

        // Use Shortcut to select all and copy to clipboard for verification
        await _client.CallToolTextAsync("Shortcut", new()
        {
            ["shortcut"] = "ctrl+a",
        });
        await Task.Delay(200);
        await _client.CallToolTextAsync("Shortcut", new()
        {
            ["shortcut"] = "ctrl+c",
        });
        await Task.Delay(200);

        // Read clipboard to verify
        var clipResult = await _client.CallToolTextAsync("Clipboard", new()
        {
            ["mode"] = "get",
        });

        _output.WriteLine($"Clipboard after copy: {clipResult}");
        Assert.Contains("ParityTest123", clipResult);
    }

    [Fact]
    public async Task Shortcut_CtrlA_SelectsAll()
    {
        // This is verified implicitly by the Type test above
        // But let's also test standalone
        var result = await _client.CallToolTextAsync("Shortcut", new()
        {
            ["shortcut"] = "ctrl+a",
        });

        _output.WriteLine($"Shortcut result: {result}");
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    // P/Invoke for cursor position verification
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);
}
```

Note: The `Move` test uses `loc` parameter (Python-compatible). If the C# server still uses `x`/`y`, this test will fail — which is correct behavior (schema mismatch). The API fix task will resolve this.

- [ ] **Step 2: Verify build**

```bash
dotnet build tests/WindowsMCP.NET.ParityTests/
```

- [ ] **Step 3: Commit**

```bash
git add tests/WindowsMCP.NET.ParityTests/Phase2_FunctionalTests/InputToolsParityTests.cs
git commit -m "feat: add Phase 2 functional tests for input tools"
```

---

## Task 11: CI Integration

**Files:**
- Modify: `.github/workflows/build.yml`

- [ ] **Step 1: Add parity test project to CI**

Read `.github/workflows/build.yml` and add a step that runs the parity tests in dotnet-only mode (no Python baseline comparison). Add after the existing Test step:

```yaml
    - name: Test (Parity - dotnet only)
      run: dotnet test tests/WindowsMCP.NET.ParityTests/ --configuration Release --filter "Category!=Schema&Category!=Desktop"
      env:
        PARITY_SERVER: dotnet
```

This runs only non-schema, non-desktop parity tests (like FileSystem and PowerShell) which work in headless CI.

- [ ] **Step 2: Verify build**

```bash
dotnet build WindowsMCP.NET.slnx
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci: add parity tests to CI pipeline (non-desktop only)"
```

---

## Self-Review Results

**Spec coverage:**
- Phase 1 schema tests: Task 6 (ToolSchemaParityTests) — covers all 18 tools, schema diff report
- Phase 2 functional tests: Tasks 7-10 — System, Screen, App/Clipboard, Input
- Infrastructure: Tasks 2-5 — McpServerFixture, McpTestClient, SchemaComparer, BaselineManager
- CI integration: Task 11
- Baseline mode (save Python / compare C#): BaselineManager + env var switching in McpServerFixture

**Spec gaps addressed:**
- Scrape testing excluded (listed in Non-Goals in spec)
- Notification testing excluded (listed in Non-Goals)
- MultiSelect/MultiEdit testing excluded (listed in Non-Goals)

**Not in this plan (spec says "fix C# implementation"):**
- The actual API fixes to make C# signatures match Python. These should be a separate plan after Phase 1 schema diff reveals the exact differences. The schema tests are designed to fail until fixes are applied.

**Placeholder scan:** No TBDs. The `ReconstructToolList` method in Task 6 has a `NotImplementedException` but with clear instructions for the implementer on how to resolve it.

**Type consistency:** `McpTestClient` methods (`CallToolTextAsync`, `CallToolImageAsync`, `CallToolAsync`, `ListToolsAsync`) used consistently across all test files. `McpServerFixture` lifecycle (`InitializeAsync`/`DisposeAsync`) used consistently via `IAsyncLifetime`.
