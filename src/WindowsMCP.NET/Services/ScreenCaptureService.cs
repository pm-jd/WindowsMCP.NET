using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

public sealed class ScreenCaptureService
{
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
    /// <paramref name="quality"/> (0-100). Used by the Observe tool's optional screenshot.</summary>
    public byte[] CaptureRegionJpeg(Rectangle region, int maxEdge = 1568, long quality = 80)
    {
        using var captured = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(captured))
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);

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

    /// <summary>Captures one monitor (or the primary) as PNG via GDI+.</summary>
    public byte[] CaptureScreen(int? displayIndex = null)
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

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(new Point(bounds.X, bounds.Y), Point.Empty, new Size(bounds.Width, bounds.Height));

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
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
