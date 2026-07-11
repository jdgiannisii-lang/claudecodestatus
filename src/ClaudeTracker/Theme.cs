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
    public required Color Danger { get; init; }
    public required Color DangerBg { get; init; }
    public required Color Focus { get; init; }

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
            Danger = Color.FromArgb(255, 69, 58), DangerBg = Color.FromArgb(74, 27, 30), Focus = Color.FromArgb(100, 181, 246),
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
            Danger = Color.FromArgb(255, 95, 86), DangerBg = Color.FromArgb(76, 32, 40), Focus = Color.FromArgb(122, 184, 255),
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
            Danger = Color.FromArgb(255, 69, 58), DangerBg = Color.FromArgb(74, 27, 30), Focus = Color.FromArgb(125, 184, 255),
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
            Danger = Color.FromArgb(215, 0, 21), DangerBg = Color.FromArgb(255, 229, 229), Focus = Color.FromArgb(0, 103, 192),
        },
        new()
        {
            Name = "Forest", Bg = Color.FromArgb(11, 23, 18), CardBg = Color.FromArgb(17, 37, 27),
            ControlBg = Color.FromArgb(27, 52, 38), ControlHover = Color.FromArgb(40, 80, 57),
            PillBg = Color.FromArgb(18, 34, 25), PillSelected = Color.FromArgb(45, 92, 65), Track = Color.FromArgb(42, 68, 51),
            TextPrimary = Color.FromArgb(239, 250, 242), TextSecondary = Color.FromArgb(164, 193, 170), Warn = Color.FromArgb(255, 179, 64),
            Danger = Color.FromArgb(255, 107, 95), DangerBg = Color.FromArgb(77, 34, 35), Focus = Color.FromArgb(125, 226, 163),
        },
        new()
        {
            Name = "Rose", Bg = Color.FromArgb(26, 13, 20), CardBg = Color.FromArgb(41, 19, 30),
            ControlBg = Color.FromArgb(60, 29, 44), ControlHover = Color.FromArgb(86, 48, 71),
            PillBg = Color.FromArgb(37, 18, 28), PillSelected = Color.FromArgb(107, 61, 89), Track = Color.FromArgb(75, 40, 57),
            TextPrimary = Color.FromArgb(255, 240, 246), TextSecondary = Color.FromArgb(208, 167, 185), Warn = Color.FromArgb(255, 179, 77),
            Danger = Color.FromArgb(255, 107, 125), DangerBg = Color.FromArgb(82, 35, 47), Focus = Color.FromArgb(255, 143, 181),
        },
    };

    public static Theme Get(string name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
