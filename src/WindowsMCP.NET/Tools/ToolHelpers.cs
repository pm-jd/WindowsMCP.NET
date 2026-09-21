using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WindowsMcpNet.Tools;

/// <summary>
/// Shared helpers for tool implementations: pagination, format detection, JSON serialization options,
/// output capping for streamed process output.
/// </summary>
public static class ToolHelpers
{
    public const int DefaultListLimit = 200;

    /// <summary>Cap for streamed tool output (PowerShell stdout/stderr); mirrors the FileSystem 1 MB read limit.</summary>
    public const int MaxOutputChars = 1_000_000;

    /// <summary>
    /// Appends <paramref name="line"/> plus a newline while the builder stays within <paramref name="maxChars"/>.
    /// Returns false once the cap is reached (the line is cut at the cap); callers then append
    /// <see cref="TruncationMarker"/> so the client knows output was dropped.
    /// </summary>
    public static bool AppendCapped(StringBuilder sb, string line, int maxChars)
    {
        var remaining = maxChars - sb.Length;
        if (remaining <= 0) return false;

        if (line.Length >= remaining)
        {
            sb.Append(line, 0, remaining);
            return false;
        }

        sb.AppendLine(line);
        return true;
    }

    public static string TruncationMarker(int maxChars) =>
        $"\n[Output truncated at {maxChars.ToString("N0", CultureInfo.InvariantCulture)} characters]";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>[x, y] coordinate array to point; null when missing or too short.</summary>
    public static (int X, int Y)? ToPoint(int[]? coords) =>
        coords is { Length: >= 2 } ? (coords[0], coords[1]) : null;

    /// <summary>Lower-case enum name for human-readable tool output ("left", "json").</summary>
    public static string Lower(this Enum value) => value.ToString().ToLowerInvariant();

    public static int ResolveLimit(int limit, int defaultLimit = DefaultListLimit) =>
        limit > 0 ? limit : defaultLimit;

    /// <summary>
    /// Lazily slices an enumerable: skips offset, takes limit+1 items so HasMore can be
    /// determined without materializing the whole sequence.
    /// </summary>
    public static (List<T> Page, bool HasMore) Paginate<T>(IEnumerable<T> source, int offset, int limit)
    {
        if (offset < 0) offset = 0;
        if (limit <= 0) limit = DefaultListLimit;

        var slice = source.Skip(offset).Take(limit + 1).ToList();
        bool hasMore = slice.Count > limit;
        if (hasMore) slice.RemoveAt(slice.Count - 1);
        return (slice, hasMore);
    }

    public static string FormatPaginationFooter(int count, int offset, int limit, bool hasMore, string itemNoun)
    {
        var nextHint = hasMore ? $" Use offset={offset + count} for more." : "";
        return $"Returned: {count} {itemNoun}(s) (offset={offset}, limit={limit}, has_more={hasMore.ToString().ToLowerInvariant()}).{nextHint}";
    }
}
