# Claude + Codex Tracker for Windows

A tiny Windows system tray app to track your Claude and OpenAI Codex usage limits — a Windows
take on the macOS menubar tools that show session usage and weekly caps at a glance.

- **Tray icon** uses a crisp 64px source image to show your current session utilization and
  mini progress bar, with Static, Pulse, and RGB treatments plus fixed warning colors near the limit.
- **Click the icon** for a flyout with one card per account: session %, progress bar,
  reset time, and weekly (7-day) usage — plus Opus weekly usage when relevant.
- **Expandable cards**: click a card to see every rate-limit window the API reports —
  session (5h), weekly (7d), Opus weekly, and anything new Anthropic adds — each with its own
  bar and reset time. With multiple accounts, a summary line shows your top usage across all of them.
- **Multiple accounts**: track several Claude accounts side by side; add one with a browser
  sign-in from the Manage tab.
- **Codex usage**: automatically reads your existing local Codex sign-in and shows its primary
  and secondary usage windows plus extra-usage credit balance/status alongside Claude, without
  copying the token into tracker settings.
- **Settings tab**: six themes (Dark, Midnight, OLED, Light, Forest, Rose), accent presets plus
  a full color picker, comfortable/compact density, flyout and progress animations, and tray effects.
- **Auto-refreshes** every 60 seconds; refresh on demand with the ↻ button.
- **Auto-updates**: checks GitHub Releases on startup and every 6 hours, downloads the new
  exe, swaps itself, and restarts. Switch to Manual in Settings if you'd rather it didn't,
  and use "Check now" to update on demand.
- **Start with Windows** toggle in the tray right-click menu.

## Download

Grab `ClaudeTracker.exe` from the [latest release](../../releases/latest).
It's a self-contained single file — no .NET install required. Run it and look for the icon
in the system tray (check the tray overflow ^ if you don't see it). From then on it keeps
itself up to date automatically.

Development builds of every commit are also available as `ClaudeTracker-win-x64` artifacts
on [Actions runs](../../actions).

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

For Codex usage, sign in with the Codex app or CLI. The tracker reads `%USERPROFILE%\.codex\auth.json`
(or `%CODEX_HOME%\auth.json`) in place and queries the same usage endpoint used by
[Codex's official client](https://github.com/openai/codex/blob/9e552e9d15ba52bed7077d5357f3e18e330f8f38/codex-rs/backend-client/src/client/rate_limit_resets.rs).

### Adding more accounts

Open the flyout → **Manage** → give the account a name and click **Sign in with Claude**.
Your browser opens Claude's authorization page — sign in to the account you want to track
(use a private/incognito window if your browser is already logged in to your other account),
approve access, then paste the code it shows back into the app and click **Complete sign-in**.

Alternatively, you can add an account from credentials directly:

- a **path** to another `.credentials.json` (e.g. from a second Claude Code config dir via
  `CLAUDE_CONFIG_DIR`), or
- **paste the JSON** contents of that file directly.

Accounts you add are stored in `%APPDATA%\ClaudeTracker\accounts.json`. Click ✕ on a card and
confirm the prompt to stop tracking an account (this never deletes your actual credentials file).

If a token expires, the app refreshes it using the stored refresh token, the same way
Claude Code does.

## Notes

- Pasted credentials are stored in plain text in `%APPDATA%\ClaudeTracker\accounts.json` —
  the same way Claude Code itself stores `.credentials.json`. Keep that in mind on shared machines.
- Codex credentials remain in Codex's own `auth.json`; the tracker does not copy them into its config.
- Unofficial tool, not affiliated with Anthropic or OpenAI. The usage endpoints may change.
