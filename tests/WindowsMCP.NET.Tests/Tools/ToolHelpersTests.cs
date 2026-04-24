using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ToolHelpersTests
{
    [Fact]
    public void Paginate_EmptySource_ReturnsEmpty()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Empty<int>(), 0, 10);
        Assert.Empty(page);
        Assert.False(hasMore);
    }

    [Fact]
    public void Paginate_FewerThanLimit_HasMoreFalse()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 3), 0, 10);
        Assert.Equal(new[] { 1, 2, 3 }, page);
        Assert.False(hasMore);
    }

    [Fact]
    public void Paginate_ExactlyLimit_HasMoreFalse()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 5), 0, 5);
        Assert.Equal(5, page.Count);
        Assert.False(hasMore);
    }

    [Fact]
    public void Paginate_MoreThanLimit_HasMoreTrue()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 10), 0, 5);
        Assert.Equal(5, page.Count);
        Assert.True(hasMore);
    }

    [Fact]
    public void Paginate_OffsetSkips()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 10), 5, 3);
        Assert.Equal(new[] { 6, 7, 8 }, page);
        Assert.True(hasMore);
    }

    [Fact]
    public void Paginate_OffsetBeyondEnd_ReturnsEmpty()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 5), 100, 10);
        Assert.Empty(page);
        Assert.False(hasMore);
    }

    [Fact]
    public void Paginate_NegativeOffset_TreatedAsZero()
    {
        var (page, hasMore) = ToolHelpers.Paginate(Enumerable.Range(1, 3), -5, 10);
        Assert.Equal(3, page.Count);
        Assert.False(hasMore);
    }

    [Fact]
    public void Paginate_ZeroLimit_AppliesDefault()
    {
        var (page, _) = ToolHelpers.Paginate(Enumerable.Range(1, 500), 0, 0);
        Assert.Equal(ToolHelpers.DefaultListLimit, page.Count);
    }

    [Fact]
    public void IsJson_CaseInsensitive()
    {
        Assert.True(ToolHelpers.IsJson("json"));
        Assert.True(ToolHelpers.IsJson("JSON"));
        Assert.True(ToolHelpers.IsJson("Json"));
        Assert.False(ToolHelpers.IsJson("markdown"));
        Assert.False(ToolHelpers.IsJson(""));
    }

    [Fact]
    public void ResolveLimit_ZeroOrNegative_UsesDefault()
    {
        Assert.Equal(ToolHelpers.DefaultListLimit, ToolHelpers.ResolveLimit(0));
        Assert.Equal(ToolHelpers.DefaultListLimit, ToolHelpers.ResolveLimit(-5));
    }

    [Fact]
    public void ResolveLimit_PositiveValue_Honored()
    {
        Assert.Equal(42, ToolHelpers.ResolveLimit(42));
    }

    [Fact]
    public void FormatPaginationFooter_HasMore_ShowsHint()
    {
        var footer = ToolHelpers.FormatPaginationFooter(10, 0, 10, hasMore: true, "item");
        Assert.Contains("offset=10", footer);
        Assert.Contains("has_more=true", footer);
    }

    [Fact]
    public void FormatPaginationFooter_NoMore_OmitsHint()
    {
        var footer = ToolHelpers.FormatPaginationFooter(3, 0, 10, hasMore: false, "item");
        Assert.Contains("has_more=false", footer);
        Assert.DoesNotContain("Use offset=", footer);
    }
}
