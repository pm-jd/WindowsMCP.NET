using WindowsMcpNet.ParityTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace WindowsMcpNet.ParityTests.Phase1_SchemaTests;

[Collection("McpServer")]
public class ToolSchemaParityTests : IAsyncLifetime
{
    private readonly McpServerFixture _fixture;
    private readonly ITestOutputHelper _output;
    private McpTestClient _client = null!;

    public ToolSchemaParityTests(McpServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _client = new McpTestClient(_fixture.Client);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Schema")]
    public async Task CaptureOrCompareSchema()
    {
        var tools = await _client.ListToolsAsync();
        _output.WriteLine($"Server type: {_fixture.ServerType}");
        _output.WriteLine($"Tools found: {tools.Count}");

        if (_fixture.ServerType == "python")
        {
            BaselineManager.SaveToolSchemas(tools, "python_tools.json");
            _output.WriteLine("Saved Python baseline to python_tools.json");
        }
        else
        {
            // dotnet mode: verify all tools exist in baseline (if baseline exists)
            if (!BaselineManager.HasBaseline("python_tools.json"))
            {
                _output.WriteLine("No Python baseline found — skipping comparison (run with PARITY_SERVER=python first).");
                Assert.NotEmpty(tools);
                return;
            }

            var baseline = BaselineManager.LoadToolSchemas("python_tools.json")!;
            _output.WriteLine($"Baseline tools: {baseline.Count}");

            var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingTools = baseline.Where(b => !toolNames.Contains(b.Name)).Select(b => b.Name).ToList();

            foreach (var missing in missingTools)
                _output.WriteLine($"  MISSING: {missing}");

            Assert.Empty(missingTools);
        }
    }

    [Fact]
    [Trait("Category", "Schema")]
    public async Task AllToolsHaveDescriptions()
    {
        var tools = await _client.ListToolsAsync();

        var toolsWithoutDescriptions = tools
            .Where(t => string.IsNullOrWhiteSpace(t.Description))
            .Select(t => t.Name)
            .ToList();

        foreach (var name in toolsWithoutDescriptions)
            _output.WriteLine($"  Missing description: {name}");

        Assert.Empty(toolsWithoutDescriptions);
    }

    [Theory]
    [Trait("Category", "Schema")]
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
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _output.WriteLine($"Looking for tool: {toolName}");
        _output.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name).OrderBy(x => x))}");
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    [Trait("Category", "Schema")]
    public async Task GenerateSchemaDiffReport()
    {
        if (_fixture.ServerType != "dotnet")
        {
            _output.WriteLine("Skipping diff report — only runs in dotnet mode.");
            return;
        }

        if (!BaselineManager.HasBaseline("python_tools.json"))
        {
            _output.WriteLine("No Python baseline found — skipping diff report (run with PARITY_SERVER=python first).");
            return;
        }

        var baseline = BaselineManager.LoadToolSchemas("python_tools.json")!;
        var actualTools = await _client.ListToolsAsync();

        _output.WriteLine($"Baseline tool count: {baseline.Count}");
        _output.WriteLine($"Actual tool count:   {actualTools.Count}");

        var diff = SchemaComparer.Compare(baseline, actualTools);
        var report = diff.ToReport();

        _output.WriteLine(report);

        // Write report to test data for reference
        var reportPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "WindowsMCP.NET.ParityTests", "TestData");
        Directory.CreateDirectory(Path.GetFullPath(reportPath));
        File.WriteAllText(
            Path.Combine(Path.GetFullPath(reportPath), "schema_diff_report.txt"),
            report);

        Assert.Equal(0, diff.Summary.TotalDifferences);
    }
}
