namespace ClaudeTracker;

public sealed class Theme
{
    public required string Name { get; init; }
    public required Color Bg { get; init; }
    public required Color CardBg { get; init; }
    public required Color ControlBg { get; init; }
    public required Color ControlHover { get; init; }
    public required Color PillBg { get; init; }
    public required Color PillSelected { get; init; }
    public required Color Track { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color Warn { get; init; }

    public static readonly Theme[] All =
    {
        new()
        {
            Name = "Dark",
            Bg = Color.FromArgb(16, 16, 16),
            CardBg = Color.FromArgb(28, 28, 30),
            ControlBg = Color.FromArgb(44, 44, 46),
            ControlHover = Color.FromArgb(60, 60, 64),
            PillBg = Color.FromArgb(30, 30, 32),
            PillSelected = Color.FromArgb(72, 72, 76),
            Track = Color.FromArgb(58, 58, 60),
            TextPrimary = Color.FromArgb(242, 242, 247),
            TextSecondary = Color.FromArgb(152, 152, 157),
            Warn = Color.FromArgb(255, 159, 10),
        },
        new()
        {
            Name = "Midnight",
            Bg = Color.FromArgb(9, 13, 27),
            CardBg = Color.FromArgb(19, 26, 48),
            ControlBg = Color.FromArgb(32, 42, 74),
            ControlHover = Color.FromArgb(44, 56, 96),
            PillBg = Color.FromArgb(19, 26, 48),
            PillSelected = Color.FromArgb(51, 64, 107),
            Track = Color.FromArgb(42, 53, 84),
            TextPrimary = Color.FromArgb(235, 240, 255),
            TextSecondary = Color.FromArgb(142, 154, 192),
            Warn = Color.FromArgb(255, 170, 60),
        },
        new()
        {
            Name = "OLED",
            Bg = Color.Black,
            CardBg = Color.FromArgb(17, 17, 19),
            ControlBg = Color.FromArgb(34, 34, 38),
            ControlHover = Color.FromArgb(52, 52, 58),
            PillBg = Color.FromArgb(17, 17, 19),
            PillSelected = Color.FromArgb(58, 58, 64),
            Track = Color.FromArgb(44, 44, 48),
            TextPrimary = Color.FromArgb(242, 242, 247),
            TextSecondary = Color.FromArgb(140, 140, 146),
            Warn = Color.FromArgb(255, 159, 10),
        },
        new()
        {
            Name = "Light",
            Bg = Color.FromArgb(242, 242, 247),
            CardBg = Color.White,
            ControlBg = Color.FromArgb(229, 229, 234),
            ControlHover = Color.FromArgb(213, 213, 220),
            PillBg = Color.FromArgb(229, 229, 234),
            PillSelected = Color.White,
            Track = Color.FromArgb(209, 209, 214),
            TextPrimary = Color.FromArgb(28, 28, 30),
            TextSecondary = Color.FromArgb(108, 108, 112),
            Warn = Color.FromArgb(255, 149, 0),
        },
    };

    public static Theme Get(string name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
