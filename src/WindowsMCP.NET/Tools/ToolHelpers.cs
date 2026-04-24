using System.Text.Json;

namespace WindowsMcpNet.Tools;

/// <summary>
/// Shared helpers for tool implementations: pagination, format detection, JSON serialization options.
/// </summary>
public static class ToolHelpers
{
    public const int DefaultListLimit = 200;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool IsJson(string format) =>
        format.Equals("json", StringComparison.OrdinalIgnoreCase);

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
