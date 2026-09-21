using WindowsMcpNet.Security;
using Xunit;

namespace WindowsMcpNet.Tests.Security;

public class ApiKeyMatchTests
{
    [Theory]
    [InlineData("Bearer wmcp_abc", true)]
    [InlineData("bearer wmcp_abc", true)]
    [InlineData("Bearer  wmcp_abc ", true)]
    [InlineData("Bearer wmcp_abd", false)]
    [InlineData("wmcp_abc", false)]
    [InlineData("Basic wmcp_abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_BearerHeaderAgainstKey(string? header, bool expected)
    {
        Assert.Equal(expected, ApiKeyMiddleware.Matches(header, "wmcp_abc"));
    }
}
