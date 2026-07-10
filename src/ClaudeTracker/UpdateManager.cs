using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ClaudeTracker;

public static class UpdateManager
{
    private const string Owner = "jdgiannisii-lang";
    private const string Repo = "claudecodestatus";
    private const string AssetName = "ClaudeTracker.exe";

    private static readonly HttpClient Http = CreateClient();
    private static bool _updating;

    public static string CurrentVersion { get; } = GetCurrentVersion();
    public static string Status { get; private set; } = "";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTracker/" + GetCurrentVersion());
        return client;
    }

    private static string GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Delete the .old exe left behind by the previous update, retrying while it exits.</summary>
    public static void CleanupAfterRestart()
    {
        string old = Application.ExecutablePath + ".old";
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (!File.Exists(old)) return;
                    File.Delete(old);
                    return;
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
    }

    /// <returns>true if an update was applied and a new instance was started — the caller should exit.</returns>
    public static async Task<bool> CheckAndApplyAsync(string? token, bool apply)
    {
        if (_updating) return false;
        _updating = true;
        try
        {
            Status = "Checking…";
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            AddAuth(req, token);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Status = resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Can't see releases — private repo needs a GitHub token in accounts.json"
                    : $"Update check failed (HTTP {(int)resp.StatusCode})";
                return false;
            }

            var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync()) as JsonObject;
            string? tag = root?["tag_name"]?.GetValue<string>();
            var latest = tag == null ? null : ParseVersion(tag);
            var current = ParseVersion(CurrentVersion);
            if (latest == null || current == null)
            {
                Status = "Update check failed";
                return false;
            }
            if (latest <= current)
            {
                Status = "Up to date";
                return false;
            }

            string latestText = "v" + latest;
            if (!apply)
            {
                Status = latestText + " available";
                return false;
            }

            string? assetUrl = null;
            if (root!["assets"] is JsonArray assets)
            {
                foreach (var node in assets)
                {
                    if (node is JsonObject asset && asset["name"]?.GetValue<string>() == AssetName)
                    {
                        // The API asset url works for private repos (with auth); browser_download_url only for public.
                        assetUrl = (string.IsNullOrWhiteSpace(token)
                            ? asset["browser_download_url"]?.GetValue<string>()
                            : asset["url"]?.GetValue<string>())
                            ?? asset["browser_download_url"]?.GetValue<string>();
                        break;
                    }
                }
            }
            if (assetUrl == null)
            {
                Status = "Release " + latestText + " has no " + AssetName;
                return false;
            }

            Status = "Downloading " + latestText + "…";
            string exe = Application.ExecutablePath;
            string newPath = exe + ".new";
            using (var dlReq = new HttpRequestMessage(HttpMethod.Get, assetUrl))
            {
                dlReq.Headers.Accept.ParseAdd("application/octet-stream");
                AddAuth(dlReq, token);
                using var dlResp = await Http.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead);
                if (!dlResp.IsSuccessStatusCode)
                {
                    Status = $"Download failed (HTTP {(int)dlResp.StatusCode})";
                    return false;
                }
                await using var file = File.Create(newPath);
                await dlResp.Content.CopyToAsync(file);
            }

            // A real self-contained build is tens of MB; anything tiny is an error page, not the app.
            if (new FileInfo(newPath).Length < 1_000_000)
            {
                File.Delete(newPath);
                Status = "Downloaded file looked wrong; update aborted";
                return false;
            }

            // A running exe can't be deleted, but it can be renamed — swap via .old.
            string oldPath = exe + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exe, oldPath);
            try
            {
                File.Move(newPath, exe);
            }
            catch
            {
                File.Move(oldPath, exe);
                throw;
            }

            Status = "Restarting…";
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Status = "Update failed: " + Shorten(ex.Message);
            return false;
        }
        finally
        {
            _updating = false;
        }
    }

    private static void AddAuth(HttpRequestMessage req, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private static Version? ParseVersion(string text)
    {
        text = text.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0) text = text[..suffix];
        if (!Version.TryParse(text, out var v)) return null;
        return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }

    private static string Shorten(string text) =>
        text.Length <= 70 ? text : text[..69] + "…";
}
