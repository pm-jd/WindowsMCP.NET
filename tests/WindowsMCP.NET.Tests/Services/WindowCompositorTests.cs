using System.Drawing;
using System.Drawing.Imaging;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// <see cref="WindowCompositor"/> rebuilds a screenshot from the session's top-level windows when the session has
/// no display to copy from (RDP disconnected or minimized). These tests cover the two pure halves: which windows
/// are painted and in what order, and how the rendered windows are painted onto the canvas.
/// </summary>
public class WindowCompositorTests
{
    private static readonly Rectangle Canvas = new(0, 0, 100, 80);

    private static WindowCandidate Window(nint handle, Rectangle bounds, bool visible = true, bool minimized = false,
        bool cloaked = false, bool hung = false, bool layered = false, bool clickThrough = false) =>
        new(handle, bounds, bounds, visible, minimized, cloaked, hung, layered, clickThrough);

    [Fact]
    public void SelectLayers_ReturnsBottomToTop()
    {
        // EnumWindows reports the topmost window first; painting must start with the bottom one.
        var topToBottom = new[]
        {
            Window(1, new Rectangle(10, 10, 20, 20)),
            Window(2, new Rectangle(20, 20, 20, 20)),
            Window(3, new Rectangle(0, 0, 100, 80)),
        };

        var layers = WindowCompositor.SelectLayers(topToBottom, Canvas);

        Assert.Equal(new nint[] { 3, 2, 1 }, layers.Select(l => l.Handle));
    }

    [Fact]
    public void SelectLayers_SkipsWindowsThatShowNothing()
    {
        var bounds = new Rectangle(10, 10, 20, 20);
        var topToBottom = new[]
        {
            Window(1, bounds, visible: false),
            Window(2, bounds, minimized: true),
            Window(3, bounds, cloaked: true),
            Window(4, bounds),
        };

        var layers = WindowCompositor.SelectLayers(topToBottom, Canvas);

        Assert.Equal(new nint[] { 4 }, layers.Select(l => l.Handle));
    }

    [Fact]
    public void SelectLayers_SkipsHungWindows()
    {
        // PrintWindow waits for the window's thread; a hung window would stall the whole screenshot.
        var layers = WindowCompositor.SelectLayers(
            new[] { Window(1, new Rectangle(0, 0, 50, 50), hung: true), Window(2, new Rectangle(0, 0, 50, 50)) },
            Canvas);

        Assert.Equal(new nint[] { 2 }, layers.Select(l => l.Handle));
    }

    [Fact]
    public void SelectLayers_SkipsClickThroughOverlaysButKeepsOtherLayeredWindows()
    {
        // Layered + click-through = an input-transparent overlay (GPU overlays, recording frames). Painted
        // opaque it would cover the real UI; a layered window that takes input (menu, tooltip) is real content.
        var bounds = new Rectangle(0, 0, 100, 80);
        var layers = WindowCompositor.SelectLayers(
            new[] { Window(1, bounds, layered: true, clickThrough: true), Window(2, bounds, layered: true) },
            Canvas);

        Assert.Equal(new nint[] { 2 }, layers.Select(l => l.Handle));
    }

    [Fact]
    public void SelectLayers_SkipsEmptyAndOffCanvasWindows()
    {
        var layers = WindowCompositor.SelectLayers(
            new[]
            {
                Window(1, Rectangle.Empty),
                Window(2, new Rectangle(500, 500, 40, 40)),
                Window(3, new Rectangle(90, 70, 40, 40)),   // partly on the canvas
            },
            Canvas);

        Assert.Equal(new nint[] { 3 }, layers.Select(l => l.Handle));
    }

