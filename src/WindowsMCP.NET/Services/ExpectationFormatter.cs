using System.Globalization;
using System.Text;
using WindowsMcpNet.Models;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>
/// Renders an <see cref="ExpectOutcome"/> for tool output: a header with the overall result and one
/// line per condition (the tool's default), and a JSON envelope (wrapped by
/// <c>ToolHelpers.JsonResult</c> at the call site). Pure.
/// </summary>
public static class ExpectationFormatter
{
    public static string ToMarkdown(ExpectOutcome o)
    {
        ArgumentNullException.ThrowIfNull(o);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Expect: {o.Result.ToString().ToUpperInvariant()} ({Passed(o)} of {o.Conditions.Count} passed) — ");
        sb.Append(o.Observed.Count == 0 ? "nothing observed" : string.Join(", ", o.Observed)).Append(" — ");
        sb.Append(CultureInfo.InvariantCulture,
            $"{o.Observations} observation{(o.Observations == 1 ? "" : "s")}, {o.ElapsedMs} ms");
        if (o.TimedOut)
            sb.Append(" (timeout)");

        foreach (var c in o.Conditions)
        {
            sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture, $"{Name(c.Result),-7}{c.Index,3} {c.Condition} — {c.Actual}");
            if (c.ElementIds.Count > 0)
                sb.Append(CultureInfo.InvariantCulture, $" ({(c.ElementIds.Count == 1 ? "id" : "ids")} {string.Join(", ", c.ElementIds)})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>{result, passed, total, observed:[{process, pid}], observations, elapsed_ms, timed_out,
    /// conditions:[{index, result, condition, actual, elements?}]}</c> — <c>elements</c> is left out when no element matched. Built
    /// with dictionaries because <c>ToolHelpers.JsonOptions</c> doesn't set <c>DefaultIgnoreCondition</c>.
    /// </summary>
    public static object ToJsonEnvelope(ExpectOutcome o)
    {
        ArgumentNullException.ThrowIfNull(o);

        return new Dictionary<string, object?>
        {
            ["result"] = Name(o.Result),
            ["passed"] = Passed(o),
            ["total"] = o.Conditions.Count,
            ["observed"] = o.Observed
                .Select(p => new Dictionary<string, object?> { ["process"] = p.Process, ["pid"] = p.Pid }).ToList(),
            ["observations"] = o.Observations,
            ["elapsed_ms"] = o.ElapsedMs,
            ["timed_out"] = o.TimedOut,
            ["conditions"] = o.Conditions.Select(ToConditionJson).ToList(),
        };
    }

    private static Dictionary<string, object?> ToConditionJson(ConditionOutcome c)
    {
        var dict = new Dictionary<string, object?>
        {
            ["index"] = c.Index,
            ["result"] = Name(c.Result),
            ["condition"] = c.Condition,
            ["actual"] = c.Actual,
        };
        if (c.ElementIds.Count > 0)
            dict["elements"] = c.ElementIds;

        return dict;
    }

    private static int Passed(ExpectOutcome o) => o.Conditions.Count(c => c.Result == ExpectResult.Pass);

    private static string Name(ExpectResult result) => result.ToString().ToLowerInvariant();
}
