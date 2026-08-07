using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace ClaudeTracker;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "ClaudeTracker";

    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly System.Windows.Forms.Timer _trayAnimationTimer;
    private readonly FlyoutForm _flyout;
    private readonly TrackerConfig _config;
    private readonly List<AccountState> _states = new();
    private bool _refreshing;

    public IReadOnlyList<AccountState> States => _states;
    public DateTimeOffset? LastUpdated { get; private set; }
    public TrackerConfig Config => _config;

    public void SaveConfig()
    {
        TrackerConfigDefaults.Apply(_config);
        CredentialStore.Save(_config);
        UpdateTrayAnimationTimer();
        UpdateIcon();
    }

    public TrayApplicationContext()
    {
        _config = CredentialStore.Load();
        foreach (var acct in _config.Accounts)
            _states.Add(new AccountState(acct));
        EnsureCodexState();

        _flyout = new FlyoutForm(this);

        _trayIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Claude + Codex Tracker",
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };
        UpdateIcon();

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(15, _config.PollSeconds) * 1000 };
        _timer.Tick += async (s, e) => await RefreshAllAsync();
        _timer.Start();

        _trayAnimationTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _trayAnimationTimer.Tick += (s, e) => UpdateIcon();
        UpdateTrayAnimationTimer();

        _updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        _updateTimer.Tick += async (s, e) => await RunUpdateCheckAsync();
        _updateTimer.Start();

        _ = RefreshAllAsync();
        _ = InitialUpdateCheckAsync();
    }

    private async Task InitialUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        await RunUpdateCheckAsync();
    }

    public async Task RunUpdateCheckAsync(bool manual = false)
    {
        if (!_config.AutoUpdate && !manual) return;
        bool restarting = await UpdateManager.CheckAndApplyAsync(_config.GithubToken, apply: true);
        if (restarting) ExitApp();
        else _flyout.RefreshView();
    }

    public void RequestRefresh() => _ = RefreshAllAsync(manual: true);

    public string? AddAccount(string name, string pathOrJson)
    {
        name = name.Trim();
        string input = pathOrJson.Trim();
        if (name.Length == 0) return "Enter a name for the account.";
        if (input.Length == 0) return "Enter a credentials file path or JSON.";
        if (_config.Accounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return "That name is already in use.";

        var acct = new AccountConfig { Name = name };
        if (input.StartsWith('{'))
        {
            if (CredentialStore.ParseCredentials(input) == null)
                return "Could not find an access token in that JSON.";
            acct.CredentialsJson = input;
        }
        else
        {
            string path = Environment.ExpandEnvironmentVariables(input.Trim('"'));
            if (!File.Exists(path)) return "File not found: " + path;
            if (CredentialStore.ReadCredentials(new AccountConfig { CredentialsPath = path }) == null)
                return "Could not read an access token from that file.";
            acct.CredentialsPath = path;
        }

        _config.Accounts.Add(acct);
        CredentialStore.Save(_config);
        _states.Add(new AccountState(acct));
        _ = RefreshAllAsync();
        return null;
    }

    public string? AddAccountFromTokens(string name, RefreshedTokens tokens)
    {
        name = name.Trim();
        if (name.Length == 0) name = "Account " + (_config.Accounts.Count + 1);
        if (_config.Accounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return "That name is already in use.";

        var credsJson = new JsonObject
        {
            ["claudeAiOauth"] = new JsonObject
            {
                ["accessToken"] = tokens.AccessToken,
                ["refreshToken"] = tokens.RefreshToken,
                ["expiresAt"] = tokens.ExpiresAtUnixMs,
            },
        };

        var acct = new AccountConfig { Name = name, CredentialsJson = credsJson.ToJsonString() };
        _config.Accounts.Add(acct);
        CredentialStore.Save(_config);
        _states.Add(new AccountState(acct));
        _ = RefreshAllAsync();
        return null;
    }

    public void RemoveAccount(AccountState state)
    {
        if (!state.CanRemove) return;
        _config.Accounts.Remove(state.Config);
        CredentialStore.Save(_config);
        _states.Remove(state);
        UpdateIcon();
        _flyout.RefreshView();
    }

    public async Task RefreshAllAsync(bool manual = false)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            EnsureCodexState();
            foreach (var st in _states.ToList())
            {
                var now = DateTimeOffset.UtcNow;
                if (st.RateLimitRetryAt is DateTimeOffset retryAt && retryAt > now)
                {
                    st.Error = FormatRateLimitStatus(retryAt, st.Snapshot != null);
                    continue;
                }

                if (st.Provider == UsageProvider.Claude &&
                    (!manual && st.NextUsageRefreshAt is DateTimeOffset nextRefresh && nextRefresh > now ||
                     manual && st.LastUsageRequestAt is DateTimeOffset lastRequest && now - lastRequest < UsageRefreshPolicy.ManualRefreshCooldown))
                    continue;

                try
                {
                    if (st.Provider == UsageProvider.Claude)
                        st.LastUsageRequestAt = now;
                    await RefreshAccountAsync(st);
                }
                catch (UsageRateLimitException ex)
                {
                    st.ConsecutiveUsageRateLimits++;
                    var nextRetryAt = ex.UsesAdaptiveBackoff
                        ? RateLimitPolicy.GetRetryAt(ex.RetryAfter, now, st.ConsecutiveUsageRateLimits)
                        : ex.RetryAt;
                    st.RateLimitRetryAt = nextRetryAt;
                    st.NextUsageRefreshAt = nextRetryAt;
                    st.Error = FormatRateLimitStatus(nextRetryAt, st.Snapshot != null);
                }
                catch (Exception ex)
                {
                    st.Snapshot = null;
                    st.Error = Shorten(ex.Message);
                }
            }
            LastUpdated = DateTimeOffset.Now;
        }
        finally
        {
            _refreshing = false;
            UpdateIcon();
            _flyout.RefreshView();
        }
    }

    private async Task RefreshAccountAsync(AccountState st)
    {
        if (st.Provider == UsageProvider.Codex)
        {
            await RefreshCodexAccountAsync(st);
            return;
        }

        var creds = CredentialStore.ReadCredentials(st.Config);
        if (creds?.AccessToken == null)
        {
            st.Snapshot = null;
            st.Error = "No credentials found. Sign in with Claude Code first.";
            return;
        }

        if (creds.IsExpired && creds.RefreshToken != null)
            creds = await TryRefreshAsync(st.Config, creds) ?? creds;

        try
        {
            st.Snapshot = await AnthropicUsageClient.FetchUsageAsync(creds.AccessToken!);
            st.Error = null;
            st.RateLimitRetryAt = null;
            st.ConsecutiveUsageRateLimits = 0;
            st.NextUsageRefreshAt = DateTimeOffset.UtcNow.Add(UsageRefreshPolicy.AutomaticRefreshInterval);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && creds.RefreshToken != null)
        {
            var refreshed = await TryRefreshAsync(st.Config, creds);
            if (refreshed == null)
            {
                st.Snapshot = null;
                st.Error = "Sign-in expired. Run Claude Code once to sign in again.";
                return;
            }
            st.Snapshot = await AnthropicUsageClient.FetchUsageAsync(refreshed.AccessToken!);
            st.Error = null;
            st.RateLimitRetryAt = null;
            st.ConsecutiveUsageRateLimits = 0;
            st.NextUsageRefreshAt = DateTimeOffset.UtcNow.Add(UsageRefreshPolicy.AutomaticRefreshInterval);
        }
    }

    private async Task RefreshCodexAccountAsync(AccountState state)
    {
        var credentials = CodexUsageClient.ReadCredentials();
        if (credentials == null)
        {
            state.Snapshot = null;
            state.Error = "Codex sign-in not found. Open Codex and sign in first.";
            return;
        }

        try
        {
            state.Snapshot = await CodexUsageClient.FetchUsageAsync(credentials);
            state.Error = null;
            state.RateLimitRetryAt = null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            state.Snapshot = null;
            state.Error = "Codex sign-in expired. Open Codex to refresh it.";
        }
    }

    private void EnsureCodexState()
    {
        if (CodexUsageClient.HasLocalAuth() && _states.All(state => state.Provider != UsageProvider.Codex))
        {
            _states.Add(new AccountState(
                new AccountConfig { Name = "Codex" },
                UsageProvider.Codex));
        }
    }

    private static string FormatRateLimitStatus(DateTimeOffset retryAt, bool hasSnapshot)
    {
        var retryTime = retryAt.ToLocalTime().ToString("h:mm tt");
        return hasSnapshot
            ? $"Rate limited · last data shown · retry {retryTime}"
            : $"Rate limited · retry {retryTime}";
    }

    private async Task<OauthCredentials?> TryRefreshAsync(AccountConfig acct, OauthCredentials creds)
    {
        var tokens = await AnthropicUsageClient.RefreshAsync(creds.RefreshToken!);
        if (tokens == null) return null;

        CredentialStore.PersistRefreshedTokens(_config, acct, tokens, creds.RefreshToken);
        return new OauthCredentials
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken ?? creds.RefreshToken,
            ExpiresAtUnixMs = tokens.ExpiresAtUnixMs,
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (s, e) => ToggleFlyout());
        menu.Items.Add("Refresh now", null, (s, e) => RequestRefresh());

        var startup = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled(),
        };
        startup.CheckedChanged += (s, e) => SetStartup(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (s, e) => ExitApp());
        return menu;
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.HideFlyout();
        }
        else if ((DateTimeOffset.Now - (_flyout.HiddenAt ?? DateTimeOffset.MinValue)).TotalMilliseconds > 300)
        {
            _flyout.ShowFlyout();
        }
    }

    private void ExitApp()
    {
        _timer.Stop();
        _updateTimer.Stop();
        _trayAnimationTimer.Stop();
        _trayAnimationTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _flyout.Dispose();
        ExitThread();
    }

    private void UpdateIcon()
    {
        double displayedPercentage = -1, highestUtilization = -1;
        foreach (var st in _states)
        {
            if (st.Snapshot?.Session is { } session && session.Utilization > highestUtilization)
            {
                highestUtilization = session.Utilization;
                displayedPercentage = UsagePresentation.DisplayPercentage(st.Provider, session.Utilization);
            }
        }

        var accent = ColorTranslator.FromHtml(ColorHelpers.ParseOpaqueHtml(_config.AccentColor, Color.White));
        var icon = TrayIconRenderer.Render(displayedPercentage, accent, _config.TrayTreatment, Environment.TickCount64, highestUtilization);
        var old = _trayIcon.Icon;
        _trayIcon.Icon = icon;
        old?.Dispose();

        _trayIcon.Text = Shorten(BuildTooltip(), 120);
    }

    private void UpdateTrayAnimationTimer()
    {
        bool animate = _config.TrayTreatment.Equals("Pulse", StringComparison.OrdinalIgnoreCase) || _config.TrayTreatment.Equals("RGB", StringComparison.OrdinalIgnoreCase);
        if (animate && _trayIcon.Visible) _trayAnimationTimer.Start(); else _trayAnimationTimer.Stop();
    }

    private string BuildTooltip()
    {
        if (_states.Count == 0) return "Claude + Codex Tracker — no accounts";
        var parts = new List<string>();
        foreach (var st in _states)
        {
            if (st.Snapshot?.Session != null)
            {
                string session = UsagePresentation.PercentageLabel(st.Provider, st.Snapshot.Session.Utilization);
                string weekly = st.Snapshot.Weekly != null
                    ? $" wk {UsagePresentation.PercentageLabel(st.Provider, st.Snapshot.Weekly.Utilization)}"
                    : "";
                parts.Add($"{st.Config.Name}: {session}{weekly}");
            }
            else
            {
                parts.Add($"{st.Config.Name}: —");
            }
        }
        return string.Join("  |  ", parts);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey);
        return key?.GetValue(StartupValueName) != null;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRunKey);
        if (key == null) return;
        if (enabled) key.SetValue(StartupValueName, '"' + Application.ExecutablePath + '"');
        else key.DeleteValue(StartupValueName, false);
    }

    private static string Shorten(string text, int max = 90) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
