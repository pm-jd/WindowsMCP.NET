using System.Text.RegularExpressions;
using WindowsMcpNet.Models;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// ElementLocator.Id must be a deterministic, collision-resistant fingerprint of the locator
/// path so Observe can hand out short stable ids without a live round-trip to the UI tree.
/// </summary>
public class ElementLocatorTests
{
    private static readonly Regex Base32Body = new("^[a-z2-7]+$", RegexOptions.Compiled);

    private static ElementLocator MakeLocator(params LocatorStep[] steps) =>
        new("notepad.exe", "Notepad", steps);

    [Fact]
    public void SameLocator_SameId()
    {
        var a = new ElementLocator("notepad.exe", "Notepad", new List<LocatorStep>
        {
            new("Window", "Notepad", 0),
            new("Button", "Open", 0),
        });
        var b = new ElementLocator("notepad.exe", "Notepad", new List<LocatorStep>
        {
            new("Window", "Notepad", 0),
            new("Button", "Open", 0),
        });

        var idA = a.Id(4);
        var idB = b.Id(4);

        Assert.Equal(idA, idB);
        Assert.StartsWith("e", idA, StringComparison.Ordinal);
        Assert.Equal(5, idA.Length);
    }

    [Fact]
    public void DifferentIndex_DifferentId()
    {
        var first = MakeLocator(new LocatorStep("Button", "Open", 0));
        var second = MakeLocator(new LocatorStep("Button", "Open", 1));

        Assert.NotEqual(first.Id(4), second.Id(4));
    }

    [Fact]
    public void NonAsciiKey_IsDeterministic()
    {
        var schliessen = MakeLocator(new LocatorStep("Button", "Schließen", 0));
        var pua = MakeLocator(new LocatorStep("Button", "", 0));

        var id1 = schliessen.Id(6);
        var id2 = schliessen.Id(6);
        var id3 = pua.Id(6);

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);

        foreach (var id in new[] { id1, id3 })
        {
            Assert.StartsWith("e", id, StringComparison.Ordinal);
            Assert.True(Base32Body.IsMatch(id[1..]), $"'{id}' contains characters outside the base32 alphabet");
        }
    }

    [Fact]
    public void Equality_UsesPathSequence()
    {
        var pathA = new List<LocatorStep> { new("Window", "Notepad", 0), new("Edit", "Text", 0) };
        var pathB = new List<LocatorStep> { new("Window", "Notepad", 0), new("Edit", "Text", 0) };

        var a = new ElementLocator("notepad.exe", "Notepad", pathA);
        var b = new ElementLocator("notepad.exe", "Notepad", pathB);

        Assert.NotSame(pathA, pathB);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Id_WithWindowHandle_DiffersPerWindow_AndIsDeterministic()
    {
        var locator = MakeLocator(new LocatorStep("Button", "Open", 0));

        var first = locator.Id(6, 0x1000);
        var second = locator.Id(6, 0x2000);

        Assert.NotEqual(first, second);
        Assert.NotEqual(locator.Id(6), first);
        Assert.Equal(first, MakeLocator(new LocatorStep("Button", "Open", 0)).Id(6, 0x1000));
        foreach (var id in new[] { first, second })
        {
            Assert.Equal(7, id.Length);
            Assert.StartsWith("e", id, StringComparison.Ordinal);
            Assert.True(Base32Body.IsMatch(id[1..]), $"'{id}' contains characters outside the base32 alphabet");
        }
    }

    [Fact]
    public void Canonical_IsProcessClassAndPath()
    {
        var locator = MakeLocator(new LocatorStep("Pane", "main", 0), new LocatorStep("Button", "Open", 2));

        Assert.Equal("notepad.exe|Notepad|Pane:main:0/Button:Open:2", locator.Canonical());
    }
}
