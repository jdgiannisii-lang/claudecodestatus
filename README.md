# Claude Tracker for Windows

A tiny Windows system tray app to track your Claude usage limits — a Windows take on the
macOS menubar tools that show your 5-hour session usage and weekly cap at a glance.

- **Tray icon** shows your current session utilization (with a mini progress bar), color-shifting
  as you approach the limit.
- **Click the icon** for a dark flyout with one card per account: session %, progress bar,
  reset time, and weekly (7-day) usage — plus Opus weekly usage when relevant.
- **Multiple accounts**: track several Claude accounts side by side.
- **Auto-refreshes** every 60 seconds; refresh on demand with the ↻ button.
- **Start with Windows** toggle in the tray right-click menu.

## Download

Grab `ClaudeTracker.exe` from the latest [Actions run](../../actions) artifact
(`ClaudeTracker-win-x64`), or from [Releases](../../releases) once a version tag is pushed.
It's a self-contained single file — no .NET install required. Run it and look for the icon
in the system tray (check the tray overflow ^ if you don't see it).

## Build it yourself

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0):

```
dotnet publish src/ClaudeTracker/ClaudeTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## How it works

The app reads the OAuth credentials that **Claude Code** stores at
`%USERPROFILE%\.claude\.credentials.json` and queries Anthropic's usage endpoint
(`https://api.anthropic.com/api/oauth/usage`) for your rate-limit utilization — the same
data Claude Code's `/usage` command shows. Nothing is scraped or estimated; it's your
account's real limit state.

So the only prerequisite is having signed in to [Claude Code](https://claude.com/claude-code)
on the machine at least once. Your default sign-in is picked up automatically on first run.

### Adding more accounts

Open the flyout → **Manage** → give the account a name and either:

- a **path** to another `.credentials.json` (e.g. from a second Claude Code config dir via
  `CLAUDE_CONFIG_DIR`), or
- **paste the JSON** contents of that file directly.

Accounts you add are stored in `%APPDATA%\ClaudeTracker\accounts.json`. Click ✕ on a card to
stop tracking an account (this never deletes your actual credentials file).

If a token expires, the app refreshes it using the stored refresh token, the same way
Claude Code does.

## Notes

- Pasted credentials are stored in plain text in `%APPDATA%\ClaudeTracker\accounts.json` —
  the same way Claude Code itself stores `.credentials.json`. Keep that in mind on shared machines.
- Unofficial tool, not affiliated with Anthropic. The usage endpoint is undocumented and may change.
