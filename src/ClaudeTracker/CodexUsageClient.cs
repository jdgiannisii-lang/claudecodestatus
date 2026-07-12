using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ClaudeTracker;

public sealed class CodexCredentials
{
    public required string AccessToken { get; init; }
    public string? AccountId { get; init; }
    public bool IsFedRamp { get; init; }
}

public static class CodexUsageClient
{
    private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";
    private static readonly HttpClient Http = CreateClient();

    public static string AuthFilePath
    {
        get
        {
            var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(Environment.ExpandEnvironmentVariables(codexHome), "auth.json");
        }
    }

    public static bool HasLocalAuth() => ReadCredentials() != null;

    public static CodexCredentials? ReadCredentials()
    {
        try
        {
            return File.Exists(AuthFilePath) ? ParseCredentials(File.ReadAllText(AuthFilePath)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static CodexCredentials? ParseCredentials(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            var tokens = root?["tokens"] as JsonObject;
            var accessToken = ReadString(tokens, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            var authClaims = DecodeJwtPayload(ReadString(tokens, "id_token"))?["https://api.openai.com/auth"] as JsonObject;
            var accountId = ReadString(tokens, "account_id") ?? ReadString(authClaims, "chatgpt_account_id");
            var isFedRamp = ReadBool(authClaims, "chatgpt_account_is_fedramp") ?? false;
            return new CodexCredentials
            {
                AccessToken = accessToken,
                AccountId = accountId,
                IsFedRamp = isFedRamp,
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task<UsageSnapshot> FetchUsageAsync(CodexCredentials credentials)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        if (credentials.IsFedRamp)
            request.Headers.TryAddWithoutValidation("X-OpenAI-Fedramp", "true");

        using var response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new UsageRateLimitException(RateLimitPolicy.GetRetryAt(response.Headers.RetryAfter, DateTimeOffset.UtcNow));
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Codex usage request failed (HTTP {(int)response.StatusCode})", null, response.StatusCode);

        return ParseUsage(await response.Content.ReadAsStringAsync());
    }

    public static UsageSnapshot ParseUsage(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Unexpected Codex usage response");
        var rateLimit = root["rate_limit"] as JsonObject
            ?? throw new InvalidOperationException("Codex usage response has no rate limits");

        var snapshot = new UsageSnapshot
        {
            Session = ReadWindow(rateLimit, "primary_window"),
            Weekly = ReadWindow(rateLimit, "secondary_window"),
            PlanLabel = Humanize(ReadString(root, "plan_type")),
            ExtraUsage = ReadExtraUsage(root, rateLimit),
            FetchedAt = DateTimeOffset.Now,
        };

        if (root["additional_rate_limits"] is JsonArray additional)
        {
            foreach (var node in additional.OfType<JsonObject>())
            {
                var name = ReadString(node, "limit_name") ?? ReadString(node, "metered_feature") ?? "Additional";
                var details = node["rate_limit"] as JsonObject;
                AddExtra(snapshot, name + " primary", details, "primary_window");
                AddExtra(snapshot, name + " secondary", details, "secondary_window");
            }
        }

        return snapshot;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTracker/" + UpdateManager.CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static void AddExtra(UsageSnapshot snapshot, string label, JsonObject? details, string key)
    {
        var window = ReadWindow(details, key);
        if (window == null) return;
        snapshot.Extra.Add(new NamedWindow
        {
            Key = "codex_" + label.ToLowerInvariant().Replace(' ', '_'),
            Label = Humanize(label),
            Window = window,
        });
    }

    private static UsageWindow? ReadWindow(JsonObject? root, string key)
    {
        var node = root?[key] as JsonObject;
        var utilization = ReadDouble(node, "used_percent");
        if (utilization == null) return null;

        var window = new UsageWindow
        {
            Utilization = Math.Clamp(utilization.Value, 0, 100),
            WindowSeconds = ReadInt(node, "limit_window_seconds"),
        };
        if (ReadLong(node, "reset_at") is long resetAt && resetAt > 0)
            window.ResetsAt = DateTimeOffset.FromUnixTimeSeconds(resetAt);
        return window;
    }

    private static CodexExtraUsage ReadExtraUsage(JsonObject root, JsonObject rateLimit)
    {
        var credits = root["credits"] as JsonObject;
        return new CodexExtraUsage
        {
            HasCredits = ReadBool(credits, "has_credits") ?? false,
            Unlimited = ReadBool(credits, "unlimited") ?? false,
            Balance = ReadDecimal(credits, "balance"),
            IncludedLimitReached = ReadBool(rateLimit, "limit_reached") ?? false,
        };
    }

    private static JsonObject? DecodeJwtPayload(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as JsonObject;
    }

    private static string? Humanize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Replace('_', ' ').Trim();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string? ReadString(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<string>(); }
        catch { return null; }
    }

    private static bool? ReadBool(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<bool>(); }
        catch { return null; }
    }

    private static double? ReadDouble(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<double>(); }
        catch { return null; }
    }

    private static decimal? ReadDecimal(JsonObject? obj, string key)
    {
        try
        {
            var value = obj?[key] as JsonValue;
            if (value == null) return null;
            if (value.TryGetValue<decimal>(out var number)) return number;
            if (value.TryGetValue<string>(out var text) &&
                decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        catch
        {
            // Treat an unexpected balance shape as unavailable rather than failing the whole usage refresh.
        }
        return null;
    }

    private static int? ReadInt(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<int>(); }
        catch { return null; }
    }

    private static long? ReadLong(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<long>(); }
        catch { return null; }
    }
}
