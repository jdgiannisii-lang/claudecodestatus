using ClaudeTracker;
using System;
using System.Drawing;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace ClaudeTracker.Tests;

public sealed class CustomizationTests
{
    [Fact]
    public void Legacy_configuration_gets_safe_defaults()
    {
        var config = new TrackerConfig { Theme = "unknown", AccentColor = "not-a-color", Density = "dense", Entrance = "zoom", TrayTreatment = "rainbow" };
        TrackerConfigDefaults.Apply(config);
        Assert.Equal("Dark", config.Theme);
        Assert.Equal("#FFFFFF", config.AccentColor);
        Assert.Equal("Comfortable", config.Density);
        Assert.Equal("Slide", config.Entrance);
        Assert.Equal("Static", config.TrayTreatment);
    }

    [Fact]
    public void Theme_catalog_has_six_presets_and_falls_back_to_dark()
    {
        Assert.Equal(6, Theme.All.Length);
        Assert.Equal("Dark", Theme.Get("missing").Name);
        Assert.Equal("Rose", Theme.Get("rose").Name);
    }

    [Fact]
    public void Credential_parser_reads_nested_synthetic_credentials()
    {
        var credentials = CredentialStore.ParseCredentials("{\"claudeAiOauth\":{\"accessToken\":\"synthetic-access\",\"refreshToken\":\"synthetic-refresh\",\"expiresAt\":123}}");
        Assert.NotNull(credentials);
        Assert.Equal("synthetic-access", credentials!.AccessToken);
        Assert.Equal(123, credentials.ExpiresAtUnixMs);
    }

    [Theory]
    [InlineData("code#expected", "expected", true)]
    [InlineData("code#wrong", "expected", false)]
    [InlineData("code", "expected", true)]
    public void Callback_state_parser_is_deterministic(string input, string expected, bool valid)
    {
        var completion = OAuthCompletionParser.Parse(input);
        Assert.True(completion.HasCode);
        Assert.Equal(valid, OAuthCompletionParser.MatchesPendingState(completion, expected));
    }

    [Fact]
    public void Tray_effects_preserve_severity_and_are_deterministic()
    {
        var accent = Color.FromArgb(10, 132, 255);
        Assert.Equal(Color.FromArgb(255, 69, 58), ColorHelpers.TrayColor(90, accent, "RGB", 100));
        Assert.Equal(Color.FromArgb(255, 159, 10), ColorHelpers.TrayColor(70, accent, "Pulse", 100));
        Assert.Equal(ColorHelpers.TrayColor(20, accent, "RGB", 420), ColorHelpers.TrayColor(20, accent, "RGB", 420));
        Assert.Equal(ColorHelpers.TrayColor(20, accent, "RGB", 0), ColorHelpers.TrayColor(20, accent, "RGB", 10_000));
        Assert.NotEqual(ColorHelpers.TrayColor(20, accent, "RGB", 0), ColorHelpers.TrayColor(20, accent, "RGB", 2_500));
    }

    [Fact]
    public void Tray_renderer_creates_a_64_pixel_source_icon()
    {
        using var icon = TrayIconRenderer.Render(42, Color.White, "Static", 0);
        using var bitmap = icon.ToBitmap();
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }

