<p align="center">
  <img src="AgentCake/src/assets/agentcake-profile.png" width="180" alt="AgentCake's scheming agent mascot holding a birthday cake">
</p>

<h1 align="center">Agent Cake's token buffet</h1>

<p align="center">A Windows tray companion for real time AI Agent token usage monitoring</p>

<p align="center">Codex and Claude usage limits implemented</p>

## What it is

AgentCake lives in the Windows notification area and shows the usage you actually have left. It reads local account-limit data written by Codex and Claude Desktop; it does not scrape conversations, use an API key, or send your data anywhere.

Click the tray portrait once to open Details. The Details view has an AgentCake portrait, service icons, current usage text, and compact usage charts. Click a service row to launch that agent.

## Done

- Real Codex weekly-limit reading from `%USERPROFILE%\.codex\sessions`, including its live reset window.
- Real Claude Desktop seven-day and five-hour plan-usage reading from its local app data.
- AgentCake portrait tray icon, with optional stacked usage bars.
- Green, yellow, and pink/red thresholds at 65% and 80% used.
- Details window beside the tray, with centered service icons, a refresh control, and a prominent mascot header.
- Usage charts with a weekly inner pie, Claude five-hour allowance rim, and blue weekly time-to-reset rim.
- One-click launch for Codex and Claude Desktop.
- Optional Claude Code row showing the shared Claude account usage; click it to open Command Prompt and run `claude`.
- Provider visibility switches saved in `%APPDATA%\AgentCake\settings.json`.
- Current-user **Run at login** option.
- Self-contained Windows x64 release builder and one-click current-user installer.

## What's missing

- Real usage readers for ChatGPT, Gemini, GitHub Copilot, Cursor, OpenRouter, and custom providers. Their switches are placeholders only; AgentCake will never invent a usage value.
- Claude numbers are only as fresh as Claude Desktop's last usage fetch. When its HTTP cache has no usage response, AgentCake falls back to the plan-usage history, which has no reset times, so it derives the next resets from observed reset transitions.

## Use it

1. Build and run it:

   ```powershell
   cd AgentCake
   dotnet test tests\AgentCake.Tests.csproj
   dotnet run --project src\AgentCake.csproj
   ```

2. Single-left-click the tray portrait for Details; right-click it for the menu.
3. Use **Providers** to hide services you do not use, enable placeholders, or enable **Show usage bars in tray**.
4. Use **Run at login** when you are happy with the development build location.

## Release and install

Build a self-contained Windows x64 release—no separate .NET installation required:

```powershell
.\Build-Release.ps1
```

Then run `release\install.bat`. It copies AgentCake into `%LOCALAPPDATA%\AgentCake`, registers that installed executable for current-user startup, and launches it. No administrator rights are required.

## Data sources

- **Codex:** live `rate_limits` events in `%USERPROFILE%\.codex\sessions`, including `window_minutes` and reset timestamps.
- **Claude (primary):** the usage responses Claude Desktop has already fetched for its own usage card (`api.anthropic.com/api/oauth/usage` and `claude.ai/api/organizations/{org}/usage`), read from Chromium's on-disk HTTP cache in `Cache\Cache_Data` inside Claude's data folder. These carry exact five-hour and weekly utilization and reset times. AgentCake only reads the cache files; it makes no network request.
- **Claude Desktop (fallback):** `plan-usage-history.json`, which records five-hour (`fh`) and seven-day (`sd`) usage samples. AgentCake finds the newest copy in `%LOCALAPPDATA%\Claude-Data\claude_pzs8sxrjxfjjc` (current releases), `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude` (older MSIX releases) or `%APPDATA%\Claude`. Claude reset times are derived from the latest observed reset for each window.
- **Claude Code:** the live `rate_limits` block (five-hour and seven-day, with reset times) from Claude Code's statusline payload, mirrored to `%APPDATA%\AgentCake\claude-status.json` by `%USERPROFILE%\.claude\agentcake-statusline.js`. Claude Desktop and Claude Code share one account allowance, so the Claude and Claude Code rows show whichever of these sources is freshest.

Open Claude Desktop while signed in at least once so it writes the plan-usage sample. For fresher numbers while using the Claude Code CLI, set its `statusLine` command to `node "%USERPROFILE%\.claude\agentcake-statusline.js"`.

## Provider settings

The tray menu writes `%APPDATA%\AgentCake\settings.json`. The relevant section looks like this:

```json
{
  "showUsageBarsInTray": false,
  "providers": {
    "codex": true,
    "claudeDesktop": true,
    "claudeCode": false,
    "chatGpt": false,
    "gemini": false,
    "gitHubCopilot": false,
    "cursor": false,
    "openRouter": false,
    "customProvider": false
  }
}
```

Turning off Codex or Claude Desktop removes it from Details and from tray-bar mode. Claude Code can be shown as its own row; the remaining entries are saved placeholders for future data readers.

## License

[MIT](LICENSE)
