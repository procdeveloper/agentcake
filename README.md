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
- Optional Claude Code row that opens Command Prompt and runs `claude`.
- Provider visibility switches saved in `%APPDATA%\AgentCake\settings.json`.
- Current-user **Run at login** option.
- Self-contained Windows x64 release builder and one-click current-user installer.

## What's missing

- Real usage readers for Claude Code, ChatGPT, Gemini, GitHub Copilot, Cursor, OpenRouter, and custom providers. Their switches are placeholders only; AgentCake will never invent a usage value.
- Direct access to Claude Desktop's server-provided reset timestamp. Its local history stores five-hour and seven-day samples but not reset times, so AgentCake derives the next Claude resets from observed reset transitions.

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
- **Claude Desktop:** `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\plan-usage-history.json`, which records five-hour (`fh`) and seven-day (`sd`) usage samples. Claude reset times are derived from the latest observed reset for each window.

Open Claude Desktop while signed in at least once so it writes the plan-usage sample.

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

Turning off Codex or Claude Desktop removes it from Details and from tray-bar mode. Claude Code can be shown as a launcher row; the remaining entries are saved placeholders for future data readers.

## License

[MIT](LICENSE)
