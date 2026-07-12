namespace ClaudeTracker;

public sealed class TrackerConfig
{
    public List<AccountConfig> Accounts { get; set; } = new();
    public int PollSeconds { get; set; } = 60;
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#FFFFFF";
    public string Density { get; set; } = "Comfortable";

    /// <summary>Flyout entrance animation: "Slide", "Fade", or "Off".</summary>
    public string Entrance { get; set; } = "Slide";
    public bool AnimateBars { get; set; } = true;
    public string TrayTreatment { get; set; } = "Static";
    public bool AutoUpdate { get; set; } = true;

    /// <summary>GitHub token for update checks while the repo is private; not needed once public.</summary>
    public string? GithubToken { get; set; }
}

public static class TrackerConfigDefaults
{
    public static void Apply(TrackerConfig config)
    {
        config.Theme = Theme.Get(config.Theme).Name;
        config.AccentColor = ColorHelpers.ParseOpaqueHtml(config.AccentColor, Color.White);
        config.Density = string.Equals(config.Density, "Compact", StringComparison.OrdinalIgnoreCase) ? "Compact" : "Comfortable";
        config.Entrance = NormalizeChoice(config.Entrance, new[] { "Slide", "Fade", "Off" }, "Slide");
        config.TrayTreatment = NormalizeChoice(config.TrayTreatment, new[] { "Static", "Pulse", "RGB" }, "Static");
    }

    private static string NormalizeChoice(string? value, IEnumerable<string> choices, string fallback) =>
        choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
}

public sealed class AccountConfig
{
    public string Name { get; set; } = "";

    /// <summary>Path to a Claude Code .credentials.json file (kept fresh by Claude Code itself).</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>Raw credentials JSON pasted by the user, for accounts without a file on this machine.</summary>
    public string? CredentialsJson { get; set; }
}

public sealed class OauthCredentials
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public long? ExpiresAtUnixMs { get; set; }

    public bool IsExpired =>
        ExpiresAtUnixMs is long ms &&
        DateTimeOffset.FromUnixTimeMilliseconds(ms) <= DateTimeOffset.UtcNow.AddMinutes(1);
}

public sealed class UsageWindow
{
    public double Utilization { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public int? WindowSeconds { get; set; }
}

public sealed class UsageSnapshot
{
    public UsageWindow? Session { get; set; }
    public UsageWindow? Weekly { get; set; }
    public UsageWindow? WeeklyOpus { get; set; }
    public string? PlanLabel { get; set; }

    /// <summary>Any other rate-limit windows the usage endpoint reports, keyed by their API name.</summary>
    public List<NamedWindow> Extra { get; set; } = new();

    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class NamedWindow
{
    public required string Key { get; init; }
    public string? Label { get; init; }
    public required UsageWindow Window { get; init; }
}

public enum UsageProvider
{
    Claude,
    Codex,
}

public sealed class AccountState
{
    public AccountConfig Config { get; }
    public UsageProvider Provider { get; }
    public bool CanRemove => Provider == UsageProvider.Claude;
    public UsageSnapshot? Snapshot { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? RateLimitRetryAt { get; set; }

    public AccountState(AccountConfig config, UsageProvider provider = UsageProvider.Claude)
    {
        Config = config;
        Provider = provider;
    }
}

public sealed class RefreshedTokens
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public long ExpiresAtUnixMs { get; init; }
}

public sealed class AuthorizeRequest
{
    public required string Url { get; init; }
    public required string Verifier { get; init; }
    public required string State { get; init; }
}
