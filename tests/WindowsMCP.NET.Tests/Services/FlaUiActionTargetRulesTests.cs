using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// The pure rule behind <see cref="FlaUiActionTarget.OwnsPoint"/>. The Win32 calls that feed it
/// (<c>WindowFromPoint</c>, <c>GetAncestor</c>) need a desktop; the decision does not.
/// </summary>
public class FlaUiActionTargetRulesTests
{
    [Theory]
    [InlineData(0x20, 0x20, true)]    // the element's own window is what a click there would hit
    [InlineData(0x30, 0x20, false)]   // another application — or a popup, menu or dialog of the same one — lies on top
    [InlineData(0, 0x20, false)]      // no window at that point (off screen)
    [InlineData(0, 0, false)]         // nothing known about the element's window: "no window" must not match it
    public void IsResolvedWindow_OnlyTheWindowTheElementWasResolvedIn(int topLevelAtPoint, int resolvedWindow, bool expected) =>
        Assert.Equal(expected, FlaUiActionTarget.IsResolvedWindow(topLevelAtPoint, resolvedWindow));
}
