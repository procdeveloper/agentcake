<p align="center">
  <img src="AgentCake/src/assets/agentcake-profile.png" width="180" alt="AgentCake's scheming agent mascot holding a birthday cake">
</p>

<h1 align="center">AgentCake</h1>

<p align="center">A Windows tray companion for real Codex and Claude usage limits.</p>

## What it is

AgentCake lives in the Windows notification area and shows the weekly usage you actually have left. It reads local, live account-limit data written by Codex and Claude Desktop; it does not estimate from token counts, scrape conversations, use an API key, or send your data anywhere.

Click the tray portrait once to open Details. The Details view has a prominent AgentCake portrait, service icons, current usage text, and compact pie charts. Click a service row to launch that agent.

## Done

- Real Codex weekly-limit reading from `%USERPROFILE%\.codex\sessions`.
- Real Claude Desktop weekly plan-usage reading from its local app data.
- AgentCake portrait tray icon, with optional stacked usage bars.
- Green, yellow, and pink/red thresholds at 65% and 80% used.
- Details window beside the tray, with service icons, pie charts, refresh control, and a prominent mascot header.
- One-click launch for Codex and Claude Desktop.
- Optional Claude Code row that opens Command Prompt and runs `claude`.
- Provider visibility switches saved in `%APPDATA%\AgentCake\settings.json`.
- Current-user **Run at login** option.

## Not done yet

- Real usage readers for Claude Code, ChatGPT, Gemini, GitHub Copilot, Cursor, OpenRouter, and custom providers. Their switches are placeholders only; AgentCake will never invent a usage value.
- A packaged installer and self-contained release build. Development currently runs from the built executable.
- A reliable reset timestamp for Claude Desktop—the local history currently provides the weekly percentage, but not its reset time.

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

## Data sources

- **Codex:** live `rate_limits` events in `%USERPROFILE%\.codex\sessions`.
- **Claude Desktop:** `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\plan-usage-history.json`.

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
