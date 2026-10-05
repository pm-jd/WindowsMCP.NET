using System.ComponentModel;
using System.Drawing;
using WindowsMcpNet.Services;
using Xunit;

namespace WindowsMcpNet.Tests.Services;

/// <summary>
/// A session without a display (RDP disconnected, or the RDP client minimized) makes the GDI screen copy throw
/// <see cref="Win32Exception"/> ("The handle is invalid"). The windows still render, so
/// <see cref="ScreenCaptureService"/> falls back to compositing them — and says so — instead of failing.
/// </summary>
public class ScreenCaptureFallbackTests
{
    private const int ErrorInvalidHandle = 6;

    [Fact]
    public void CaptureBitmap_UsesTheScreenWhenTheSessionHasADisplay()
    {
        var composed = false;
        var service = new ScreenCaptureService(
            r => new Bitmap(r.Width, r.Height),
            r => { composed = true; return new CompositeCapture(new Bitmap(r.Width, r.Height), 1); });

        using var bitmap = service.CaptureBitmap(new Rectangle(0, 0, 4, 3), out var note);

        Assert.Equal(new Size(4, 3), bitmap.Size);
        Assert.Null(note);
        Assert.False(composed);
    }

    [Fact]
    public void CaptureBitmap_ComposesTheWindowsWhenTheSessionHasNoDisplay()
    {
        var service = new ScreenCaptureService(
            _ => throw new Win32Exception(ErrorInvalidHandle),
            r => new CompositeCapture(new Bitmap(r.Width, r.Height), 3));

        using var bitmap = service.CaptureBitmap(new Rectangle(0, 0, 2560, 1600), out var note);

        Assert.Equal(new Size(2560, 1600), bitmap.Size);
        Assert.NotNull(note);
        Assert.Contains("3 windows", note);
        Assert.Contains("2560x1600", note);
        // The note rides along a successful result; the [ERROR] prefix would make ErrorFlagFilter flag it.
        Assert.DoesNotContain("[ERROR]", note);
    }

    [Fact]
    public void CaptureBitmap_WithoutAnyRenderableWindow_ReportsTheMissingDisplay()
    {
        var service = new ScreenCaptureService(
            _ => throw new Win32Exception(ErrorInvalidHandle),
            r => new CompositeCapture(new Bitmap(r.Width, r.Height), 0));

        var ex = Assert.Throws<InvalidOperationException>(() => service.CaptureBitmap(new Rectangle(0, 0, 8, 8), out _));

        Assert.Contains("no display", ex.Message);
    }

    [Fact]
    public void CaptureBitmap_OtherFailuresAreNotHiddenByTheFallback()
    {
        var composed = false;
        var service = new ScreenCaptureService(
            _ => throw new ArgumentException("bad region"),
            r => { composed = true; return new CompositeCapture(new Bitmap(r.Width, r.Height), 1); });

        Assert.Throws<ArgumentException>(() => service.CaptureBitmap(new Rectangle(0, 0, 8, 8), out _));
        Assert.False(composed);
    }
}
