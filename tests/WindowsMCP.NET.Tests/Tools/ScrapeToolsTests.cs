using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ScrapeToolsTests
{
    private const string SampleHtml =
        "<html><body>" +
        "<h1>Title</h1>" +
        "<p>Hello <b>world</b> and <a href=\"https://example.com/x\">link</a>.</p>" +
        "<!-- a comment that must vanish -->" +
        "<ul><li>one</li><li>two</li></ul>" +
        "<custom-tag>kept</custom-tag>" +
        "</body></html>";

    [Fact]
    public void ConvertHtml_ProducesGithubFlavoredMarkdown()
    {
        var md = ScrapeTools.ConvertHtml(SampleHtml, query: null);

        Assert.Contains("# Title", md);
        Assert.Contains("**world**", md);
        Assert.Contains("[link](https://example.com/x)", md);
        Assert.Contains("- one", md);
        Assert.Contains("- two", md);
    }

    [Fact]
    public void ConvertHtml_RemovesCommentsAndPassesUnknownTagsThrough()
    {
        var md = ScrapeTools.ConvertHtml(SampleHtml, query: null);

        Assert.DoesNotContain("a comment that must vanish", md);
        Assert.Contains("kept", md);
    }

    [Fact]
    public void ConvertHtml_QueryFiltersToMatchingLines()
    {
        var md = ScrapeTools.ConvertHtml(SampleHtml, query: "WORLD");

        Assert.Contains("world", md);
        Assert.DoesNotContain("Title", md);
        Assert.DoesNotContain("- one", md);
    }

    [Fact]
    public void ConvertHtml_TruncatesOversizedOutput()
    {
        var huge = "<p>" + new string('x', 60_000) + "</p>";

        var md = ScrapeTools.ConvertHtml(huge, query: null);

        Assert.Contains("[Truncated at 50,000 characters]", md);
        Assert.True(md.Length < 60_000);
    }
}
