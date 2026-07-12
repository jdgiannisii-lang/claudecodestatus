using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeTracker;

public sealed class FlyoutForm : Form
{
    private enum TabPage { Usage, Manage, Settings }

    private static readonly string MonoFamily = PickMonoFamily();

    private static readonly Font FontTitle = MakeMonoFont(14f, FontStyle.Bold);
    private static readonly Font FontSmall = MakeMonoFont(9.5f);
    private static readonly Font FontTiny = MakeMonoFont(8.5f);
    private static readonly Font FontCard = MakeMonoFont(11f, FontStyle.Bold);
    private static readonly Font FontTab = MakeMonoFont(10f);
    private static readonly Font FontInput = MakeMonoFont(9.5f);

    private static readonly Color[] AccentPresets =
    {
        Color.White,
        Color.FromArgb(255, 159, 10),
        Color.FromArgb(10, 132, 255),
        Color.FromArgb(48, 209, 88),
        Color.FromArgb(255, 55, 95),
        Color.FromArgb(191, 90, 242),
    };

    private readonly TrayApplicationContext _owner;
    private TabPage _tab = TabPage.Usage;
    private float _scale = 1f;
    private Theme _t = Theme.Get("Dark");
    private Color _accent = Color.White;
    private bool _dialogOpen;

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _lastFrac = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProgressBarPanel> _bars = new();
    private System.Windows.Forms.Timer? _entranceTimer;
    private System.Windows.Forms.Timer? _barTimer;

    private TextBox? _nameBox;
    private TextBox? _credsBox;
    private TextBox? _codeBox;
    private Label? _manageError;
    private AuthorizeRequest? _pendingAuth;
    private string? _authStatus;

