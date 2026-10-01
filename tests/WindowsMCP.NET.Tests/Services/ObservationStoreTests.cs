using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ObservationStore"/> keeps id → <see cref="StoredElement"/> (locator plus the window it
/// was seen in) for the last 8 observations. Synthetic <see cref="Observation"/>s are built through
/// <see cref="ObservationBuilder.Build"/> (as the spec requires) so every id is a real,
/// deterministically-hashed <see cref="ElementLocator.Id(int)"/> rather than a hand-picked string.
/// </summary>
public class ObservationStoreTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 1, HitMs: 1, TotalMs: 2);

    /// <summary>Builds a one-window, one-actionable-element observation; <paramref name="elementName"/>
    /// controls the element's (and therefore the id's) identity so tests can make two observations
    /// share — or not share — the same id.</summary>
    private static Observation BuildObservation(string elementName, nint handle = 1)
    {
        var window = new ObservedWindow(handle, "Main", "MainWin", "app", 100, true, false, new Rectangle(0, 0, 800, 600));
        var nodes = new[]
        {
            new ObservedNode(0, null, 0, 0, "Window", "Main", "", new Rectangle(0, 0, 800, 600), true, false, null, null, false, null, null),
            new ObservedNode(1, 0, 1, 0, "Button", elementName, "", new Rectangle(10, 10, 20, 20), true, false, null, null, false, null, true),
        };

        return ObservationBuilder.Build([window], nodes, maxElements: 10, Timings, budgetExceeded: false);
    }

    [Fact]
    public void Get_KnownId_ReturnsLocatorWindowHandleAndTransient()
    {
        var store = new ObservationStore();
        var observation = BuildObservation("Target", handle: 0x4711);
        var element = Assert.Single(observation.Elements);

        store.Remember(observation);

        Assert.Equal(new StoredElement(element.Locator, 0x4711, Transient: false), store.Get(element.Id));
    }

    [Fact]
    public void Get_TransientElement_IsStoredAsTransient()
    {
        var popup = new ObservedWindow(0x11, "Menu", "Popup", "app", 100, true, false, new Rectangle(0, 0, 100, 100));
        var main = new ObservedWindow(0x22, "Main", "MainWin", "app", 100, false, false, new Rectangle(0, 0, 800, 600));
        var nodes = new[]
        {
            new ObservedNode(0, null, 0, 0, "Window", "Menu", "", new Rectangle(0, 0, 100, 100), true, false, null, null, false, null, null),
            new ObservedNode(1, 0, 1, 0, "MenuItem", "Save", "", new Rectangle(10, 10, 20, 20), true, false, null, null, false, null, null),
            new ObservedNode(2, null, 0, 1, "Window", "Main", "", new Rectangle(0, 0, 800, 600), true, false, null, null, false, null, null),
        };
        var observation = ObservationBuilder.Build([popup, main], nodes, maxElements: 10, Timings, budgetExceeded: false);
        var element = Assert.Single(observation.Elements);
        var store = new ObservationStore();

        store.Remember(observation);

        var stored = store.Get(element.Id);
        Assert.Equal((nint)0x11, stored.WindowHandle);
        Assert.True(stored.Transient);
    }

    [Fact]
    public void Get_UnknownId_Throws_WithCallObserveMessage()
    {
        var store = new ObservationStore();

        var ex = Assert.Throws<ElementNotFoundException>(() => store.Get("e0000"));

        Assert.Equal("element e0000 no longer present — call Observe", ex.Message);
    }

    [Fact]
    public void Evicts_AfterEightObservations()
    {
        var store = new ObservationStore();
        var first = BuildObservation("Target");
        var id = Assert.Single(first.Elements).Id;
        store.Remember(first);

        for (var i = 0; i < 8; i++)
            store.Remember(BuildObservation($"Filler{i}"));

        Assert.Throws<ElementNotFoundException>(() => store.Get(id));
    }

    [Fact]
    public void ReSeenId_SurvivesEviction()
    {
        var store = new ObservationStore();
        var first = BuildObservation("Target");
        var element = Assert.Single(first.Elements);
        store.Remember(first);

        for (var i = 0; i < 7; i++)
            store.Remember(BuildObservation($"Filler{i}"));
        store.Remember(BuildObservation("Target"));

        Assert.Equal(element.Locator, store.Get(element.Id).Locator);
    }

    [Fact]
    public void ReSeenId_TakesTheWindowOfTheNewestObservation()
    {
        var store = new ObservationStore();
        var first = BuildObservation("Target", handle: 0x10);
        var id = Assert.Single(first.Elements).Id;
        store.Remember(first);

        store.Remember(BuildObservation("Target", handle: 0x20));

        Assert.Equal((nint)0x20, store.Get(id).WindowHandle);
    }
}
