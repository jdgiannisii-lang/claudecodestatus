using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeTracker;

public static class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTracker");

    public static string ConfigPath => Path.Combine(ConfigDir, "accounts.json");

    public static string DefaultClaudeCredentialsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public static TrackerConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<TrackerConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null)
                {
                    TrackerConfigDefaults.Apply(cfg);
                    return cfg;
                }
            }
        }
        catch
        {
            // Unreadable config: fall through and start fresh rather than crash at startup.
        }

        var fresh = new TrackerConfig();
        TrackerConfigDefaults.Apply(fresh);
        if (File.Exists(DefaultClaudeCredentialsPath))
        {
            fresh.Accounts.Add(new AccountConfig
            {
                Name = "Default",
                CredentialsPath = DefaultClaudeCredentialsPath,
            });
        }
        Save(fresh);
        return fresh;
    }

    public static void Save(TrackerConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    public static OauthCredentials? ReadCredentials(AccountConfig account)
    {
        string? json = account.CredentialsJson;
        if (json == null && account.CredentialsPath != null && File.Exists(account.CredentialsPath))
        {
            try { json = File.ReadAllText(account.CredentialsPath); }
            catch { return null; }
        }
        if (string.IsNullOrWhiteSpace(json)) return null;
        return ParseCredentials(json);
    }

    public static OauthCredentials? ParseCredentials(string json)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return null;
            var oauth = root["claudeAiOauth"] as JsonObject ?? root;

            var access = ReadString(oauth, "accessToken");
            if (string.IsNullOrEmpty(access)) return null;

            return new OauthCredentials
            {
                AccessToken = access,
                RefreshToken = ReadString(oauth, "refreshToken"),
                ExpiresAtUnixMs = ReadLong(oauth, "expiresAt"),
            };
        }
        catch
        {
            return null;
        }
    }

    public static void PersistRefreshedTokens(TrackerConfig config, AccountConfig account, RefreshedTokens tokens, string? fallbackRefreshToken)
    {
        string? refresh = tokens.RefreshToken ?? fallbackRefreshToken;
        try
        {
            if (account.CredentialsPath != null && File.Exists(account.CredentialsPath))
            {
                var text = File.ReadAllText(account.CredentialsPath);
                var updated = UpdateCredentialsJson(text, tokens.AccessToken, refresh, tokens.ExpiresAtUnixMs);
                if (updated != null)
                {
                    var tmp = account.CredentialsPath + ".tmp";
                    File.WriteAllText(tmp, updated);
                    File.Move(tmp, account.CredentialsPath, overwrite: true);
                }
            }
            else if (account.CredentialsJson != null)
            {
                var updated = UpdateCredentialsJson(account.CredentialsJson, tokens.AccessToken, refresh, tokens.ExpiresAtUnixMs);
                if (updated != null)
                {
                    account.CredentialsJson = updated;
                    Save(config);
                }
            }
        }
        catch
        {
            // Persisting is best-effort; the in-memory token still works for this run.
        }
    }

    private static string? UpdateCredentialsJson(string json, string accessToken, string? refreshToken, long expiresAtUnixMs)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root == null) return null;
        var oauth = root["claudeAiOauth"] as JsonObject ?? root;

        oauth["accessToken"] = accessToken;
        if (refreshToken != null) oauth["refreshToken"] = refreshToken;
        oauth["expiresAt"] = expiresAtUnixMs;

        return root.ToJsonString(JsonOptions);
    }

    private static string? ReadString(JsonObject obj, string key)
    {
        try { return obj[key]?.GetValue<string>(); }
        catch { return null; }
    }

    private static long? ReadLong(JsonObject obj, string key)
    {
        try
        {
            var node = obj[key];
            if (node == null) return null;
            return (long)node.GetValue<double>();
        }
        catch
        {
            try { return obj[key]?.GetValue<long>(); }
            catch { return null; }
        }
    }
}