    public DateTimeOffset? HiddenAt { get; private set; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public FlyoutForm(TrayApplicationContext owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = _t.Bg;
        ForeColor = _t.TextPrimary;
        Width = 400;
        Height = 200;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) HideFlyout(); };
        Deactivate += (s, e) => { if (!_dialogOpen) HideFlyout(); };
    }

    /// <summary>Scale a 96-dpi design dimension to the current monitor DPI.</summary>
    private int S(int value) => (int)MathF.Round(value * _scale);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2 (Windows 11; ignored elsewhere).
            int pref = 2;
            DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch
        {
        }
    }

    public void HideFlyout()
    {
        HiddenAt = DateTimeOffset.Now;
        StopEntrance();
        _barTimer?.Stop();
        Hide();
    }

    public void ShowFlyout()
    {
        Rebuild();
        PositionNearTray();

        string mode = _owner.Config.Entrance;
        if (mode.Equals("Slide", StringComparison.OrdinalIgnoreCase))
            StartEntrance(slide: true);
        else if (mode.Equals("Fade", StringComparison.OrdinalIgnoreCase))
            StartEntrance(slide: false);
        else
            Opacity = 1;

        Show();
        Activate();
    }

    public void RefreshView()
    {
        // Never rebuild while the Manage tab is open — it would wipe text the user is typing.
        if (Visible && IsHandleCreated && _tab != TabPage.Manage)
            RebuildDeferred();
    }

    private void RebuildDeferred() =>
        BeginInvoke(new Action(() => { Rebuild(); PositionNearTray(); }));

    private void PositionNearTray()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
    }

    private void StartEntrance(bool slide)
    {
        StopEntrance();
        int targetTop = Top;
        int offset = slide ? S(26) : 0;
        Opacity = 0;
        if (offset != 0) Top = targetTop + offset;

        long started = Environment.TickCount64;
        _entranceTimer = new System.Windows.Forms.Timer { Interval = 10 };
        _entranceTimer.Tick += (s, e) =>
        {
            float t = Math.Min(1f, (Environment.TickCount64 - started) / 170f);
            float ease = 1f - MathF.Pow(1f - t, 3f);
            Opacity = ease;
            if (offset != 0) Top = targetTop + (int)MathF.Round((1f - ease) * offset);
            if (t >= 1f) StopEntrance();
        };
        _entranceTimer.Start();
    }

    private void StopEntrance()
    {
        _entranceTimer?.Stop();
        _entranceTimer?.Dispose();
        _entranceTimer = null;
        Opacity = 1;
    }

    private void StartBarAnimation()
    {
        _barTimer?.Stop();
        _barTimer?.Dispose();
        var bars = _bars.ToList();
        _barTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _barTimer.Tick += (s, e) =>
        {
            bool done = true;
            foreach (var b in bars)
            {
                if (b.IsDisposed) continue;
                double diff = b.Target - b.Fraction;
                if (Math.Abs(diff) > 0.003)
                {
                    b.Fraction += diff * 0.18;
                    b.Invalidate();
                    done = false;
                }
                else if (b.Fraction != b.Target)
                {
                    b.Fraction = b.Target;
                    b.Invalidate();
                }
            }
            if (done) _barTimer?.Stop();
        };
        _barTimer.Start();
    }

    private void Rebuild()
    {
        _scale = DeviceDpi / 96f;
        _t = Theme.Get(_owner.Config.Theme);
        _accent = ParseColor(_owner.Config.AccentColor, Color.White);
        BackColor = _t.Bg;
        ForeColor = _t.TextPrimary;

        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        _bars.Clear();
        _nameBox = null;
        _credsBox = null;
        _codeBox = null;
        _manageError = null;

        Width = S(400);
        int pad = S(_owner.Config.Density.Equals("Compact", StringComparison.OrdinalIgnoreCase) ? 12 : 16);
        int y = pad;

        var title = MakeLabel("Claude + Codex", FontTitle, _t.TextPrimary, Width - pad * 2 - S(40), S(26));
        title.Location = new Point(pad, y);
        Controls.Add(title);

        var refreshBtn = MakeButtonLabel("↻", FontTitle, S(36), S(36));
        refreshBtn.Location = new Point(Width - pad - S(36), y + S(2));
        refreshBtn.Click += (s, e) => _owner.RequestRefresh();
        Controls.Add(refreshBtn);

        string updatedText = _owner.LastUpdated is DateTimeOffset t
            ? "Updated " + t.ToLocalTime().ToString("h:mm tt")
            : "Updating…";
        var updated = MakeLabel(updatedText, FontSmall, _t.TextSecondary, Width - pad * 2 - S(40), S(20));
        updated.Location = new Point(pad, y + S(28));
        Controls.Add(updated);
        y += S(28) + S(20);

        if (_tab == TabPage.Usage && _owner.States.Count > 1)
        {
            string? summary = BuildSummary();
            if (summary != null)
            {
                var sum = MakeLabel(summary, FontTiny, _t.TextSecondary, Width - pad * 2, S(16));
                sum.Location = new Point(pad, y);
                Controls.Add(sum);
                y += S(16);
            }
        }

        y += S(14);
        y = BuildTabs(y);
        y = _tab switch
        {
            TabPage.Manage => BuildManage(y),
            TabPage.Settings => BuildSettings(y),
            _ => BuildUsage(y),
        };

        Height = y + S(6);
        ResumeLayout();

        if (_owner.Config.AnimateBars && _bars.Count > 0)
            StartBarAnimation();
    }

    private string? BuildSummary()
    {
        double maxSession = -1, maxWeekly = -1;
        foreach (var st in _owner.States)
        {
            if (st.Snapshot?.Session != null) maxSession = Math.Max(maxSession, st.Snapshot.Session.Utilization);
            if (st.Snapshot?.Weekly != null) maxWeekly = Math.Max(maxWeekly, st.Snapshot.Weekly.Utilization);
        }
        if (maxSession < 0 && maxWeekly < 0) return null;

        string summary = _owner.States.Count + " accounts";
        if (maxSession >= 0) summary += " · top session " + Math.Round(maxSession) + "%";
        if (maxWeekly >= 0) summary += " · top weekly " + Math.Round(maxWeekly) + "%";
        return summary;
    }

    private int BuildTabs(int y)
    {
        int tabW = S(96), tabH = S(_owner.Config.Density.Equals("Compact", StringComparison.OrdinalIgnoreCase) ? 32 : 36);
        int pillW = tabW * 3 + S(8), pillH = tabH + S(6);
        var pill = new Panel { Width = pillW, Height = pillH, BackColor = _t.PillBg };
        pill.Location = new Point((Width - pillW) / 2, y);
        pill.Region = new Region(RoundedRect(new Rectangle(0, 0, pillW, pillH), pillH / 2));

        var names = new[] { ("Usage", TabPage.Usage), ("Manage", TabPage.Manage), ("Settings", TabPage.Settings) };
        for (int i = 0; i < names.Length; i++)
        {
            var (text, page) = names[i];
            var tab = MakeTab(text, _tab == page, tabW, tabH);
            tab.Location = new Point(S(4) + i * tabW, S(3));
            tab.Click += (s, e) => SwitchTab(page);
            pill.Controls.Add(tab);
        }

        Controls.Add(pill);
        return y + pillH + S(16);
    }

    private void SwitchTab(TabPage page)
    {
        if (_tab == page) return;
        _tab = page;
        RebuildDeferred();
    }

    // ----- Usage tab -----

    private int BuildUsage(int y)
    {
        int pad = S(18);
        var states = _owner.States;

        if (states.Count == 0)
        {
            var none = MakeLabel(
                "No accounts yet.\nSwitch to Manage to add one.",
                FontSmall, _t.TextSecondary, Width - pad * 2, S(48));
            none.Location = new Point(pad, y);
            Controls.Add(none);
            return y + S(48) + S(12);
        }

        foreach (var st in states)
        {
            var card = BuildCard(st);
            card.Location = new Point(pad, y);
            Controls.Add(card);
            y += card.Height + S(12);
        }
        return y + S(4);
    }

    private Panel BuildCard(AccountState st)
    {
        string acctName = st.Config.Name;
        bool expanded = _expanded.Contains(acctName);
        int w = Width - S(36);
        int pad = S(14);
        var card = new Panel { Width = w, BackColor = _t.CardBg, Cursor = Cursors.Hand };
        card.Click += (s, e) => ToggleExpand(acctName);
        int y = pad;
        int rowH = S(24);

        int closeX = w - pad - S(24);
        int closeSlot = st.CanRemove ? S(30) : 0;
        int chevX = w - pad - closeSlot - S(18);
        int pctX = chevX - S(4) - S(64);

        string accountTitle = st.Snapshot?.PlanLabel is string plan
            ? $"{acctName} · {plan}"
            : acctName;
        var name = MakeLabel(accountTitle, FontCard, _t.TextPrimary, pctX - pad - S(4), rowH);
        name.Location = new Point(pad, y);
        name.Cursor = Cursors.Hand;
        name.Click += (s, e) => ToggleExpand(acctName);
        card.Controls.Add(name);

        double sessionPct = st.Snapshot?.Session?.Utilization ?? -1;
        var pct = MakeLabel(sessionPct >= 0 ? Math.Round(sessionPct) + "%" : "–",
            FontCard, SeverityColor(sessionPct), S(64), rowH);
        pct.TextAlign = ContentAlignment.MiddleRight;
        pct.Location = new Point(pctX, y);
        card.Controls.Add(pct);

        var chev = MakeLabel(expanded ? "▾" : "▸", FontSmall, _t.TextSecondary, S(18), rowH);
        chev.TextAlign = ContentAlignment.MiddleCenter;
        chev.Location = new Point(chevX, y);
        chev.Cursor = Cursors.Hand;
        chev.Click += (s, e) => ToggleExpand(acctName);
        card.Controls.Add(chev);

        if (st.CanRemove)
        {
            var close = MakeButtonLabel("✕", FontSmall, S(24), S(24));
            close.Location = new Point(closeX, y);
            close.ForeColor = _t.Danger;
            close.MouseEnter += (s, e) => close.BackColor = _t.DangerBg;
            close.MouseLeave += (s, e) => close.BackColor = _t.ControlBg;
            close.Click += (s, e) => ConfirmRemoveAccount(st);
            card.Controls.Add(close);
        }

        y += rowH + S(10);

        double frac = Math.Clamp(Math.Max(sessionPct, 0) / 100.0, 0, 1);
        AddBar(card, "main/" + acctName, frac, new Point(pad, y), w - pad * 2, S(8));
        y += S(8) + S(12);

        int lineH = S(20);
        if (st.Error != null)
        {
            var err = MakeLabel(st.Error, FontSmall, _t.Warn, w - pad * 2, S(38));
            err.Location = new Point(pad, y);
            card.Controls.Add(err);
            y += S(38) + S(4);
        }
        else if (st.Snapshot == null)
        {
            var loading = MakeLabel("Loading…", FontSmall, _t.TextSecondary, w - pad * 2, lineH);
            loading.Location = new Point(pad, y);
            card.Controls.Add(loading);
            y += lineH + S(4);
        }
        else if (!expanded)
        {
            var session = st.Snapshot.Session;
            if (session is { Utilization: > 0, ResetsAt: not null })
            {
                var reset = MakeLabel(FormatReset(session.ResetsAt.Value), FontSmall, _t.TextSecondary, w - pad * 2, lineH);
                reset.Location = new Point(pad, y);
                reset.Click += (s, e) => ToggleExpand(acctName);
                card.Controls.Add(reset);
                y += lineH + S(4);
            }

            if (st.Snapshot.Weekly != null)
            {
                string weeklyText = "Weekly " + Math.Round(st.Snapshot.Weekly.Utilization) + "%";
                if (st.Snapshot.WeeklyOpus is { Utilization: > 0 })
                    weeklyText += "  ·  Opus " + Math.Round(st.Snapshot.WeeklyOpus.Utilization) + "%";
                var weekly = MakeLabel(weeklyText, FontSmall, _t.TextSecondary, w - pad * 2, lineH);
                weekly.Location = new Point(pad, y);
                weekly.Click += (s, e) => ToggleExpand(acctName);
                card.Controls.Add(weekly);
                y += lineH + S(4);
            }
        }
        else
        {
            foreach (var (label, key, window) in EnumerateWindows(st.Snapshot))
            {
                var rowName = MakeLabel(label, FontSmall, _t.TextSecondary, w - pad * 2 - S(70), S(18));
                rowName.Location = new Point(pad, y);
                card.Controls.Add(rowName);

                var rowPct = MakeLabel(Math.Round(window.Utilization) + "%",
                    FontSmall, SeverityColor(window.Utilization), S(64), S(18));
                rowPct.TextAlign = ContentAlignment.MiddleRight;
                rowPct.Location = new Point(w - pad - S(64), y);
                card.Controls.Add(rowPct);
                y += S(18) + S(4);

                AddBar(card, acctName + "/" + key, Math.Clamp(window.Utilization / 100.0, 0, 1),
                    new Point(pad, y), w - pad * 2, S(5));
                y += S(5) + S(4);

                if (window.ResetsAt is DateTimeOffset resetAt)
                {
                    var reset = MakeLabel(FormatReset(resetAt), FontTiny, _t.TextSecondary, w - pad * 2, S(15));
                    reset.Location = new Point(pad, y);
                    card.Controls.Add(reset);
                    y += S(15);
                }
                y += S(8);
            }
        }

        card.Height = y + pad - S(4);
        card.Region = new Region(RoundedRect(new Rectangle(0, 0, w, card.Height), S(14)));
        return card;
    }

    private void AddBar(Panel parent, string key, double frac, Point location, int width, int height)
    {
        bool animate = _owner.Config.AnimateBars;
        double start = _lastFrac.GetValueOrDefault(key, 0);
        var bar = new ProgressBarPanel
        {
            Width = width,
            Height = height,
            BackColor = parent.BackColor,
            Track = _t.Track,
            Fill = frac >= 0.9 ? _t.Danger : frac >= 0.7 ? _t.Warn : _accent,
            Fraction = animate ? start : frac,
            Target = frac,
            Location = location,
        };
        parent.Controls.Add(bar);
        _bars.Add(bar);
        _lastFrac[key] = frac;
    }

    private void ToggleExpand(string name)
    {
        if (!_expanded.Remove(name)) _expanded.Add(name);
        RebuildDeferred();
    }

    private void ConfirmRemoveAccount(AccountState state)
    {
        _dialogOpen = true;
        try
        {
            var result = MessageBox.Show(
                this,
                $"Are you sure you want to remove ‘{state.Config.Name}’ from Claude Tracker?\n\nThis only removes the account from the tracker; its Claude credentials file will not be deleted.",
                "Remove account?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result == DialogResult.Yes)
                _owner.RemoveAccount(state);
        }
        finally
        {
            _dialogOpen = false;
            Activate();
        }
    }

    private Color SeverityColor(double pct) =>
        pct >= 90 ? _t.Danger : pct >= 70 ? _t.Warn : _t.TextPrimary;

    private static List<(string Label, string Key, UsageWindow Window)> EnumerateWindows(UsageSnapshot s)
    {
        var list = new List<(string, string, UsageWindow)>();
        if (s.Session != null) list.Add((WindowLabel("Session", s.Session, "5h"), "five_hour", s.Session));
        if (s.Weekly != null) list.Add((WindowLabel("Weekly", s.Weekly, "7d"), "seven_day", s.Weekly));
        if (s.WeeklyOpus != null) list.Add(("Opus weekly", "seven_day_opus", s.WeeklyOpus));
        foreach (var extra in s.Extra) list.Add((extra.Label ?? Humanize(extra.Key), extra.Key, extra.Window));
        return list;
    }

    private static string WindowLabel(string name, UsageWindow window, string fallback)
    {
        if (window.WindowSeconds is not int seconds || seconds <= 0) return $"{name} ({fallback})";
        string duration = seconds % 86400 == 0 ? $"{seconds / 86400}d"
            : seconds % 3600 == 0 ? $"{seconds / 3600}h"
            : $"{Math.Max(1, seconds / 60)}m";
        return $"{name} ({duration})";
    }

    private static string Humanize(string key)
    {
        string text = key.Replace('_', ' ').Trim();
        return text.Length == 0 ? key : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // ----- Manage tab -----

    private int BuildManage(int y)
    {
        int pad = S(18);
        int w = Width - pad * 2;
        int labelH = S(20);

        var heading = MakeLabel("Add an account", FontCard, _t.TextPrimary, w, S(24));
        heading.Location = new Point(pad, y);
        Controls.Add(heading);
        y += S(30);

        var codexHint = MakeLabel(
            CodexUsageClient.HasLocalAuth()
                ? "Codex usage is detected automatically from your local Codex sign-in."
                : "Sign in with Codex locally to add its usage automatically.",
            FontSmall, _t.TextSecondary, w, S(36));
        codexHint.Location = new Point(pad, y);
        Controls.Add(codexHint);
        y += S(36) + S(8);

        var nameHint = MakeLabel("Name (e.g. Work)", FontSmall, _t.TextSecondary, w, labelH);
        nameHint.Location = new Point(pad, y);
        Controls.Add(nameHint);
        y += labelH + S(2);

        _nameBox = MakeTextBox(w, false);
        _nameBox.Location = new Point(pad, y);
        Controls.Add(_nameBox);
        y += _nameBox.Height + S(12);

        var signIn = MakeButtonLabel("Sign in with Claude", FontTab, w, S(36));
        signIn.Location = new Point(pad, y);
        signIn.Click += (s, e) => StartSignIn();
        Controls.Add(signIn);
        y += S(36) + S(10);

        var hostedHint = MakeLabel("You'll sign in on claude.ai in your browser. Claude Tracker never asks for your Anthropic password.", FontSmall, _t.TextSecondary, w, S(36));
        hostedHint.Location = new Point(pad, y);
        Controls.Add(hostedHint);
        y += S(36) + S(8);

        if (_pendingAuth != null)
        {
            var authHint = MakeLabel(
                _authStatus ?? "Browser opened. Sign in on claude.ai, approve access, then paste the completion code shown there.",
                FontSmall, _t.TextSecondary, w, S(48));
            authHint.Location = new Point(pad, y);
            Controls.Add(authHint);
            y += S(48) + S(6);

            int recoveryW = (w - S(8)) / 2;
            var reopen = MakeButtonLabel("Open sign-in again", FontTiny, recoveryW, S(32));
            reopen.Location = new Point(pad, y);
            reopen.Click += (s, e) => OpenPendingSignIn();
            Controls.Add(reopen);
            var copy = MakeButtonLabel("Copy authorization URL", FontTiny, recoveryW, S(32));
            copy.Location = new Point(pad + recoveryW + S(8), y);
            copy.Click += (s, e) => { if (_pendingAuth != null) Clipboard.SetText(_pendingAuth.Url); };
            Controls.Add(copy);
            y += S(32) + S(8);

            var codeLabel = MakeLabel("Paste the completion code (code#state)", FontSmall, _t.TextSecondary, w, labelH);
            codeLabel.Location = new Point(pad, y);
            Controls.Add(codeLabel);
            y += labelH + S(2);

            _codeBox = MakeTextBox(w, false);
            _codeBox.Location = new Point(pad, y);
            Controls.Add(_codeBox);
            y += _codeBox.Height + S(8);

            var complete = MakeButtonLabel("Complete sign-in", FontTab, w, S(34));
            complete.Location = new Point(pad, y);
            complete.Click += (s, e) => CompleteSignIn();
            Controls.Add(complete);
            y += S(34) + S(10);
        }

        var credsHint = MakeLabel("Or use a Claude Code credentials file or JSON", FontSmall, _t.TextSecondary, w, labelH);
        credsHint.Location = new Point(pad, y);
        Controls.Add(credsHint);
        y += labelH + S(2);

        _credsBox = MakeTextBox(w, true);
        _credsBox.Location = new Point(pad, y);
        Controls.Add(_credsBox);
        y += _credsBox.Height + S(10);

        var add = MakeButtonLabel("Add from file / JSON", FontTab, w, S(34));
        add.Location = new Point(pad, y);
        add.Click += (s, e) => DoAdd();
        Controls.Add(add);
        y += S(34) + S(8);

        _manageError = MakeLabel("", FontSmall, _t.Warn, w, S(36));
        _manageError.Location = new Point(pad, y);
        Controls.Add(_manageError);
        y += S(36);

        return y + S(8);
    }

    private void StartSignIn()
    {
        _pendingAuth = AnthropicUsageClient.CreateAuthorizeRequest();
        _authStatus = null;
        OpenPendingSignIn();
    }

    private void OpenPendingSignIn()
    {
        if (_pendingAuth == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_pendingAuth.Url) { UseShellExecute = true });
            _authStatus = "Browser opened. Sign in on claude.ai, approve access, then paste the completion code shown there.";
        }
        catch
        {
            _authStatus = "The browser did not open. Use Open sign-in again or copy the authorization URL.";
        }
        RebuildDeferred();
    }

    private async void CompleteSignIn()
    {
        var auth = _pendingAuth;
        var codeBox = _codeBox;
        var errLabel = _manageError;
        var nameBox = _nameBox;
        if (auth == null || codeBox == null || errLabel == null) return;

        string pasted = codeBox.Text.Trim();
        if (pasted.Length == 0)
        {
            errLabel.Text = "Paste the completion code from the browser first.";
            return;
        }

        var completion = OAuthCompletionParser.Parse(pasted);
        if (!completion.HasCode)
        {
            errLabel.Text = "Paste the completion code from the browser first.";
            return;
        }
        if (!OAuthCompletionParser.MatchesPendingState(completion, auth.State))
        {
            errLabel.Text = "That completion code does not match this sign-in attempt. Start sign-in again.";
            return;
        }

        errLabel.Text = "Signing in…";
        try
        {
            var tokens = await AnthropicUsageClient.ExchangeCodeAsync(pasted, auth.State, auth.Verifier);
            if (errLabel.IsDisposed) return;
            if (tokens == null)
            {
                errLabel.Text = "Sign-in failed. Check the code and try again.";
                return;
            }

            string? error = _owner.AddAccountFromTokens(nameBox?.Text ?? "", tokens);
            if (error != null)
            {
                errLabel.Text = error;
                return;
            }

            _pendingAuth = null;
            _tab = TabPage.Usage;
            Rebuild();
            PositionNearTray();
        }
        catch
        {
            if (!errLabel.IsDisposed) errLabel.Text = "Sign-in failed. Check the code and try again.";
        }
    }

    private void DoAdd()
    {
        if (_nameBox == null || _credsBox == null || _manageError == null) return;
        string? error = _owner.AddAccount(_nameBox.Text, _credsBox.Text);
        if (error != null)
        {
            _manageError.Text = error;
            return;
        }
        _tab = TabPage.Usage;
        RebuildDeferred();
    }

    // ----- Settings tab -----

    private int BuildSettings(int y)
    {
        int pad = S(18);
        int w = Width - pad * 2;
        int labelH = S(20);
        int gap = S(8);

        var themeLbl = MakeLabel("Theme", FontSmall, _t.TextSecondary, w, labelH);
        themeLbl.Location = new Point(pad, y);
        Controls.Add(themeLbl);
        y += labelH + S(4);

        int themeW = (w - gap * 2) / 3;
        int x = pad;
        for (int i = 0; i < Theme.All.Length; i++)
        {
            var theme = Theme.All[i];
            var name = theme.Name;
            var choice = MakeChoice(name, name.Equals(_owner.Config.Theme, StringComparison.OrdinalIgnoreCase), themeW, S(30));
            choice.Location = new Point(pad + (i % 3) * (themeW + gap), y + (i / 3) * (S(30) + gap));
            choice.Click += (s, e) =>
            {
                _owner.Config.Theme = name;
                _owner.SaveConfig();
                RebuildDeferred();
            };
            Controls.Add(choice);
        }
        y += S(68) + S(14);

        var accentLbl = MakeLabel("Accent color", FontSmall, _t.TextSecondary, w, labelH);
        accentLbl.Location = new Point(pad, y);
        Controls.Add(accentLbl);
        y += labelH + S(4);

        x = pad;
        foreach (var preset in AccentPresets)
        {
            var color = preset;
            var swatch = MakeSwatch(color, color.ToArgb() == _accent.ToArgb());
            swatch.Location = new Point(x, y);
            swatch.Click += (s, e) => SetAccent(color);
            Controls.Add(swatch);
            x += S(28) + gap;
        }

        var custom = MakeButtonLabel("…", FontTab, S(28), S(28));
        custom.Location = new Point(x, y);
        custom.Click += (s, e) => PickCustomAccent();
        Controls.Add(custom);
        y += S(28) + S(14);

        var densityLbl = MakeLabel("Density", FontSmall, _t.TextSecondary, w, labelH);
        densityLbl.Location = new Point(pad, y);
        Controls.Add(densityLbl);
        y += labelH + S(4);
        int densityW = (w - gap) / 2;
        x = pad;
        foreach (var density in new[] { "Comfortable", "Compact" })
        {
            var value = density;
            var choice = MakeChoice(density, density.Equals(_owner.Config.Density, StringComparison.OrdinalIgnoreCase), densityW, S(30));
            choice.Location = new Point(x, y);
            choice.Click += (s, e) => { _owner.Config.Density = value; _owner.SaveConfig(); RebuildDeferred(); };
            Controls.Add(choice);
            x += densityW + gap;
        }
        y += S(30) + S(14);

        var animLbl = MakeLabel("Flyout animation", FontSmall, _t.TextSecondary, w, labelH);
        animLbl.Location = new Point(pad, y);
        Controls.Add(animLbl);
        y += labelH + S(4);

        int choiceW = (w - gap * 2) / 3;
        x = pad;
        foreach (var mode in new[] { "Slide", "Fade", "Off" })
        {
            var value = mode;
            var choice = MakeChoice(mode, mode.Equals(_owner.Config.Entrance, StringComparison.OrdinalIgnoreCase), choiceW, S(30));
            choice.Location = new Point(x, y);
            choice.Click += (s, e) =>
            {
                _owner.Config.Entrance = value;
                _owner.SaveConfig();
                RebuildDeferred();
            };
            Controls.Add(choice);
            x += choiceW + gap;
        }
        y += S(30) + S(14);

        var trayLbl = MakeLabel("Tray treatment", FontSmall, _t.TextSecondary, w, labelH);
        trayLbl.Location = new Point(pad, y);
        Controls.Add(trayLbl);
        y += labelH + S(4);
        int trayW = (w - gap * 2) / 3;
        x = pad;
        foreach (var treatment in new[] { "Static", "Pulse", "RGB" })
        {
            var value = treatment;
            var choice = MakeChoice(treatment, treatment.Equals(_owner.Config.TrayTreatment, StringComparison.OrdinalIgnoreCase), trayW, S(30));
            choice.Location = new Point(x, y);
            choice.Click += (s, e) => { _owner.Config.TrayTreatment = value; _owner.SaveConfig(); RebuildDeferred(); };
            Controls.Add(choice);
            x += trayW + gap;
        }
        y += S(30) + S(4);
        var trayHint = MakeLabel("Warning and danger colors always stay visible", FontTiny, _t.TextSecondary, w, labelH);
        trayHint.Location = new Point(pad, y);
        Controls.Add(trayHint);
        y += labelH + S(10);

        var barsLbl = MakeLabel("Progress bar animation", FontSmall, _t.TextSecondary, w, labelH);
        barsLbl.Location = new Point(pad, y);
        Controls.Add(barsLbl);
        y += labelH + S(4);

        int halfW = (w - gap) / 2;
        x = pad;
        foreach (var on in new[] { true, false })
        {
            var value = on;
            var choice = MakeChoice(on ? "Animated" : "Instant", _owner.Config.AnimateBars == on, halfW, S(30));
            choice.Location = new Point(x, y);
            choice.Click += (s, e) =>
            {
                _owner.Config.AnimateBars = value;
                _owner.SaveConfig();
                RebuildDeferred();
            };
            Controls.Add(choice);
            x += halfW + gap;
        }
        y += S(30) + S(14);

        var updLbl = MakeLabel("Updates", FontSmall, _t.TextSecondary, w, labelH);
        updLbl.Location = new Point(pad, y);
        Controls.Add(updLbl);
        y += labelH + S(4);

        x = pad;
        foreach (var on in new[] { true, false })
        {
            var value = on;
            var choice = MakeChoice(on ? "Automatic" : "Manual", _owner.Config.AutoUpdate == on, halfW, S(30));
            choice.Location = new Point(x, y);
            choice.Click += (s, e) =>
            {
                _owner.Config.AutoUpdate = value;
                _owner.SaveConfig();
                RebuildDeferred();
            };
            Controls.Add(choice);
            x += halfW + gap;
        }
        y += S(30) + S(6);

        string status = "v" + UpdateManager.CurrentVersion +
            (UpdateManager.Status.Length > 0 ? " · " + UpdateManager.Status : "");
        var verLbl = MakeLabel(status, FontTiny, _t.TextSecondary, w - S(110), S(30));
        verLbl.Location = new Point(pad, y);
        Controls.Add(verLbl);

        var checkNow = MakeButtonLabel("Check now", FontTiny, S(104), S(30));
        checkNow.Location = new Point(pad + w - S(104), y);
        checkNow.Click += async (s, e) => await _owner.RunUpdateCheckAsync(manual: true);
        Controls.Add(checkNow);
        y += S(30);

        return y + S(10);
    }

    private void SetAccent(Color color)
    {
        _owner.Config.AccentColor = ColorTranslator.ToHtml(color);
        _owner.SaveConfig();
        RebuildDeferred();
    }

    private void PickCustomAccent()
    {
        _dialogOpen = true;
        try
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = _accent };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                SetAccent(dlg.Color);
        }
        finally
        {
            _dialogOpen = false;
            Activate();
        }
    }

    // ----- helpers -----

    private static Color ParseColor(string html, Color fallback)
    {
        try { return ColorTranslator.FromHtml(html); }
        catch { return fallback; }
    }

    private static string FormatReset(DateTimeOffset resetsAt)
    {
        var local = resetsAt.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return "Reset " + local.ToString("h:mm tt");
        if (local.Date == today.AddDays(1)) return "Reset " + local.ToString("h:mm tt") + " tomorrow";
        return "Reset " + local.ToString("ddd h:mm tt");
    }

    private static Label MakeLabel(string text, Font font, Color color, int width, int height) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = false,
        Width = width,
        Height = height,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private Label MakeButtonLabel(string text, Font font, int width, int height)
    {
        var l = new Label
        {
            Text = text,
            Font = font,
            ForeColor = _t.TextPrimary,
            BackColor = _t.ControlBg,
            AutoSize = false,
            Width = width,
            Height = height,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        l.Region = new Region(RoundedRect(new Rectangle(0, 0, width, height), Math.Min(height / 2, S(17))));
        l.MouseEnter += (s, e) => l.BackColor = _t.ControlHover;
        l.MouseLeave += (s, e) => l.BackColor = _t.ControlBg;
        return l;
    }

    private Label MakeChoice(string text, bool selected, int width, int height)
    {
        var l = new Label
        {
            Text = text,
            Font = FontTiny,
            AutoSize = false,
            Width = width,
            Height = height,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            ForeColor = selected ? _t.Focus : _t.TextSecondary,
            BackColor = selected ? _t.PillSelected : _t.ControlBg,
        };
        l.Region = new Region(RoundedRect(new Rectangle(0, 0, width, height), Math.Min(height / 2, S(15))));
        if (!selected)
        {
            l.MouseEnter += (s, e) => l.BackColor = _t.ControlHover;
            l.MouseLeave += (s, e) => l.BackColor = _t.ControlBg;
        }
        return l;
    }

    private Label MakeSwatch(Color color, bool selected)
    {
        var l = new Label
        {
            Text = selected ? "✓" : "",
            Font = FontTiny,
            ForeColor = PerceivedBrightness(color) > 140 ? Color.Black : Color.White,
            BackColor = color,
            AutoSize = false,
            Width = S(28),
            Height = S(28),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        l.Region = new Region(RoundedRect(new Rectangle(0, 0, S(28), S(28)), S(9)));
        return l;
    }

    private static int PerceivedBrightness(Color c) =>
        (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);

    private Label MakeTab(string text, bool selected, int width, int height)
    {
        var l = new Label
        {
            Text = text,
            Font = FontTab,
            AutoSize = false,
            Width = width,
            Height = height,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            ForeColor = selected ? _t.TextPrimary : _t.TextSecondary,
            BackColor = selected ? _t.PillSelected : _t.PillBg,
        };
        if (selected)
            l.Region = new Region(RoundedRect(new Rectangle(0, 0, width, height), height / 2));
        return l;
    }

    private TextBox MakeTextBox(int width, bool multiline) => new()
    {
        Width = width,
        Multiline = multiline,
        Height = multiline ? S(64) : S(28),
        Font = FontInput,
        BackColor = _t.CardBg,
        ForeColor = _t.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static string PickMonoFamily()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" })
                if (names.Contains(candidate)) return candidate;
        }
        catch
        {
        }
        return FontFamily.GenericMonospace.Name;
    }

    private static Font MakeMonoFont(float size, FontStyle style = FontStyle.Regular) =>
        new(MonoFamily, size, style);

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ProgressBarPanel : Panel
    {
        public double Fraction { get; set; }
        public double Target { get; set; }
        public Color Track { get; set; } = Color.Gray;
        public Color Fill { get; set; } = Color.White;

        public ProgressBarPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var track = new SolidBrush(Track))
            using (var trackPath = RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2))
            {
                g.FillPath(track, trackPath);
            }

            int fillW = (int)Math.Round(Width * Fraction);
            if (fillW > 0)
            {
                fillW = Math.Max(fillW, Height);
                using var fill = new SolidBrush(Fill);
                using var fillPath = RoundedRect(new Rectangle(0, 0, fillW, Height), Height / 2);
                g.FillPath(fill, fillPath);
            }
        }
    }
}
