using System.Runtime.InteropServices;
using PowerMate.Services;
using GdiColor    = System.Drawing.Color;
using GdiBitmap   = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiPen      = System.Drawing.Pen;
using GdiBrush    = System.Drawing.SolidBrush;
using GdiIcon     = System.Drawing.Icon;
using GdiPoint    = System.Drawing.PointF;
using GdiLineCap  = System.Drawing.Drawing2D.LineCap;
using SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode;
using CompositingMode = System.Drawing.Drawing2D.CompositingMode;
using PixelFormat     = System.Drawing.Imaging.PixelFormat;

namespace PowerMate.WinUI;

/// <summary>
/// Renders the PowerMate tray icon.
///
/// Outer ring:
///   • Bright white when user is interacting, gray when idle, dark when muted
///   • Volume (or track-position during FF/RW) arc sweeps CW from 12 o'clock
///
/// Inner symbol (center):
///   • Play  ▶  — filled triangle
///   • Pause ⏸  — two vertical bars
///   • Stop  ⏹  — filled square
///   • Unknown  — small filled circle
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static IntPtr _lastHIcon = IntPtr.Zero;

    /// <param name="displayValue">Volume (0–1) normally; track position (0–1) during FF/RW.</param>
    /// <param name="muted">Whether output is currently muted.</param>
    /// <param name="playbackState">Current media playback state for the inner symbol.</param>
    /// <param name="interacting">True while the user is actively using the knob.</param>
    public static GdiIcon Render(float displayValue, bool muted,
        PlaybackState playbackState = PlaybackState.Unknown,
        bool interacting = false)
    {
        const int size = 64;
        using var bmp = new GdiBitmap(size, size, PixelFormat.Format32bppArgb);
        using var g   = GdiGraphics.FromImage(bmp);

        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.Clear(GdiColor.Transparent);

        // ── Outer ring ────────────────────────────────────────────────────────
        var ringColor = muted
            ? GdiColor.FromArgb(255,  80,  80,  80)  // dark – muted
            : interacting
                ? GdiColor.FromArgb(255, 255, 255, 255) // bright white – active
                : GdiColor.FromArgb(255, 160, 160, 160); // gray – idle

        using var ringPen = new GdiPen(ringColor, 4f);
        g.DrawEllipse(ringPen, 2, 2, size - 4, size - 4);

        // ── Display arc (volume or track position, blue, CW from top) ─────────
        if (!muted && displayValue > 0.001f)
        {
            using var arcPen = new GdiPen(GdiColor.FromArgb(255, 100, 200, 255), 6f);
            arcPen.StartCap = GdiLineCap.Round;
            arcPen.EndCap   = GdiLineCap.Round;
            g.DrawArc(arcPen, 6, 6, size - 12, size - 12, -90f, displayValue * 360f);
        }

        // ── Inner playback symbol ─────────────────────────────────────────────
        int cx = size / 2, cy = size / 2;

        var symColor = muted
            ? GdiColor.FromArgb(180, 80,  80,  80)
            : GdiColor.FromArgb(230, 255, 255, 255);

        switch (playbackState)
        {
            case PlaybackState.Playing:  DrawPlay(g, cx, cy, symColor);     break;
            case PlaybackState.Paused:   DrawPause(g, cx, cy, symColor);    break;
            case PlaybackState.Stopped:  DrawStop(g, cx, cy, symColor);     break;
            case PlaybackState.SkipNext: DrawSkipNext(g, cx, cy, symColor); break;
            case PlaybackState.SkipPrev: DrawSkipPrev(g, cx, cy, symColor); break;
            default:                     DrawDot(g, cx, cy, symColor);      break;
        }

        // ── Convert bitmap → HICON (track to avoid GDI leaks) ────────────────
        var hIcon = bmp.GetHicon();
        if (_lastHIcon != IntPtr.Zero) DestroyIcon(_lastHIcon);
        _lastHIcon = hIcon;

        return GdiIcon.FromHandle(hIcon);
    }

    // ── Inner symbol helpers ──────────────────────────────────────────────────

    private static void DrawPlay(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        // Right-pointing triangle inscribed in ~r=10 circle
        var points = new GdiPoint[]
        {
            new(cx - 7f, cy - 10f),
            new(cx - 7f, cy + 10f),
            new(cx + 11f, cy),
        };
        using var brush = new GdiBrush(color);
        g.FillPolygon(brush, points);
    }

    private static void DrawPause(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        using var brush = new GdiBrush(color);
        // Two vertical bars: 5 px wide, 18 px tall, 6 px apart
        g.FillRectangle(brush, cx - 9f, cy - 9f, 5f, 18f);
        g.FillRectangle(brush, cx + 4f, cy - 9f, 5f, 18f);
    }

    private static void DrawStop(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        using var brush = new GdiBrush(color);
        // 18×18 square centered
        g.FillRectangle(brush, cx - 9f, cy - 9f, 18f, 18f);
    }

    private static void DrawSkipNext(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        using var brush = new GdiBrush(color);
        // Smaller right-pointing triangle offset left to make room for the bar
        var tri = new GdiPoint[] { new(cx - 9f, cy - 9f), new(cx - 9f, cy + 9f), new(cx + 3f, cy) };
        g.FillPolygon(brush, tri);
        // Vertical bar on the right
        g.FillRectangle(brush, cx + 5f, cy - 9f, 5f, 18f);
    }

    private static void DrawSkipPrev(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        using var brush = new GdiBrush(color);
        // Vertical bar on the left
        g.FillRectangle(brush, cx - 10f, cy - 9f, 5f, 18f);
        // Smaller left-pointing triangle offset right
        var tri = new GdiPoint[] { new(cx + 9f, cy - 9f), new(cx + 9f, cy + 9f), new(cx - 3f, cy) };
        g.FillPolygon(brush, tri);
    }

    private static void DrawDot(GdiGraphics g, int cx, int cy, GdiColor color)
    {
        const int r = 7;
        using var brush = new GdiBrush(color);
        g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
    }
}
