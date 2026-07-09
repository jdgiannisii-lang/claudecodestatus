using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeTracker;

public sealed class FlyoutForm : Form
{
    private static readonly string MonoFamily = PickMonoFamily();

    private static readonly Color Bg = Color.FromArgb(16, 16, 16);
    private static readonly Color CardBg = Color.FromArgb(28, 28, 30);
    private static readonly Color ControlBg = Color.FromArgb(44, 44, 46);
    private static readonly Color ControlHover = Color.FromArgb(60, 60, 64);
    private static readonly Color PillBg = Color.FromArgb(30, 30, 32);
    private static readonly Color PillSelected = Color.FromArgb(72, 72, 76);
    private static readonly Color TrackColor = Color.FromArgb(58, 58, 60);
    private static readonly Color TextPrimary = Color.FromArgb(242, 242, 247);
    private static readonly Color TextSecondary = Color.FromArgb(152, 152, 157);
    private static readonly Color WarnColor = Color.FromArgb(255, 159, 10);

    private static readonly Font FontTitle = MakeMonoFont(14f, FontStyle.Bold);
    private static readonly Font FontSmall = MakeMonoFont(9.5f);
    private static readonly Font FontCard = MakeMonoFont(11f, FontStyle.Bold);
    private static readonly Font FontTab = MakeMonoFont(10f);
    private static readonly Font FontInput = MakeMonoFont(9.5f);

