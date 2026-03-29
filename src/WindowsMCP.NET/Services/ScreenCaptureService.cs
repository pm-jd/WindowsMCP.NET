using System.Drawing;
using System.Drawing.Imaging;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

public sealed class ScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public byte[] CaptureScreen(int? displayIndex = null)
    {
        try
        {
            return CaptureWithDxgi(displayIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DXGI capture failed, falling back to GDI+");
            return CaptureWithGdi(displayIndex);
        }
    }

    private byte[] CaptureWithGdi(int? displayIndex)
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

    private byte[] CaptureWithDxgi(int? displayIndex)
    {
        // DXGI Desktop Duplication will be implemented as a future enhancement.
        // For now, delegate to GDI+ so the tool chain works end-to-end.
        _logger.LogDebug("DXGI not yet implemented, using GDI+");
        return CaptureWithGdi(displayIndex);
    }

    public byte[] AnnotateScreenshot(byte[] pngBytes, IReadOnlyList<(int X, int Y, string Label)> annotations)
    {
        using var ms = new MemoryStream(pngBytes);
        using var bitmap = new Bitmap(ms);
        using var graphics = Graphics.FromImage(bitmap);

        var font = new Font("Arial", 10, FontStyle.Bold);
        var bgBrush = new SolidBrush(Color.FromArgb(200, Color.Red));
        var textBrush = new SolidBrush(Color.White);

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
