namespace ClaudeTracker;

public static class ColorHelpers
{
    public static string ParseOpaqueHtml(string? value, Color fallback)
    {
        try
        {
            var color = ColorTranslator.FromHtml(value ?? string.Empty);
            return ToOpaqueHtml(color);
        }
        catch
        {
            return ToOpaqueHtml(fallback);
        }
    }

    private static string ToOpaqueHtml(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color TrayColor(double usage, Color accent, string treatment, long elapsedMs)
    {
        if (usage < 0) return Color.FromArgb(152, 152, 157);
        if (usage >= 90) return Color.FromArgb(255, 69, 58);
        if (usage >= 70) return Color.FromArgb(255, 159, 10);
        if (string.Equals(treatment, "Pulse", StringComparison.OrdinalIgnoreCase))
        {
            var scale = 0.85 + 0.15 * ((Math.Sin(elapsedMs * 2 * Math.PI / 800d) + 1) / 2);
            return Color.FromArgb((int)(accent.R * scale), (int)(accent.G * scale), (int)(accent.B * scale));
        }
        if (string.Equals(treatment, "RGB", StringComparison.OrdinalIgnoreCase))
            return ColorFromHue((elapsedMs % 1500) * 360d / 1500d);

        return accent;
    }

    private static Color ColorFromHue(double hue)
    {
        const double chroma = 1d;
        var x = chroma * (1 - Math.Abs((hue / 60d % 2) - 1));
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