    [Fact]
    public void Rate_limit_policy_honors_retry_after_and_escalates_without_one()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddSeconds(90), RateLimitPolicy.GetRetryAt(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(90)), now));
        Assert.Equal(now.AddMinutes(5), RateLimitPolicy.GetRetryAt(null, now));
        Assert.Equal(now.AddMinutes(20), RateLimitPolicy.GetRetryAt(null, now, consecutiveRateLimits: 3));
        Assert.Equal(now.AddHours(1), RateLimitPolicy.GetRetryAt(
            new RetryConditionHeaderValue(TimeSpan.FromHours(1)), now));
    }

    [Fact]
    public void Codex_credentials_and_usage_parse_without_persisting_real_tokens()
    {
        var claims = "{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"account-synthetic\",\"chatgpt_account_is_fedramp\":false}}";
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var credentials = CodexUsageClient.ParseCredentials(
            $"{{\"auth_mode\":\"chatgpt\",\"tokens\":{{\"access_token\":\"token-synthetic\",\"id_token\":\"header.{payload}.signature\"}}}}");

        Assert.NotNull(credentials);
        Assert.Equal("token-synthetic", credentials!.AccessToken);
        Assert.Equal("account-synthetic", credentials.AccountId);

        var usage = CodexUsageClient.ParseUsage("""
            {
              "plan_type": "plus",
              "rate_limit": {
                "primary_window": { "used_percent": 42, "limit_window_seconds": 18000, "reset_at": 1783843951 },
                "secondary_window": { "used_percent": 17, "limit_window_seconds": 604800, "reset_at": 1784354957 }
              }
            }
            """);

        Assert.Equal("Plus", usage.PlanLabel);
        Assert.Equal(42, usage.Session!.Utilization);
        Assert.Equal(18000, usage.Session.WindowSeconds);
        Assert.Equal(17, usage.Weekly!.Utilization);
    }

    [Theory]
    [InlineData(UsageProvider.Codex, 17, 83, "83% left")]
    [InlineData(UsageProvider.Claude, 17, 17, "17%")]
    [InlineData(UsageProvider.Codex, 100, 0, "0% left")]
    public void Usage_presentation_makes_Codex_remaining_explicit(
        UsageProvider provider, double utilization, double expectedPercentage, string expectedLabel)
    {
        Assert.Equal(expectedPercentage, UsagePresentation.DisplayPercentage(provider, utilization));
        Assert.Equal(expectedLabel, UsagePresentation.PercentageLabel(provider, utilization));
    }

    [Fact]
    public void Claude_usage_credits_parse_without_becoming_a_rate_limit_window()
    {
        var usage = AnthropicUsageClient.ParseUsage("""
            {
              "five_hour": { "utilization": 12 },
              "extra_usage": {
                "is_enabled": true,
                "monthly_limit": 100000,
                "used_credits": 45710,
                "utilization": 45.71,
                "currency": "USD"
              }
            }
            """);

        Assert.NotNull(usage.ClaudeUsageCredits);
        Assert.True(usage.ClaudeUsageCredits!.IsEnabled);
        Assert.Equal(100000, usage.ClaudeUsageCredits.MonthlyLimit);
        Assert.Equal(45710, usage.ClaudeUsageCredits.UsedCredits);
        Assert.Equal(54290, usage.ClaudeUsageCredits.RemainingCredits);
        Assert.Equal("54,290 credits left", usage.ClaudeUsageCredits.SummaryLabel);
        Assert.Empty(usage.Extra);
    }

    [Theory]
    [InlineData("{\"has_credits\":true,\"unlimited\":false,\"balance\":\"12.6\"}", true, true, "13 credits")]
    [InlineData("{\"has_credits\":true,\"unlimited\":false,\"balance\":\"12.6\"}", false, false, "13 credits")]
    [InlineData("{\"has_credits\":true,\"unlimited\":true,\"balance\":\"not-a-number\"}", true, true, "Unlimited")]
    [InlineData(null, true, false, "No credits")]
    [InlineData("{\"has_credits\":false,\"unlimited\":false,\"balance\":\"99\"}", true, false, "99 credits")]
    [InlineData("{\"has_credits\":true,\"unlimited\":false,\"balance\":\"not-a-number\"}", true, false, "No credits")]
    [InlineData("{\"has_credits\":true,\"unlimited\":false,\"balance\":\"0\"}", true, false, "No credits")]
    public void Codex_extra_usage_requires_a_reached_limit_and_usable_credits(
        string creditsJson, bool limitReached, bool expectedInUse, string expectedBalance)
    {
        string credits = creditsJson == null ? "" : $", \"credits\": {creditsJson}";
        var usage = CodexUsageClient.ParseUsage($$"""
            {
              "rate_limit": {
                "limit_reached": {{limitReached.ToString().ToLowerInvariant()}},
                "primary_window": { "used_percent": 0 }
              }{{credits}}
            }
            """);

        Assert.NotNull(usage.ExtraUsage);
        Assert.Equal(expectedInUse, usage.ExtraUsage!.IsInUse);
        Assert.Equal(expectedBalance, usage.ExtraUsage.BalanceLabel);
    }
}
