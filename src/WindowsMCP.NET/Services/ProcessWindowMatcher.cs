using FuzzySharp;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public static class ProcessWindowMatcher
{
    private const int FuzzyThreshold = 60;

    /// <summary>
    /// Find matches for a given name against (process, window) candidates.
    /// Strategy:
    ///   1. Exact process-name match (case-insensitive, .exe suffix normalized)
    ///   2. Fuzzy window-title fallback (PartialRatio >= 60) if no process match
    /// Input order is preserved (callers pass Z-ordered lists from EnumWindows).
    /// </summary>
    public static List<(WindowInfo Window, string ProcessName)> Match(
        IEnumerable<(ProcessSnapshot Process, WindowInfo Window)> candidates,
        string name)
    {
        var needle = StripExe(name).ToLowerInvariant();
        // Materialize once so both passes read the same snapshot
        // (callers may pass live LINQ queries).
        var materialized = candidates.ToList();

        var exactMatches = materialized
            .Where(c => StripExe(c.Process.Name).Equals(needle, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.Window, c.Process.Name))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        // Fuzzy-title fallback.
        return materialized
            .Where(c =>
            {
                var candStripped = StripExe(c.Process.Name).ToLowerInvariant();
                // Reject sibling apps (same prefix, different name) — e.g. "notepad" must not match "notepad++"
                if (candStripped != needle && candStripped.StartsWith(needle))
                    return false;
                var title = c.Window.Title.ToLowerInvariant();
                // Same rule on the title: Explorer's "Eigenschaften von Notepad++" carries no
                // process-name hint, yet "notepad" only appears as the prefix of another product.
                if (ContainsOnlyAsLongerToken(title, needle))
                    return false;
                return Fuzz.PartialRatio(needle, title) >= FuzzyThreshold;
            })
            .Select(c => (c.Window, c.Process.Name))
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="needle"/> occurs in <paramref name="title"/> only as the start of a
    /// longer product token ("notepad" in "notepad++" or "notepad2"), never as a whole token.
    /// A title without any literal occurrence returns false so genuine fuzzy matches still work.
    /// </summary>
    private static bool ContainsOnlyAsLongerToken(string title, string needle)
    {
        var found = false;
        for (var i = title.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = title.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            found = true;
            var end = i + needle.Length;
            var startsToken = i == 0 || !char.IsLetterOrDigit(title[i - 1]);
            var endsToken = end >= title.Length || !IsTokenChar(title[end]);
            if (startsToken && endsToken)
                return false;
        }
        return found;
    }

    private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c is '+' or '#';

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
}
