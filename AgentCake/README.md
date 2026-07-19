# AgentCake

A Windows notification-area widget for your **real weekly remaining usage** in Codex and Claude Desktop.

The tray icon has two stacked percentage rows: Codex on top and Claude below. Each row shows the weekly percentage remaining over a used-progress background:

- normal below 65% used
- yellow from 65% used
- light pink/red from 80% used

Hover for service names and percentages; double-click for reset details.

## Data sources

- **Codex**: local live `rate_limits` events in `%USERPROFILE%\.codex\sessions`.
- **Claude Desktop**: its locally maintained plan-usage history at `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\plan-usage-history.json`.

No token-estimation, transcript accounting, API key, or network request is used.

Open Claude Desktop while signed in at least once; it writes the weekly plan-usage sample AgentCake reads. No Claude Code hook is needed.

## Build

```powershell
cd AgentCake
dotnet test tests\AgentCake.Tests.csproj
dotnet run --project src\AgentCake.csproj
```

Use the tray menu to enable **Run at login**.
