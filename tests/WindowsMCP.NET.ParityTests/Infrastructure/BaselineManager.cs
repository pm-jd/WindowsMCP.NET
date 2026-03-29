using System.Text.Json;
using ModelContextProtocol.Client;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public static class BaselineManager
{
    private static readonly string BaselineDir = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "tests", "WindowsMCP.NET.ParityTests", "TestData", "baseline");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static void SaveToolSchemas(IList<McpClientTool> tools, string filename)
    {
        Directory.CreateDirectory(GetBaselineDir());
        var baselines = tools.Select(t => new ToolSchemaBaseline(
            t.Name,
            t.ProtocolTool.InputSchema)).ToList();

        var json = JsonSerializer.Serialize(baselines, JsonOptions);
        File.WriteAllText(GetPath(filename), json);
    }

    public static IReadOnlyList<ToolSchemaBaseline>? LoadToolSchemas(string filename)
    {
        var path = GetPath(filename);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ToolSchemaBaseline>>(json, JsonOptions);
    }

    public static void SaveFunctionalResult(string testName, string result)
    {
        Directory.CreateDirectory(GetBaselineDir());
        File.WriteAllText(GetPath(testName + ".txt"), result);
    }

    public static string? LoadFunctionalResult(string testName)
    {
        var path = GetPath(testName + ".txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static bool HasBaseline(string filename)
        => File.Exists(GetPath(filename));

    private static string GetBaselineDir()
        => Path.GetFullPath(BaselineDir);

    private static string GetPath(string filename)
        => Path.Combine(GetBaselineDir(), filename);
}
