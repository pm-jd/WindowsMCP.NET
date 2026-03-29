using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace WindowsMcpNet.ParityTests.Infrastructure;

public sealed record SchemaDiff(IReadOnlyList<ToolDiff> ToolDiffs, DiffSummary Summary)
{
    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Schema Diff Report ===");
        sb.AppendLine($"Tools only in baseline  : {Summary.OnlyInBaseline.Count}");
        sb.AppendLine($"Tools only in actual    : {Summary.OnlyInActual.Count}");
        sb.AppendLine($"Tools with differences  : {Summary.ToolsWithDiffs}");
        sb.AppendLine($"Total differences       : {Summary.TotalDifferences}");
        sb.AppendLine();

        if (Summary.OnlyInBaseline.Count > 0)
        {
            sb.AppendLine("--- Missing from actual (in baseline only) ---");
            foreach (var name in Summary.OnlyInBaseline)
                sb.AppendLine($"  - {name}");
            sb.AppendLine();
        }

        if (Summary.OnlyInActual.Count > 0)
        {
            sb.AppendLine("--- New tools (in actual only) ---");
            foreach (var name in Summary.OnlyInActual)
                sb.AppendLine($"  + {name}");
            sb.AppendLine();
        }

        foreach (var diff in ToolDiffs.Where(d => d.Differences.Count > 0))
        {
            sb.AppendLine($"--- {diff.ToolName} ---");
            foreach (var d in diff.Differences)
                sb.AppendLine($"  [{d.Field}] baseline={d.Baseline ?? "(null)"} actual={d.Actual ?? "(null)"}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public sealed record ToolDiff(string ToolName, IReadOnlyList<Difference> Differences);

public sealed record Difference(string Field, string? Baseline, string? Actual);

public sealed record DiffSummary(
    IReadOnlyList<string> OnlyInBaseline,
    IReadOnlyList<string> OnlyInActual,
    int ToolsWithDiffs,
    int TotalDifferences);

public sealed record ToolSchemaBaseline(string Name, JsonElement Schema);

public static class SchemaComparer
{
    public static SchemaDiff Compare(
        IReadOnlyList<ToolSchemaBaseline> baseline,
        IList<McpClientTool> actual)
    {
        var baselineMap = baseline.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var actualMap = actual.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var onlyInBaseline = baselineMap.Keys.Except(actualMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var onlyInActual = actualMap.Keys.Except(baselineMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        var toolDiffs = new List<ToolDiff>();
        foreach (var name in baselineMap.Keys.Intersect(actualMap.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var diffs = CompareToolSchemas(name, baselineMap[name].Schema, actualMap[name]);
            toolDiffs.Add(new ToolDiff(name, diffs));
        }

        var toolsWithDiffs = toolDiffs.Count(d => d.Differences.Count > 0);
        var totalDifferences = toolDiffs.Sum(d => d.Differences.Count) + onlyInBaseline.Count + onlyInActual.Count;

        var summary = new DiffSummary(onlyInBaseline, onlyInActual, toolsWithDiffs, totalDifferences);
        return new SchemaDiff(toolDiffs, summary);
    }

    private static IReadOnlyList<Difference> CompareToolSchemas(string toolName, JsonElement baselineSchema, McpClientTool actualTool)
    {
        var diffs = new List<Difference>();

        var actualSchema = actualTool.ProtocolTool.InputSchema;

        // Skip description comparison — descriptions are intentionally different between servers.

        // Compare parameters (properties in inputSchema)
        var baselineProps = GetProperties(baselineSchema);
        var actualProps = GetProperties(actualSchema);

        var baselinePropNames = baselineProps?.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var actualPropNames = actualProps?.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var prop in baselinePropNames.Except(actualPropNames, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new Difference($"{toolName}.params.{prop}", "exists", "missing"));

        foreach (var prop in actualPropNames.Except(baselinePropNames, StringComparer.OrdinalIgnoreCase))
            diffs.Add(new Difference($"{toolName}.params.{prop}", "missing", "exists"));

        // Compare types for shared properties
        var baselinePropsDict = baselineProps?.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase) ?? [];
        var actualPropsDict = actualProps?.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var propName in baselinePropNames.Intersect(actualPropNames, StringComparer.OrdinalIgnoreCase))
        {
            var baselineType = GetStringProperty(baselinePropsDict[propName], "type");
            // Skip type comparison when baseline type is null — Python baseline doesn't capture all type info.
            if (baselineType is null) continue;
            var actualType = GetStringProperty(actualPropsDict[propName], "type");
            // Skip type comparison when actual type is null — the C# MCP SDK omits "type" for nullable
            // reference types, while Python always records it. Treat as compatible.
            if (actualType is null) continue;
            if (!string.Equals(baselineType, actualType, StringComparison.Ordinal))
                diffs.Add(new Difference($"{toolName}.params.{propName}.type", baselineType, actualType));
        }

        // Compare required fields
        var baselineRequired = GetRequired(baselineSchema);
        var actualRequired = GetRequired(actualSchema);
        var missingRequired = baselineRequired.Except(actualRequired, StringComparer.OrdinalIgnoreCase).ToList();
        var addedRequired = actualRequired.Except(baselineRequired, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var req in missingRequired)
            diffs.Add(new Difference($"{toolName}.required.{req}", "required", "optional"));
        foreach (var req in addedRequired)
            diffs.Add(new Difference($"{toolName}.required.{req}", "optional", "required"));

        return diffs;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static IEnumerable<JsonProperty>? GetProperties(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return null;
        if (!schema.TryGetProperty("properties", out var props)) return null;
        if (props.ValueKind != JsonValueKind.Object) return null;
        return props.EnumerateObject();
    }

    private static IReadOnlyList<string> GetRequired(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return [];
        if (!schema.TryGetProperty("required", out var req)) return [];
        if (req.ValueKind != JsonValueKind.Array) return [];
        return req.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }
}