    private readonly TrayApplicationContext _owner;
    private bool _showManage;
    private TextBox? _nameBox;
    private TextBox? _credsBox;
    private Label? _manageError;

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
        BackColor = Bg;
        ForeColor = TextPrimary;
        Width = 380;
        Height = 200;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) HideFlyout(); };
        Deactivate += (s, e) => HideFlyout();
    }

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
        Hide();
    }

    public void ShowFlyout()
    {
        Rebuild();
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        Show();
        Activate();
    }

    public void RefreshView()
    {
        if (Visible && IsHandleCreated)
            BeginInvoke(new Action(Rebuild));
    }

    private void Rebuild()
    {
        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        _nameBox = null;
        _credsBox = null;
        _manageError = null;

        int pad = 18;
        int y = pad;

        var title = MakeLabel("Claude Tracker", FontTitle, TextPrimary, Width - pad * 2 - 40, 24);
        title.Location = new Point(pad, y);
        Controls.Add(title);

        var refreshBtn = MakeButtonLabel("↻", FontTitle, 36, 36);
        refreshBtn.Location = new Point(Width - pad - 36, y + 2);
        refreshBtn.Click += (s, e) => _owner.RequestRefresh();
        Controls.Add(refreshBtn);

        string updatedText = _owner.LastUpdated is DateTimeOffset t
            ? "Updated " + t.ToLocalTime().ToString("h:mm tt")
            : "Updating…";
        var updated = MakeLabel(updatedText, FontSmall, TextSecondary, Width - pad * 2 - 40, 18);
        updated.Location = new Point(pad, y + 26);
        Controls.Add(updated);

        y += 26 + 18 + 16;
        y = BuildTabs(y);
        y = _showManage ? BuildManage(y) : BuildUsage(y);

        Height = y + 6;
        ResumeLayout();

        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Location.X, wa.Bottom - Height - 12);
    }

    private int BuildTabs(int y)
    {
        int pillW = 220, pillH = 34;
        var pill = new Panel { Width = pillW, Height = pillH, BackColor = PillBg };
        pill.Location = new Point((Width - pillW) / 2, y);
        pill.Region = new Region(RoundedRect(new Rectangle(0, 0, pillW, pillH), pillH / 2));

        var usage = MakeTab("Usage", !_showManage);
        usage.Location = new Point(4, 3);
        usage.Click += (s, e) => SwitchTab(false);
        pill.Controls.Add(usage);

        var manage = MakeTab("Manage", _showManage);
        manage.Location = new Point(pillW / 2, 3);
        manage.Click += (s, e) => SwitchTab(true);
        pill.Controls.Add(manage);

        Controls.Add(pill);
        return y + pillH + 16;
    }

    private void SwitchTab(bool manage)
    {
        if (_showManage == manage) return;
        _showManage = manage;
        BeginInvoke(new Action(Rebuild));
    }

    private int BuildUsage(int y)
    {
        int pad = 18;
        var states = _owner.States;

        if (states.Count == 0)
        {
            var none = MakeLabel(
                "No accounts yet.\nSwitch to Manage to add one.",
                FontSmall, TextSecondary, Width - pad * 2, 44);
            none.Location = new Point(pad, y);
            Controls.Add(none);
            return y + 44 + 12;
        }

        foreach (var st in states)
        {
            var card = BuildCard(st);
            card.Location = new Point(pad, y);
            Controls.Add(card);
            y += card.Height + 12;
        }
        return y + 4;
    }

    private Panel BuildCard(AccountState st)
    {
        int w = Width - 36;
        int pad = 14;
        var card = new Panel { Width = w, BackColor = CardBg };
        int y = pad;

        var name = MakeLabel(st.Config.Name, FontCard, TextPrimary, w - 130, 22);
        name.Location = new Point(pad, y);
        card.Controls.Add(name);

        var close = MakeButtonLabel("✕", FontSmall, 24, 24);
        close.Location = new Point(w - pad - 24, y - 1);
        close.Click += (s, e) => _owner.RemoveAccount(st);
        card.Controls.Add(close);

        string pctText = st.Snapshot?.Session != null
            ? Math.Round(st.Snapshot.Session.Utilization) + "%"
            : "–";
        var pct = MakeLabel(pctText, FontCard, TextPrimary, 64, 22);
        pct.TextAlign = ContentAlignment.MiddleRight;
        pct.Location = new Point(w - pad - 24 - 6 - 64, y);
        card.Controls.Add(pct);

        y += 22 + 10;

        double frac = Math.Clamp((st.Snapshot?.Session?.Utilization ?? 0) / 100.0, 0, 1);
        var bar = new ProgressBarPanel { Width = w - pad * 2, Height = 8, Fraction = frac, BackColor = CardBg };
        bar.Location = new Point(pad, y);
        card.Controls.Add(bar);
        y += 8 + 12;

        if (st.Error != null)
        {
            var err = MakeLabel(st.Error, FontSmall, WarnColor, w - pad * 2, 34);
            err.Location = new Point(pad, y);
            card.Controls.Add(err);
            y += 34 + 4;
        }
        else if (st.Snapshot != null)
        {
            var session = st.Snapshot.Session;
            if (session is { Utilization: > 0, ResetsAt: not null })
            {
                var reset = MakeLabel(FormatReset(session.ResetsAt.Value), FontSmall, TextSecondary, w - pad * 2, 18);
                reset.Location = new Point(pad, y);
                card.Controls.Add(reset);
                y += 18 + 4;
            }

            if (st.Snapshot.Weekly != null)
            {
                string weeklyText = "Weekly " + Math.Round(st.Snapshot.Weekly.Utilization) + "%";
                if (st.Snapshot.WeeklyOpus is { Utilization: > 0 })
                    weeklyText += "  ·  Opus " + Math.Round(st.Snapshot.WeeklyOpus.Utilization) + "%";
                var weekly = MakeLabel(weeklyText, FontSmall, TextSecondary, w - pad * 2, 18);
                weekly.Location = new Point(pad, y);
                card.Controls.Add(weekly);
                y += 18 + 4;
            }
        }
        else
        {
            var loading = MakeLabel("Loading…", FontSmall, TextSecondary, w - pad * 2, 18);
            loading.Location = new Point(pad, y);
            card.Controls.Add(loading);
            y += 18 + 4;
        }

        card.Height = y + pad - 4;
        card.Region = new Region(RoundedRect(new Rectangle(0, 0, w, card.Height), 14));
        return card;
    }

    private int BuildManage(int y)
    {
        int pad = 18;
        int w = Width - pad * 2;

        var heading = MakeLabel("Add an account", FontCard, TextPrimary, w, 22);
        heading.Location = new Point(pad, y);
        Controls.Add(heading);
        y += 28;

        var nameHint = MakeLabel("Name", FontSmall, TextSecondary, w, 16);
        nameHint.Location = new Point(pad, y);
        Controls.Add(nameHint);
        y += 18;

        _nameBox = MakeTextBox(w, false);
        _nameBox.Location = new Point(pad, y);
        Controls.Add(_nameBox);
        y += _nameBox.Height + 10;

        var credsHint = MakeLabel("Path to .credentials.json, or paste its JSON", FontSmall, TextSecondary, w, 16);
        credsHint.Location = new Point(pad, y);
        Controls.Add(credsHint);
        y += 18;

        _credsBox = MakeTextBox(w, true);
        _credsBox.Location = new Point(pad, y);
        Controls.Add(_credsBox);
        y += _credsBox.Height + 12;

        var add = MakeButtonLabel("Add account", FontTab, w, 34);
        add.Location = new Point(pad, y);
        add.Click += (s, e) => DoAdd();
        Controls.Add(add);
        y += 34 + 8;

        _manageError = MakeLabel("", FontSmall, WarnColor, w, 32);
        _manageError.Location = new Point(pad, y);
        Controls.Add(_manageError);
        y += 32 + 4;

        var hint = MakeLabel(
            "Your Claude Code sign-in (%USERPROFILE%\\.claude\\.credentials.json) is added automatically on first run.",
            FontSmall, TextSecondary, w, 46);
        hint.Location = new Point(pad, y);
        Controls.Add(hint);
        y += 46;

        return y + 8;
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
        _showManage = false;
        BeginInvoke(new Action(Rebuild));
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

    private static Label MakeButtonLabel(string text, Font font, int width, int height)
    {
        var l = new Label
        {
            Text = text,
            Font = font,
            ForeColor = TextPrimary,
            BackColor = ControlBg,
            AutoSize = false,
            Width = width,
            Height = height,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        l.Region = new Region(RoundedRect(new Rectangle(0, 0, width, height), Math.Min(height / 2, 17)));
        l.MouseEnter += (s, e) => l.BackColor = ControlHover;
        l.MouseLeave += (s, e) => l.BackColor = ControlBg;
        return l;
    }

    private static Label MakeTab(string text, bool selected)
    {
        int w = 106, h = 28;
        var l = new Label
        {
            Text = text,
            Font = FontTab,
            AutoSize = false,
            Width = w,
            Height = h,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            ForeColor = selected ? TextPrimary : TextSecondary,
            BackColor = selected ? PillSelected : PillBg,
        };
        if (selected)
            l.Region = new Region(RoundedRect(new Rectangle(0, 0, w, h), h / 2));
        return l;
    }

    private static TextBox MakeTextBox(int width, bool multiline) => new()
    {
        Width = width,
        Multiline = multiline,
        Height = multiline ? 64 : 26,
        Font = FontInput,
        BackColor = CardBg,
        ForeColor = TextPrimary,
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

        public ProgressBarPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var track = new SolidBrush(TrackColor))
            using (var trackPath = RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2))
            {
                g.FillPath(track, trackPath);
            }

            int fillW = (int)Math.Round(Width * Fraction);
            if (fillW > 0)
            {
                fillW = Math.Max(fillW, Height);
                using var fill = new SolidBrush(Color.White);
                using var fillPath = RoundedRect(new Rectangle(0, 0, fillW, Height), Height / 2);
                g.FillPath(fill, fillPath);
            }
        }
    }
}
