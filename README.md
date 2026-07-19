# AgentCake

A Windows notification-area widget for your **real weekly remaining usage** in Codex and Claude Code.

The tray icon has two stacked percentage rows: Codex on top and Claude below. Each row shows the weekly percentage remaining over a used-progress background:

- normal below 65% used
- yellow from 65% used
- light pink/red from 80% used

Hover for service names and percentages; double-click for reset details.

## Data sources

- **Codex**: local live `rate_limits` events in `%USERPROFILE%\.codex\sessions`.
- **Claude Code**: a local `statusLine` hook writes Claude's live status payload to `%APPDATA%\AgentCake\claude-status.json`.

No token-estimation, transcript accounting, API key, or network request is used.

## Install Claude capture

Run once from PowerShell:

```powershell
.\Install-ClaudeStatusHook.ps1
```

It copies the hook into `%USERPROFILE%\.claude` and repairs/configures Claude Code's `statusLine` setting. If the existing settings file is invalid JSON, it is backed up before replacement.

## Build

```powershell
cd AgentCake
dotnet test tests\AgentCake.Tests.csproj
dotnet run --project src\AgentCake.csproj
```

Use the tray menu to enable **Run at login**.