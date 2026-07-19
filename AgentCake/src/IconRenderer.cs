using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AgentCake;

/// <summary>
/// Builds the tray glyph at runtime: two vertical fill bars — left = current 5-hour window,
/// right = this week. The notification area only shows an icon (not text), so we draw the bars
/// into a bitmap and convert to an HICON each refresh (the trick battery/CPU meters use).
/// </summary>
public static class IconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Color Green = Color.FromArgb(0x39, 0xD3, 0x53);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0xB8, 0x2E);
    private static readonly Color Red   = Color.FromArgb(0xF0, 0x4A, 0x3A);
    private static readonly Color Track = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);

    private static Color ColorFor(double f) => f >= 0.85 ? Red : f >= 0.60 ? Amber : Green;

    /// <summary>Render the two-bar gauge. Fractions are 0..1+ (clamped to full when drawing).</summary>
    public static Icon Render(double f5h, double fWeek, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float top = size * 0.12f;
            float bottom = size * 0.88f;
            float full = bottom - top;
            float barW = size * 0.30f;
            float gap = size * 0.16f;
            float side = (size - 2 * barW - gap) / 2f;

            DrawBar(g, side, top, barW, full, f5h);
            DrawBar(g, side + barW + gap, top, barW, full, fWeek);
        }

        IntPtr hIcon = bmp.GetHicon();
        using var tmp = Icon.FromHandle(hIcon);
        var icon = (Icon)tmp.Clone();
        DestroyIcon(hIcon);               // free the transient HICON immediately
        return icon;
    }

    private static void DrawBar(Graphics g, float x, float top, float w, float full, double frac)
    {
        float radius = w * 0.45f;

        // faint full-height track
        using (var trackBrush = new SolidBrush(Track))
            FillRounded(g, trackBrush, x, top, w, full, radius);

        double f = Math.Clamp(frac, 0, 1);
        if (f <= 0.001) return;

        float fillH = (float)(full * f);
        float y = top + (full - fillH);
        using var fillBrush = new SolidBrush(ColorFor(frac));
        FillRounded(g, fillBrush, x, y, w, fillH, Math.Min(radius, fillH / 2f));
    }

    private static void FillRounded(Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        if (h <= 0 || w <= 0) return;
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2f));
        using var path = new GraphicsPath();
        if (r <= 0.5f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
        }
        else
        {
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
        }
        g.FillPath(brush, path);
    }

    public static void DestroyHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero) DestroyIcon(handle);
    }
}
