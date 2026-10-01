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

    // --- collected value (F5): a password field's value never leaves the collector ---------------------

    [Theory]
    [InlineData(false, "90,000", "90,000")]
    [InlineData(false, "", null)]
    [InlineData(false, null, null)]
    [InlineData(true, "hunter2", null)]
    [InlineData(true, "", null)]
    public void CollectedValue_IsNullForPasswordFields_AndForEmptyValues(bool isPassword, string? raw, string? expected) =>
        Assert.Equal(expected, ObservationService.CollectedValue(isPassword, raw));

    // --- hit-test (BF1): what is drawn in another top-level window is not visible in the main window ---
    // WinForms lists an open drop-down's items (and an owned dialog's controls) under the main window
    // too. Those copies resolve in the main window, where a click point is "covered" — so an action on
    // them falls back to Invoke, the call that blocks. Only the copy in the popup's own window stays.

    [Theory]
    [InlineData(0x30)]   // a popup, menu or dialog of the same application — or another application
    [InlineData(0)]      // no window at the node's centre
    public void ClassifyHit_CentreInAnotherTopLevelWindow_IsInOtherWindow_AndUiaIsNotAsked(int topLevelAtCentre)
    {
        var asked = false;

        var hit = ObservationService.ClassifyHit(topLevelAtCentre, nodeWindow: 0x20, () => asked = true);

        // Not merely "hidden": the reason travels to the builder, where a tab item whose UIA hit-test
        // missed is kept — one that is drawn in another window is not.
        Assert.Equal(ObservationService.NodeHit.InOtherWindow, hit);
        Assert.False(asked); // no cross-process FromPoint for a node that cannot be clicked in its window
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassifyHit_CentreInTheNodesOwnWindow_IsDecidedByTheUiaHitTest(bool uiaHit)
    {
        var asked = 0;

        var hit = ObservationService.ClassifyHit(topLevelAtCentre: 0x20, nodeWindow: 0x20, () => { asked++; return uiaHit; });

        Assert.Equal(uiaHit ? ObservationService.NodeHit.Visible : ObservationService.NodeHit.Hidden, hit);
        Assert.Equal(1, asked);
    }

    // --- unreadable windows (AF2): a window that does not answer UIA is reported, at one attempt per process ---

    private sealed class Root;

    /// <summary>Drives <see cref="ObservationService.ReadWindow{TRoot}"/> like the collector does and
    /// records which windows UIA was actually asked for.</summary>
    private sealed class Reader(Func<nint, bool> answers, Func<nint, bool>? stillVisible = null)
    {
        private readonly HashSet<uint> _unreadablePids = [];

        public List<nint> Asked { get; } = [];

        public ObservationService.WindowRead Read(nint handle, uint pid, bool budgetSpent = false) =>
            ObservationService.ReadWindow(handle, pid, _unreadablePids, budgetSpent,
                h => { Asked.Add(h); return answers(h) ? new Root() : null; },
                stillVisible ?? (_ => true), out _);
    }

    [Fact]
    public void ReadWindow_WindowThatAnswers_IsRead()
    {
        var root = new Root();

        var result = ObservationService.ReadWindow(0x10, pid: 1, [], budgetSpent: false, _ => root, _ => true, out var read);

        Assert.Equal(ObservationService.WindowRead.Read, result);
        Assert.Same(root, read);
    }

    [Fact]
    public void ReadWindow_WindowThatDoesNotAnswer_IsUnreadable_NotSkipped()
    {
        var result = ObservationService.ReadWindow<Root>(0x10, pid: 1, [], budgetSpent: false, _ => null, _ => true, out var read);

        Assert.Equal(ObservationService.WindowRead.Unreadable, result);
        Assert.Null(read);
    }

    [Fact]
    public void ReadWindow_AfterTheFirstUnreadableWindowOfAProcess_ItsOtherWindowsAreNotAskedAgain()
    {
        // A blocked UI thread lets every request run into the timeout: one timeout per process, not per window.
        var reader = new Reader(answers: _ => false);

        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x11, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x12, pid: 1));

        Assert.Equal([(nint)0x10], reader.Asked);
    }

    [Fact]
    public void ReadWindow_UnreadableProcess_DoesNotAffectTheWindowsOfOtherProcesses()
    {
        var reader = new Reader(answers: h => h != 0x10);

        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Read, reader.Read(0x20, pid: 2));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x11, pid: 1));

        Assert.Equal([(nint)0x10, (nint)0x20], reader.Asked);
    }

    [Fact]
    public void ReadWindow_WindowsReadBeforeTheFirstFailure_StayRead()
    {
        var reader = new Reader(answers: h => h == 0x10);

        Assert.Equal(ObservationService.WindowRead.Read, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x11, pid: 1));
    }

    [Fact]
    public void ReadWindow_WindowThatDisappearedMeanwhile_IsGone_AndSaysNothingAboutItsProcess()
    {
        // A menu or tooltip that closed between the window enumeration and the read: not "unreadable",
        // and no reason to stop asking the other windows of that process.
        var reader = new Reader(answers: h => h != 0x10, stillVisible: h => h != 0x10);

        Assert.Equal(ObservationService.WindowRead.Gone, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Read, reader.Read(0x11, pid: 1));

        Assert.Equal([(nint)0x10, (nint)0x11], reader.Asked);
    }

    // --- time budget (BF4): it limits UIA walks, not the cheap record of windows known to be unreadable ---

    [Fact]
    public void ReadWindow_BudgetSpent_WindowsNotYetRead_AreOutOfBudget_AndNotAsked()
    {
        var reader = new Reader(answers: _ => true);

        Assert.Equal(ObservationService.WindowRead.Read, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.OutOfBudget, reader.Read(0x11, pid: 1, budgetSpent: true));
        Assert.Equal(ObservationService.WindowRead.OutOfBudget, reader.Read(0x20, pid: 2, budgetSpent: true));

        Assert.Equal([(nint)0x10], reader.Asked);
    }

    [Fact]
    public void ReadWindow_BudgetSpent_WindowsOfAProcessKnownToBeUnreadable_AreStillRecorded()
    {
        // The timeout of the first window may have eaten the budget; the other windows of that process
        // cost only Win32 calls and must not vanish from the observation.
        var reader = new Reader(answers: h => h == 0x20);

        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x10, pid: 1));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x11, pid: 1, budgetSpent: true));
        Assert.Equal(ObservationService.WindowRead.OutOfBudget, reader.Read(0x20, pid: 2, budgetSpent: true));
        Assert.Equal(ObservationService.WindowRead.Unreadable, reader.Read(0x12, pid: 1, budgetSpent: true));

        Assert.Equal([(nint)0x10], reader.Asked);
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
        // The application was restarted: its main window has a new handle. The id is looked for in the
        // new window — whether it is found there depends on the application exposing real AutomationIds
        // or names (WinForms reports the window handle as AutomationId, which changes with every start).
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
