using System.Drawing;
using WindowsMcpNet.Models;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="ObservationStore"/> keeps id → locator for the last 8 observations. Synthetic
/// <see cref="Observation"/>s are built through <see cref="ObservationBuilder.Build"/> (as the spec
/// requires) so every id is a real, deterministically-hashed <see cref="ElementLocator.Id"/> rather
/// than a hand-picked string.
/// </summary>
public class ObservationStoreTests
{
    private static readonly ObservationTimings Timings = new(WalkMs: 1, HitMs: 1, TotalMs: 2);

    /// <summary>Builds a one-window, one-actionable-element observation; <paramref name="elementName"/>
    /// controls the element's (and therefore the id's) identity so tests can make two observations
    /// share — or not share — the same id.</summary>
    private static Observation BuildObservation(string elementName)
    {
        var window = new ObservedWindow(1, "Main", "MainWin", "app", 100, true, false, new Rectangle(0, 0, 800, 600));
        var nodes = new[]
        {
            new ObservedNode(0, null, 0, 0, "Window", "Main", "", new Rectangle(0, 0, 800, 600), true, false, null, null, false, null, null),
            new ObservedNode(1, 0, 1, 0, "Button", elementName, "", new Rectangle(10, 10, 20, 20), true, false, null, null, false, null, true),
        };

        return ObservationBuilder.Build([window], nodes, maxElements: 10, Timings, budgetExceeded: false);
    }

    [Fact]
    public void Get_KnownId_ReturnsLocator()
    {
        var store = new ObservationStore();
        var observation = BuildObservation("Target");
        var element = Assert.Single(observation.Elements);

        store.Remember(observation);

        Assert.Equal(element.Locator, store.Get(element.Id));
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

        Assert.Equal(element.Locator, store.Get(element.Id));
    }
}
