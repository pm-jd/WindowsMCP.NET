using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

public class ProcessWindowMatcherTests
{
    // Z-order is preserved from input order (first = topmost).
    private static WindowInfo Win(nint handle, string title, uint pid) =>
        new(handle, title, pid, 0, 0, 100, 100, true);

    [Fact]
    public void Match_ProcessNameExact_Found()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "chrome"),  Win(1, "Google - Chrome",  100)),
            (new ProcessSnapshot(200, "notepad"), Win(2, "Untitled - Notepad", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Single(result);
        Assert.Equal((nint)2, result[0].Window.Handle);
        Assert.Equal("notepad", result[0].ProcessName);
    }

    [Fact]
    public void Match_ProcessNameCaseInsensitive_Found()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "Notepad"), Win(1, "Untitled - Notepad", 100)),
        };

        var result = ProcessWindowMatcher.Match(input, "NOTEPAD");

        Assert.Single(result);
    }

    [Fact]
    public void Match_DotExeSuffix_NormalizedBothDirections()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"),     Win(1, "A - Notepad", 100)),
            (new ProcessSnapshot(200, "notepad.exe"), Win(2, "B - Notepad", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad.exe");
        Assert.Equal(2, result.Count);

        var result2 = ProcessWindowMatcher.Match(input, "notepad");
        Assert.Equal(2, result2.Count);
    }

    [Fact]
    public void Match_NoProcessMatch_FallsBackToFuzzyTitle()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "Code"), Win(1, "main.cs - Visual Studio Code",  100)),
            (new ProcessSnapshot(200, "foo"),  Win(2, "unrelated",                      200)),
        };

        var result = ProcessWindowMatcher.Match(input, "visual studio code");

        Assert.Single(result);
        Assert.Equal((nint)1, result[0].Window.Handle);
    }

    [Fact]
    public void Match_MultipleExact_PreservesInputZOrder()
    {
        // EnumWindows returns top-to-bottom Z-order.
        // Matcher must preserve that order for the tiebreaker.
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"), Win(1, "A - Notepad", 100)),
            (new ProcessSnapshot(200, "notepad"), Win(2, "B - Notepad", 200)),
            (new ProcessSnapshot(300, "notepad"), Win(3, "C - Notepad", 300)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Equal(3, result.Count);
        Assert.Equal((nint)1, result[0].Window.Handle);
        Assert.Equal((nint)2, result[1].Window.Handle);
        Assert.Equal((nint)3, result[2].Window.Handle);
    }

    [Fact]
    public void Match_ProcessNameBeatsFuzzyTitle()
    {
        // "notepad" as process name exists — fuzzy title match on another window must NOT appear.
        var input = new[]
        {
            (new ProcessSnapshot(100, "notepad"), Win(1, "irrelevant",                  100)),
            (new ProcessSnapshot(200, "foo"),     Win(2, "notepad tutorial — browser", 200)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Single(result);
        Assert.Equal((nint)1, result[0].Window.Handle);
    }

    [Fact]
    public void Match_EmptyInput_ReturnsEmpty()
    {
        var result = ProcessWindowMatcher.Match(
            Array.Empty<(ProcessSnapshot, WindowInfo)>(), "notepad");
        Assert.Empty(result);
    }

    [Fact]
    public void Match_FuzzyScoreBelowThreshold_Excluded()
    {
        var input = new[]
        {
            (new ProcessSnapshot(100, "foo"), Win(1, "xyz", 100)),
        };

        var result = ProcessWindowMatcher.Match(input, "notepad");

        Assert.Empty(result);
    }
}