    [Fact]
    public void Compose_UpperWindowCoversLowerAndUncoveredAreaIsBlack()
    {
        using var lower = Filled(15, 15, Color.Red, PixelFormat.Format32bppRgb);
        using var upper = Filled(10, 10, Color.Blue, PixelFormat.Format32bppRgb);
        var canvas = new Rectangle(100, 100, 20, 20);

        using var result = WindowCompositor.Compose(canvas, new[]
        {
            new WindowLayer(new Rectangle(100, 100, 15, 15), lower),
            new WindowLayer(new Rectangle(110, 110, 10, 10), upper),
        });

        Assert.Equal(new Size(20, 20), result.Size);
        AssertRgb(Color.Red, result.GetPixel(0, 0));
        AssertRgb(Color.Blue, result.GetPixel(12, 12));   // overlap: the upper window wins
        AssertRgb(Color.Black, result.GetPixel(19, 0));   // covered by no window
        Assert.Equal(255, result.GetPixel(19, 0).A);      // opaque background, not a transparent hole
    }

    [Fact]
    public void Compose_PlacesWindowsRelativeToACanvasLeftOfThePrimaryMonitor()
    {
        using var window = Filled(5, 5, Color.Lime, PixelFormat.Format32bppRgb);
        var canvas = new Rectangle(-1920, 0, 1920, 1080);

        using var result = WindowCompositor.Compose(canvas,
            new[] { new WindowLayer(new Rectangle(-1910, 20, 5, 5), window) });

        AssertRgb(Color.Lime, result.GetPixel(10, 20));
        AssertRgb(Color.Black, result.GetPixel(9, 20));
    }

    [Fact]
    public void Compose_TransparentPixelsOfALayeredWindowShowTheWindowBelow()
    {
        using var below = Filled(10, 10, Color.Green, PixelFormat.Format32bppRgb);
        using var overlay = Filled(10, 10, Color.FromArgb(0, 255, 0, 0), PixelFormat.Format32bppPArgb);

        using var result = WindowCompositor.Compose(new Rectangle(0, 0, 10, 10), new[]
        {
            new WindowLayer(new Rectangle(0, 0, 10, 10), below),
            new WindowLayer(new Rectangle(0, 0, 10, 10), overlay),
        });

        AssertRgb(Color.Green, result.GetPixel(5, 5));
    }

    [Fact]
    public void TryRenderWithin_ReturnsTheImageOfAWindowThatAnswers()
    {
        using var expected = new Bitmap(2, 2);

        var returned = WindowCompositor.TryRenderWithin(() => expected, TimeSpan.FromSeconds(30), out var image);

        Assert.True(returned);
        Assert.Same(expected, image);
    }

    [Fact]
    public void TryRenderWithin_AWindowThatRefuses_ReturnedWithoutAnImage()
    {
        // PrintWindow returned false: that is an answer, not a window that is still busy.
        var returned = WindowCompositor.TryRenderWithin(() => null, TimeSpan.FromSeconds(30), out var image);

        Assert.True(returned);
        Assert.Null(image);
    }

    [Fact]
    public void TryRenderWithin_AWindowThatDoesNotAnswer_IsGivenUp_AndItsLateImageIsDisposed()
    {
        // PrintWindow waits for a window that does not process messages; the call cannot be cancelled.
        using var release = new ManualResetEventSlim();
        var late = new Bitmap(2, 2);

        var returned = WindowCompositor.TryRenderWithin(
            () => { release.Wait(); return late; }, TimeSpan.FromMilliseconds(50), out var image);

        Assert.False(returned);
        Assert.Null(image);

        release.Set();
        Assert.True(SpinWait.SpinUntil(() => IsDisposed(late), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void TryRenderWithin_AFailureInTime_IsNotSwallowed()
    {
        Assert.Throws<InvalidOperationException>(() => WindowCompositor.TryRenderWithin(
            () => throw new InvalidOperationException("render failed"), TimeSpan.FromSeconds(30), out _));
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try
        {
            _ = bitmap.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static Bitmap Filled(int width, int height, Color color, PixelFormat format)
    {
        var bitmap = new Bitmap(width, height, format);
        using var g = Graphics.FromImage(bitmap);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, width, height);
        return bitmap;
    }

    private static void AssertRgb(Color expected, Color actual) =>
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
}
