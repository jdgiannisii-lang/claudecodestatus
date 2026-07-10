using System.Drawing.Drawing2D;
using System.Drawing.Text;
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
    private readonly FlyoutForm _flyout;
    private readonly TrackerConfig _config;
    private readonly List<AccountState> _states = new();
    private bool _refreshing;

    public IReadOnlyList<AccountState> States => _states;
    public DateTimeOffset? LastUpdated { get; private set; }
    public TrackerConfig Config => _config;

    public void SaveConfig()
    {
        CredentialStore.Save(_config);
        UpdateIcon();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayApplicationContext()
    {
        _config = CredentialStore.Load();
        foreach (var acct in _config.Accounts)
            _states.Add(new AccountState(acct));

        _flyout = new FlyoutForm(this);

        _trayIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Claude Tracker",
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

        _ = RefreshAllAsync();
    }

    public void RequestRefresh() => _ = RefreshAllAsync();

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
        _config.Accounts.Remove(state.Config);
        CredentialStore.Save(_config);
        _states.Remove(state);
        UpdateIcon();
        _flyout.RefreshView();
    }

    public async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var st in _states.ToList())
            {
                try
                {
                    await RefreshAccountAsync(st);
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
        }
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
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _flyout.Dispose();
        ExitThread();
    }

    private void UpdateIcon()
    {
        double best = -1;
        foreach (var st in _states)
        {
            if (st.Snapshot?.Session != null)
                best = Math.Max(best, st.Snapshot.Session.Utilization);
        }

        Color accent = Color.White;
        try { accent = ColorTranslator.FromHtml(_config.AccentColor); }
        catch { }

        Color severity = best switch
        {
            < 0 => Color.FromArgb(152, 152, 157),
            >= 90 => Color.FromArgb(255, 69, 58),
            >= 70 => Color.FromArgb(255, 159, 10),
            _ => accent,
        };

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            string text = best < 0 ? "--" : Math.Round(best).ToString();
            float fontSize = text.Length >= 3 ? 11f : 14f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(best >= 70 ? severity : Color.White);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, textBrush, (32 - size.Width) / 2f, (25 - size.Height) / 2f);

            using var trackBrush = new SolidBrush(Color.FromArgb(90, 90, 95));
            g.FillRectangle(trackBrush, 4, 26, 24, 4);
            if (best >= 0)
            {
                int fill = (int)Math.Max(2, 24 * Math.Clamp(best, 0, 100) / 100.0);
                using var fillBrush = new SolidBrush(severity);
                g.FillRectangle(fillBrush, 4, 26, fill, 4);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var handleIcon = Icon.FromHandle(hIcon);
            var old = _trayIcon.Icon;
            _trayIcon.Icon = (Icon)handleIcon.Clone();
            old?.Dispose();
        }
        finally
        {
            DestroyIcon(hIcon);
        }

        _trayIcon.Text = Shorten(BuildTooltip(), 120);
    }

    private string BuildTooltip()
    {
        if (_states.Count == 0) return "Claude Tracker — no accounts";
        var parts = new List<string>();
        foreach (var st in _states)
        {
            if (st.Snapshot?.Session != null)
            {
                string weekly = st.Snapshot.Weekly != null ? $" wk {Math.Round(st.Snapshot.Weekly.Utilization)}%" : "";
                parts.Add($"{st.Config.Name}: {Math.Round(st.Snapshot.Session.Utilization)}%{weekly}");
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
