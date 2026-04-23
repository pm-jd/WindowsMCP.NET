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

        var exactMatches = candidates
            .Where(c => StripExe(c.Process.Name).Equals(needle, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.Window, c.Process.Name))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        // Fuzzy-title fallback.
        return candidates
            .Select(c => (c.Window, c.Process.Name,
                Score: Fuzz.PartialRatio(name.ToLowerInvariant(), c.Window.Title.ToLowerInvariant())))
            .Where(x => x.Score >= FuzzyThreshold)
            .Select(x => (x.Window, x.Name))
            .ToList();
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
}
