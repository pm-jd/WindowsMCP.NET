using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

/// <summary>A PNG screenshot. <see cref="Note"/> is set when the session had no display and the image was composed
/// from its windows — the caller should pass that on, because such a picture can lack windows or show stale parts.</summary>
public sealed record ScreenCapture(byte[] Png, string? Note);

public sealed class ScreenCaptureService
{
    /// <summary>ERROR_INVALID_HANDLE: what the GDI screen copy fails with when the session has no display.</summary>
    internal const int ErrorInvalidHandle = 6;

    private readonly Func<Rectangle, Bitmap> _grabScreen;
    private readonly Func<Rectangle, CompositeCapture> _composeWindows;

    public ScreenCaptureService() : this(GrabScreen, WindowCompositor.Capture) { }

    /// <summary>Test seam: the GDI screen copy and the window compositor are the two native dependencies.</summary>
    internal ScreenCaptureService(Func<Rectangle, Bitmap> grabScreen, Func<Rectangle, CompositeCapture> composeWindows)
    {
        _grabScreen = grabScreen;
        _composeWindows = composeWindows;
    }

    /// <summary>Scales <paramref name="source"/> so its longest edge is at most <paramref name="maxEdge"/>,
    /// preserving aspect ratio. Never upscales (a region already within the cap is returned unchanged)
    /// and never rounds a non-zero edge down to 0.</summary>
    public static Size ScaleToFit(Size source, int maxEdge)
    {
        var longestEdge = Math.Max(source.Width, source.Height);
        if (longestEdge <= maxEdge)
            return source;

        var scale = (double)maxEdge / longestEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Size(width, height);
    }

    /// <summary>Captures <paramref name="region"/> of the screen via GDI, downscales it (HighQualityBicubic)
    /// to fit within <paramref name="maxEdge"/> on its longest side, and encodes it as JPEG at
    /// <paramref name="quality"/> (0-100). Used by the Observe tool's optional screenshot.
    /// <paramref name="note"/> is set when the image was composed from the windows, see <see cref="CaptureBitmap"/>.</summary>
    public byte[] CaptureRegionJpeg(Rectangle region, out string? note, int maxEdge = 1568, long quality = 80)
    {
        using var captured = CaptureBitmap(region, out note);

        var targetSize = ScaleToFit(region.Size, maxEdge);
        using var resized = targetSize == region.Size ? null : Resize(captured, targetSize);

        return EncodeJpeg(resized ?? captured, quality);
    }

    private static Bitmap Resize(Bitmap source, Size targetSize)
    {
        var resized = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(resized);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(Point.Empty, targetSize));
            return resized;
        }
        catch
        {
            resized.Dispose();
            throw;
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        using var ms = new MemoryStream();
        bitmap.Save(ms, codec, parameters);
        return ms.ToArray();
    }

    /// <summary>Captures one monitor (or the primary) as PNG. See <see cref="CaptureScreenWithNote"/>.</summary>
    public byte[] CaptureScreen(int? displayIndex = null) => CaptureScreenWithNote(displayIndex).Png;

    /// <summary>Captures one monitor (or the primary) as PNG; in a session without a display the image is composed
    /// from the windows and <see cref="ScreenCapture.Note"/> says so.</summary>
    public ScreenCapture CaptureScreenWithNote(int? displayIndex = null)
    {
        using var bitmap = CaptureBitmap(ResolveBounds(displayIndex), out var note);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return new ScreenCapture(ms.ToArray(), note);
    }

    /// <summary>Copies <paramref name="bounds"/> from the screen. Only the "no display" failure of the GDI copy
    /// (<see cref="Win32Exception"/> with ERROR_INVALID_HANDLE, "The handle is invalid": RDP disconnected or its
    /// client minimized) switches to composing the windows; anything else — other Win32 errors included — still
    /// fails, so real errors are not papered over with a partial picture.
    /// <paramref name="note"/> is null for a normal screen copy. The caller owns the returned bitmap.</summary>
    internal Bitmap CaptureBitmap(Rectangle bounds, out string? note)
    {
        try
        {
            var screen = _grabScreen(bounds);
            note = null;
            return screen;
        }
        catch (Win32Exception noDisplay) when (noDisplay.NativeErrorCode == ErrorInvalidHandle)
        {
            var composite = _composeWindows(bounds);
            if (composite.WindowCount == 0)
            {
                composite.Dispose();
                throw new InvalidOperationException(
                    "The session has no display (RDP disconnected or its window minimized) and no window could be " +
                    $"rendered instead: {noDisplay.Message}", noDisplay);
            }

            note = CompositedNote(composite.WindowCount, bounds.Size, composite.UnansweredCount);
            return composite.Image;
        }
    }

    internal static string CompositedNote(int windowCount, Size size, int unansweredCount = 0) =>
        "Composited screenshot: this session has no display (RDP disconnected or the RDP window minimized), so " +
        $"the image was assembled from {windowCount} windows rendered one by one. Minimized windows are missing, and " +
        "parts that do not repaint without a display (e.g. the taskbar clock) can show an older state; the window " +
        $"positions are those of the session's current screen, of which this image covers {size.Width}x{size.Height}." +
        (unansweredCount == 0
            ? ""
            : $" {unansweredCount} more window(s) did not answer within " +
              $"{WindowCompositor.RenderLimit.TotalSeconds:0} s (busy application) and are missing too.");

    private static Bitmap GrabScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static Rectangle ResolveBounds(int? displayIndex)
    {
        var monitors = EnumerateMonitors();

        Rectangle bounds;
        if (displayIndex.HasValue && displayIndex.Value < monitors.Count)
        {
            bounds = monitors[displayIndex.Value];
        }
        else
        {
            // Primary screen — the monitor that contains (0,0)
            bounds = monitors.FirstOrDefault(m => m.Contains(0, 0));
            if (bounds.IsEmpty)
            {
                // Fall back to virtual screen if no monitor contains origin
                int x = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
                int y = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
                int w = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
                int h = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);
                if (w == 0 || h == 0)
                    throw new InvalidOperationException("No screen found.");
                bounds = new Rectangle(x, y, w, h);
            }
        }

        return bounds;
    }

    private static List<Rectangle> EnumerateMonitors()
    {
        var monitors = new List<Rectangle>();
        User32.EnumDisplayMonitors(nint.Zero, nint.Zero, (_, _, ref rect, _) =>
        {
            monitors.Add(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, nint.Zero);
        return monitors;
    }

    public byte[] AnnotateScreenshot(byte[] pngBytes, IReadOnlyList<(int X, int Y, string Label)> annotations)
    {
        using var ms = new MemoryStream(pngBytes);
        using var bitmap = new Bitmap(ms);
        using var graphics = Graphics.FromImage(bitmap);

        using var font = new Font("Arial", 10, FontStyle.Bold);
        using var bgBrush = new SolidBrush(Color.FromArgb(200, Color.Red));
        using var textBrush = new SolidBrush(Color.White);

        foreach (var (x, y, label) in annotations)
        {
            var textSize = graphics.MeasureString(label, font);
            var rect = new RectangleF(x - 2, y - textSize.Height - 2, textSize.Width + 4, textSize.Height + 2);
            graphics.FillRectangle(bgBrush, rect);
            graphics.DrawString(label, font, textBrush, x, y - textSize.Height - 1);
        }

        using var outMs = new MemoryStream();
        bitmap.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }
}
