using System.Drawing;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// Pure rules of <see cref="ObservationService"/> that must hold without a desktop:
/// <see cref="ObservationService.IsCollectible"/> is the one eligibility rule shared by the cached
/// walk (dropping offscreen/too-small subtrees) and <c>FindLive</c>'s live sibling-index counting and
/// fallback uniqueness check — both sides must agree on exactly which nodes count, or a stored id can
/// walk into the wrong live sibling. <see cref="ObservationService.SelectResolutionWindows"/> is the
/// window-affinity rule that decides in which live windows a stored id may be resolved.
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

    // --- window affinity (F4): matching = visible windows of the locator's process name and class, z-order ---

    [Fact]
    public void SelectResolutionWindows_StoredWindowStillThere_ResolvesOnlyInThatWindow()
    {
        nint[] matching = [0x30, 0x20, 0x10];

        Assert.Equal([(nint)0x20], ObservationService.SelectResolutionWindows(0x20, transient: false, matching));
        Assert.Equal([(nint)0x20], ObservationService.SelectResolutionWindows(0x20, transient: true, matching));
    }

    [Fact]
    public void SelectResolutionWindows_TransientWindowGone_ResolvesNowhere()
    {
        // A remembered dialog/menu id must not resolve in a later dialog of the same class.
        nint[] matching = [0x30, 0x10];

        Assert.Empty(ObservationService.SelectResolutionWindows(0x20, transient: true, matching));
    }

    [Fact]
    public void SelectResolutionWindows_MainWindowGone_FallsBackToSameClassWindowsInZOrder()
    {
        // The application was restarted: its main window has a new handle, the id keeps working.
        nint[] matching = [0x30, 0x10];

        Assert.Equal(matching, ObservationService.SelectResolutionWindows(0x20, transient: false, matching));
    }

    [Fact]
    public void SelectResolutionWindows_UnknownHandle_FallsBackOnlyWhenNotTransient()
    {
        nint[] matching = [0x30, 0x10];

        Assert.Equal(matching, ObservationService.SelectResolutionWindows(0, transient: false, matching));
        Assert.Empty(ObservationService.SelectResolutionWindows(0, transient: true, matching));
    }

    [Fact]
    public void SelectResolutionWindows_NoMatchingWindow_ResolvesNowhere() =>
        Assert.Empty(ObservationService.SelectResolutionWindows(0x20, transient: false, []));
}
