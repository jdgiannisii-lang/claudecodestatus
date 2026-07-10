using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ClaudeTracker;

public static class AnthropicUsageClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string TokenUrl = "https://console.anthropic.com/v1/oauth/token";
    private const string AuthorizeUrl = "https://claude.ai/oauth/authorize";
    private const string RedirectUri = "https://console.anthropic.com/oauth/code/callback";
    private const string Scopes = "org:create_api_key user:profile user:inference";

    // Claude Code's public OAuth client id — required by the token endpoint for refresh grants.
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTracker/1.0");
        return client;
    }

    public static async Task<UsageSnapshot> FetchUsageAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Usage request failed (HTTP {(int)resp.StatusCode})", null, resp.StatusCode);

        return ParseUsage(body);
    }

    public static async Task<RefreshedTokens?> RefreshAsync(string refreshToken)
    {
        var payload = new JsonObject
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };
        return await PostTokenRequestAsync(payload);
    }

    public static AuthorizeRequest CreateAuthorizeRequest()
    {
        string verifier = RandomUrlSafe(64);
        string state = RandomUrlSafe(32);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        string url = AuthorizeUrl +
            "?code=true&response_type=code" +
            "&client_id=" + ClientId +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
            "&scope=" + Uri.EscapeDataString(Scopes) +
            "&code_challenge=" + challenge +
            "&code_challenge_method=S256" +
            "&state=" + state;

        return new AuthorizeRequest { Url = url, Verifier = verifier, State = state };
    }

    public static async Task<RefreshedTokens?> ExchangeCodeAsync(string pastedCode, string fallbackState, string verifier)
    {
        // The callback page shows the code as "<code>#<state>".
        string code = pastedCode.Trim();
        string state = fallbackState;
        int hash = code.IndexOf('#');
        if (hash >= 0)
        {
            state = code[(hash + 1)..];
            code = code[..hash];
        }
        if (code.Length == 0) return null;

        var payload = new JsonObject
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["state"] = state,
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        };
        return await PostTokenRequestAsync(payload);
    }

    private static async Task<RefreshedTokens?> PostTokenRequestAsync(JsonObject payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync()) as JsonObject;
        var access = ReadString(root, "access_token");
        if (root == null || access == null) return null;

        long expiresIn = ReadDouble(root, "expires_in") is double d ? (long)d : 3600;
        return new RefreshedTokens
        {
            AccessToken = access,
            RefreshToken = ReadString(root, "refresh_token"),
            ExpiresAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds(),
        };
    }

    private static string RandomUrlSafe(int bytes) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static UsageSnapshot ParseUsage(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Unexpected usage response");

        var snapshot = new UsageSnapshot
        {
            Session = ReadWindow(root, "five_hour"),
            Weekly = ReadWindow(root, "seven_day"),
            WeeklyOpus = ReadWindow(root, "seven_day_opus"),
            FetchedAt = DateTimeOffset.Now,
        };

        foreach (var kv in root)
        {
            if (kv.Key is "five_hour" or "seven_day" or "seven_day_opus") continue;
            if (kv.Value is JsonObject obj && obj["utilization"] != null)
            {
                var window = ReadWindowObject(obj);
                if (window != null)
                    snapshot.Extra.Add(new NamedWindow { Key = kv.Key, Window = window });
            }
        }

        return snapshot;
    }

    private static UsageWindow? ReadWindow(JsonObject root, string key)
    {
        var node = root[key] as JsonObject;
        if (node == null && root["usage"] is JsonObject nested) node = nested[key] as JsonObject;
        return node == null ? null : ReadWindowObject(node);
    }

    private static UsageWindow? ReadWindowObject(JsonObject node)
    {
        var window = new UsageWindow
        {
            Utilization = Math.Clamp(ReadDouble(node, "utilization") ?? 0, 0, 100),
        };

        var resetsAt = ReadString(node, "resets_at");
        if (resetsAt != null && DateTimeOffset.TryParse(resetsAt, out var parsed))
            window.ResetsAt = parsed;

        return window;
    }

    private static string? ReadString(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<string>(); }
        catch { return null; }
    }

    private static double? ReadDouble(JsonObject? obj, string key)
    {
        try { return obj?[key]?.GetValue<double>(); }
        catch { return null; }
    }
}
