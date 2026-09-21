using System.Text;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class OutputCapTests
{
    [Fact]
    public void AppendCapped_WithinBudget_AppendsLineAndReturnsTrue()
    {
        var sb = new StringBuilder();

        var ok = ToolHelpers.AppendCapped(sb, "hello", maxChars: 100);

        Assert.True(ok);
        Assert.Equal("hello" + Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void AppendCapped_LineExceedsBudget_AppendsPrefixAndReturnsFalse()
    {
        var sb = new StringBuilder();

        var ok = ToolHelpers.AppendCapped(sb, "0123456789ABCDE", maxChars: 10);

        Assert.False(ok);
        Assert.Equal("0123456789", sb.ToString());
    }

    [Fact]
    public void AppendCapped_BudgetExhausted_ReturnsFalseWithoutAppending()
    {
        var sb = new StringBuilder("0123456789");

        var ok = ToolHelpers.AppendCapped(sb, "more", maxChars: 10);

        Assert.False(ok);
        Assert.Equal("0123456789", sb.ToString());
    }

    [Fact]
    public async Task PowerShell_LargeOutput_IsTruncatedWithMarker()
    {
        // 1.2 million chars on one line — well above the 1,000,000 char cap.
        var result = await SystemTools.PowerShell("'x' * 1200000", timeout: 60, ct: TestContext.Current.CancellationToken);

        Assert.Contains("[Output truncated at 1,000,000 characters]", result);
        Assert.InRange(result.Length, 1_000_000, 1_000_200);
    }
}
