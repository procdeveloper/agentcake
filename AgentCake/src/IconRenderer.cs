using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AgentCake;

public static class IconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Color Track = Color.FromArgb(42, 45, 48);
    private static readonly Color Normal = Color.FromArgb(65, 150, 100);
    private static readonly Color Warning = Color.FromArgb(241, 205, 76);
    private static readonly Color Critical = Color.FromArgb(244, 161, 174);

    public static Icon Render(ServiceUsage codex, ServiceUsage claude, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            int gap = Math.Max(1, size / 16);
            int rowHeight = (size - gap * 3) / 2;
            DrawRow(graphics, new Rectangle(gap, gap, size - gap * 2, rowHeight), codex);
            DrawRow(graphics, new Rectangle(gap, gap * 2 + rowHeight, size - gap * 2, rowHeight), claude);
        }
        IntPtr handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return icon;
    }

    private static void DrawRow(Graphics graphics, Rectangle row, ServiceUsage usage)
    {
        using var track = new SolidBrush(Track);
        graphics.FillRectangle(track, row);
        if (usage.UsedPercent is { } used)
        {
            int width = (int)Math.Round(row.Width * Math.Clamp(used / 100d, 0d, 1d));
            using var fill = new SolidBrush(used >= 80 ? Critical : used >= 65 ? Warning : Normal);
            graphics.FillRectangle(fill, new Rectangle(row.X, row.Y, width, row.Height));
        }

        string label = usage.RemainingPercent is { } remaining ? $"{remaining}%" : "--";
        float fontSize = row.Height >= 13 ? 9f : 7f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = graphics.MeasureString(label, font);
        float x = row.X + (row.Width - measured.Width) / 2f;
        float y = row.Y + (row.Height - measured.Height) / 2f - 1f;
        using var shadow = new SolidBrush(Color.FromArgb(130, Color.Black));
        using var text = new SolidBrush(Color.White);
        graphics.DrawString(label, font, shadow, x + 1, y + 1);
        graphics.DrawString(label, font, text, x, y);
    }
}