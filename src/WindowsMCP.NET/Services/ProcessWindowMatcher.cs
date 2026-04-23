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
                return Fuzz.PartialRatio(needle, c.Window.Title.ToLowerInvariant()) >= FuzzyThreshold;
            })
            .Select(c => (c.Window, c.Process.Name))
            .ToList();
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
}
