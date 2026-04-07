using System.Runtime.InteropServices;
using GdiColor   = System.Drawing.Color;
using GdiBitmap  = System.Drawing.Bitmap;
using GdiGraphics= System.Drawing.Graphics;
using GdiPen     = System.Drawing.Pen;
using GdiBrush   = System.Drawing.SolidBrush;
using GdiIcon    = System.Drawing.Icon;
using GdiLineCap = System.Drawing.Drawing2D.LineCap;
using SmoothingMode    = System.Drawing.Drawing2D.SmoothingMode;
using CompositingMode  = System.Drawing.Drawing2D.CompositingMode;
using PixelFormat      = System.Drawing.Imaging.PixelFormat;

namespace PowerMate.WinUI;

/// <summary>
/// Renders the PowerMate tray icon: a circular volume arc with mute indicator,
/// matching the visual style of the original Python version.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static IntPtr _lastHIcon = IntPtr.Zero;

    /// <summary>
    /// Creates a 64×64 tray icon bitmap:
    ///   • Gray ring (dim when muted, light when active)
    ///   • Blue arc sweeping clockwise from 12 o'clock showing volume %
    ///   • White center dot (dark + red X when muted)
    /// </summary>
    public static GdiIcon Render(float volume, bool muted)
    {
        const int size = 64;
        using var bmp = new GdiBitmap(size, size, PixelFormat.Format32bppArgb);
        using var g   = GdiGraphics.FromImage(bmp);

        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.Clear(GdiColor.Transparent);

        // ── Outer ring ────────────────────────────────────────────────────────
        var ringColor = muted
            ? GdiColor.FromArgb(255, 80,  80,  80)
            : GdiColor.FromArgb(255, 200, 200, 200);
        using var ringPen = new GdiPen(ringColor, 4f);
        g.DrawEllipse(ringPen, 2, 2, size - 4, size - 4);

        // ── Volume arc (blue, clockwise from top) ─────────────────────────────
        if (!muted && volume > 0.001f)
        {
            using var arcPen = new GdiPen(GdiColor.FromArgb(255, 100, 200, 255), 6f);
            arcPen.StartCap = GdiLineCap.Round;
            arcPen.EndCap   = GdiLineCap.Round;
            g.DrawArc(arcPen, 6, 6, size - 12, size - 12, -90f, volume * 360f);
        }

        // ── Center dot ────────────────────────────────────────────────────────
        int cx = size / 2, cy = size / 2, r = 8;
        var dotColor = muted
            ? GdiColor.FromArgb(200, 60,  60,  60)
            : GdiColor.FromArgb(230, 255, 255, 255);
        using var dotBrush = new GdiBrush(dotColor);
        g.FillEllipse(dotBrush, cx - r, cy - r, r * 2, r * 2);

        // ── Mute X ────────────────────────────────────────────────────────────
        if (muted)
        {
            using var xPen = new GdiPen(GdiColor.FromArgb(255, 220, 60, 60), 3f);
            xPen.StartCap = GdiLineCap.Round;
            xPen.EndCap   = GdiLineCap.Round;
            g.DrawLine(xPen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(xPen, cx + 5, cy - 5, cx - 5, cy + 5);
        }

        // ── Convert bitmap → HICON (track handle to avoid GDI leaks) ─────────
        var hIcon = bmp.GetHicon();
        if (_lastHIcon != IntPtr.Zero) DestroyIcon(_lastHIcon);
        _lastHIcon = hIcon;

        return GdiIcon.FromHandle(hIcon);
    }
}
