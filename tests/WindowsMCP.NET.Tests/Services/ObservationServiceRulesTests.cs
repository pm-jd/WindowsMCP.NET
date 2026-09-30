using System.Drawing;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ObservationService.IsCollectible"/> is the one eligibility rule shared by the cached
/// walk (dropping offscreen/too-small subtrees) and <c>FindLive</c>'s live sibling-index counting and
/// fallback uniqueness check. Both sides must agree on exactly which nodes count, or a stored id can
/// walk into the wrong live sibling — see fix round 1 review finding.
/// </summary>
public class ObservationServiceRulesTests
{
    [Theory]
    [InlineData(true, 20, 20, false)]   // offscreen — dropped regardless of size
    [InlineData(false, 1, 20, false)]   // 1 px wide
    [InlineData(false, 20, 1, false)]   // 1 px tall
    [InlineData(false, 2, 2, true)]     // on-screen and big enough
    public void IsCollectible_MatchesCollectorDropRule(bool isOffscreen, int width, int height, bool expected)
    {
        var rect = new Rectangle(0, 0, width, height);

        Assert.Equal(expected, ObservationService.IsCollectible(isOffscreen, rect));
    }
}
