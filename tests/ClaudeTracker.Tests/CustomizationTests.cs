using ClaudeTracker;
using System;
using System.Drawing;
using System.Net.Http.Headers;
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
    public void Rate_limit_policy_honors_retry_after_with_safe_bounds()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddSeconds(90), RateLimitPolicy.GetRetryAt(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(90)), now));
        Assert.Equal(now.AddMinutes(2), RateLimitPolicy.GetRetryAt(null, now));
        Assert.Equal(now.AddMinutes(15), RateLimitPolicy.GetRetryAt(
            new RetryConditionHeaderValue(TimeSpan.FromHours(1)), now));
    }
}
