using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeTracker;

public static class TrayIconRenderer
{
    public static Icon Render(double displayedPercentage, Color accent, string treatment, long elapsedMs, double? utilization = null)
    {
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var color = ColorHelpers.TrayColor(utilization ?? displayedPercentage, accent, treatment, elapsedMs);
            var text = displayedPercentage < 0 ? "--" : Math.Round(Math.Clamp(displayedPercentage, 0, 100)).ToString();
            var fontSize = text.Length >= 3 ? 22f : text == "--" ? 24f : 28f;
            using var backdrop = new SolidBrush(Color.FromArgb(96, 18, 18, 20));
            using var backdropPath = RoundedRect(new Rectangle(2, 2, 60, 47), 12);
            graphics.FillPath(backdrop, backdropPath);
            using var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, brush, new RectangleF(2, 3, 60, 45), format);
            using var track = new SolidBrush(Color.FromArgb(120, 120, 125));
            using var trackPath = RoundedRect(new Rectangle(6, 52, 52, 6), 3);
            graphics.FillPath(track, trackPath);
            if (displayedPercentage >= 0)
            {
                var fill = Math.Max(3, (int)Math.Round(Math.Clamp(displayedPercentage, 0, 100) * 52 / 100d));
                using var fillPath = RoundedRect(new Rectangle(6, 52, fill, 6), 3);
                graphics.FillPath(brush, fillPath);
            }
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeIcon.DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var diameter = Math.Max(2, radius * 2);
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class NativeIcon
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr handle);
}
