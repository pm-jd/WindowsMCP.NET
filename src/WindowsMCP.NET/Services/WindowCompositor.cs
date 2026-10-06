using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

/// <summary>A top-level window as EnumWindows reports it, with the properties that decide whether it is painted.
/// <see cref="VisibleBounds"/> is the DWM frame without the invisible resize borders.</summary>
internal readonly record struct WindowCandidate(
    nint Handle,
    Rectangle WindowRect,
    Rectangle VisibleBounds,
    bool Visible,
    bool Minimized,
    bool Cloaked,
    bool Hung,
    bool Layered,
    bool ClickThrough);

/// <summary>A rendered window covering <see cref="Bounds"/> (screen coordinates). An image without an alpha
/// channel paints opaque; a premultiplied-alpha image is blended over what lies below.</summary>
internal sealed record WindowLayer(Rectangle Bounds, Bitmap Image);

/// <summary>A composited screenshot, the number of windows that went into it and the number left out because
/// they did not answer within <see cref="WindowCompositor.RenderLimit"/>. Owns <see cref="Image"/>.</summary>
internal sealed class CompositeCapture(Bitmap image, int windowCount, int unansweredCount = 0) : IDisposable
{
    public Bitmap Image { get; } = image;
    public int WindowCount { get; } = windowCount;
    public int UnansweredCount { get; } = unansweredCount;
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Rebuilds a screenshot from the session's top-level windows when the session has no display.
/// Why: with RDP disconnected (or the RDP client minimized) there is no screen surface, so BitBlt/CopyFromScreen
/// fails with "The handle is invalid". DWM keeps composing the windows themselves, and PrintWindow with
/// PW_RENDERFULLCONTENT reads each one back — on SWENTW3 (2026-10-05) a disconnected session returned the full
/// MCS window including its OpenGL live view this way. Minimized windows are absent, and content that only
/// repaints with a display (the taskbar clock, some XAML chrome) keeps its state from before the disconnect.
/// </summary>
internal static class WindowCompositor
{
    /// <summary>Limit for rendering one window. PrintWindow waits for a window that does not process messages,
    /// and <see cref="User32.IsHungAppWindow"/> reports it only after about 5 s — measured on GuiTestServer
    /// (2026-10-05): a window blocking for 3 s at a time made the screenshot take 3 s. A window that answers
    /// took about 0.25 s there.</summary>
    internal static readonly TimeSpan RenderLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs <paramref name="render"/> on a pool thread and waits at most <paramref name="limit"/> for it (the
    /// way <see cref="PatternCall"/> limits a UIA call). True with the image (null when the window refused)
    /// when it returned in time; an exception it threw in time is rethrown. False when it has not returned:
    /// the call cannot be cancelled, so it is left running and its image is disposed when it arrives.
    /// </summary>
    internal static bool TryRenderWithin(Func<Bitmap?> render, TimeSpan limit, out Bitmap? image)
    {
        var task = Task.Run(render);

        bool returned;
        try
        {
            returned = task.Wait(limit);
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (returned)
        {
            image = task.Result;
            return true;
        }

        _ = task.ContinueWith(
            static t =>
            {
                if (t.IsCompletedSuccessfully)
                    t.Result?.Dispose();
                else
                    _ = t.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        image = null;
        return false;
    }

    /// <summary>The windows to paint, bottom to top (EnumWindows lists the topmost first).</summary>
    internal static List<WindowCandidate> SelectLayers(IEnumerable<WindowCandidate> topToBottom, Rectangle canvas)
    {
        var layers = topToBottom
            .Where(w => w.Visible && !w.Minimized && !w.Cloaked)
            // PrintWindow waits for the window's thread, so one hung application would stall the screenshot.
            .Where(w => !w.Hung)
            // Layered + click-through is an input-transparent overlay (GPU overlays, recording frames, menu
            // shadows); rendered without its blending it would cover the real UI.
            .Where(w => !(w.Layered && w.ClickThrough))
            .Where(w => !w.VisibleBounds.IsEmpty && w.VisibleBounds.IntersectsWith(canvas))
            .ToList();
        layers.Reverse();
        return layers;
    }

    /// <summary>Paints <paramref name="bottomToTop"/> onto a black canvas covering <paramref name="canvas"/>.</summary>
    internal static Bitmap Compose(Rectangle canvas, IReadOnlyList<WindowLayer> bottomToTop)
    {
        var result = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.Clear(Color.Black);
            foreach (var layer in bottomToTop)
            {
                // Explicit source and destination rectangles: DrawImage(image, x, y) would rescale by the
                // bitmap's DPI.
                var destination = new Rectangle(layer.Bounds.X - canvas.X, layer.Bounds.Y - canvas.Y,
                    layer.Bounds.Width, layer.Bounds.Height);
                graphics.DrawImage(layer.Image, destination, new Rectangle(Point.Empty, layer.Image.Size),
                    GraphicsUnit.Pixel);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Renders every paintable window intersecting <paramref name="canvas"/> and composes them.</summary>
    internal static CompositeCapture Capture(Rectangle canvas)
    {
        var rendered = new List<WindowLayer>();
        var unanswered = 0;
        try
        {
            foreach (var window in SelectLayers(EnumerateTopLevelWindows(), canvas))
            {
                if (!TryRenderWithin(() => Render(window), RenderLimit, out var image))
                    unanswered++;
                else if (image is not null)
                    rendered.Add(new WindowLayer(window.VisibleBounds, image));
            }
            return new CompositeCapture(Compose(canvas, rendered), rendered.Count, unanswered);
        }
        finally
        {
            foreach (var layer in rendered)
                layer.Image.Dispose();
        }
    }

    private static List<WindowCandidate> EnumerateTopLevelWindows()
    {
        var windows = new List<WindowCandidate>();
        User32.EnumWindows((hwnd, _) =>
        {
            windows.Add(Describe(hwnd));
            return true;
        }, nint.Zero);
        return windows;
    }

    private static WindowCandidate Describe(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd))
            return new WindowCandidate(hwnd, Rectangle.Empty, Rectangle.Empty, false, false, false, false, false, false);

        User32.GetWindowRect(hwnd, out var rect);
        var windowRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var visibleBounds = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT frame, Marshal.SizeOf<RECT>()) == 0
            ? Rectangle.Intersect(Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom), windowRect)
            : windowRect;
        var cloaked = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, out int cloakReason, sizeof(int)) == 0
            && cloakReason != 0;
        var exStyle = (long)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);

        return new WindowCandidate(hwnd, windowRect, visibleBounds,
            Visible: true,
            Minimized: User32.IsIconic(hwnd),
            Cloaked: cloaked,
            Hung: User32.IsHungAppWindow(hwnd),
            Layered: (exStyle & User32.WS_EX_LAYERED) != 0,
            ClickThrough: (exStyle & User32.WS_EX_TRANSPARENT) != 0);
    }

    /// <summary>PrintWindow into a bitmap the size of the whole window, cropped to the visible frame.
    /// Null when the window refuses (returns false), e.g. one of higher integrity than this process.</summary>
    private static Bitmap? Render(WindowCandidate window)
    {
        var whole = window.WindowRect;
        if (whole.Width <= 0 || whole.Height <= 0)
            return null;

        // Only layered windows carry meaningful per-pixel alpha (menus, tooltips). Every other window is opaque,
        // and an RGB target ignores whatever alpha the DWM redirection surface reports for it.
        var format = window.Layered ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb;
        using var full = new Bitmap(whole.Width, whole.Height, format);
        using (var graphics = Graphics.FromImage(full))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!User32.PrintWindow(window.Handle, hdc, User32.PW_RENDERFULLCONTENT))
                    return null;
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        var crop = new Rectangle(window.VisibleBounds.X - whole.X, window.VisibleBounds.Y - whole.Y,
            window.VisibleBounds.Width, window.VisibleBounds.Height);
        return full.Clone(crop, format);
    }
}
