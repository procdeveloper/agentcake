# AgentCake

A tiny Windows system-tray app that shows your Claude Code usage as two bars:

- **Left bar** — % of the current **5-hour** rate-limit window
- **Right bar** — % of your **weekly** limit

Each bar is green below 60%, amber 60–85%, red above 85%. Double-click the tray
icon for exact numbers and reset times; right-click for the menu.

It reads the usage data Claude Code already writes to disk
(`%USERPROFILE%\.claude\projects\**\*.jsonl`) — the same files `ccusage` parses.
**No API key, no network, no Node** — just a self-contained .NET WinForms app.
Works on Pro/Max subscriptions and on API billing alike.

## Requirements

- Windows 10/11
- .NET 10 SDK (installed) — or Visual Studio with the **.NET desktop development** workload
- Claude Code, having run at least once (so there are logs under `~/.claude/projects`)

> Targets `net10.0-windows` to match the installed .NET 10 SDK. Retarget in `src/AgentCake.csproj` if needed.
> `net9.0-windows` / `net10.0-windows` if you prefer; no other change needed.

## Build & run

In Visual Studio: open `AgentCake.sln`, set `AgentCake` as startup, press **F5**.

From a terminal:

    dotnet build AgentCake.sln -c Release
    dotnet run --project src/AgentCake.csproj

Single-file release exe:

    dotnet publish src/AgentCake.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

The exe lands in `src/bin/Release/net10.0-windows/win-x64/publish/AgentCake.exe`.
Use **Run at login** in the tray menu to start it with Windows.

## Tests

The window-block and weekly-reset math live in `src/UsageMath.cs` (pure, no WinForms),
exercised by an xUnit suite in `tests/`:

    dotnet test tests/AgentCake.Tests.csproj

The test project compiles `UsageMath.cs` directly, so it builds without dragging in the
WinForms assembly.

## Settings

Right-click the tray icon → **Plan** for the common case, or **Edit settings.json…**
for everything. The file lives at `%APPDATA%\AgentCake\settings.json`:

| Key                        | Meaning |
|----------------------------|---------|
| `Plan`                     | `Pro` / `Max5` / `Max20` / `Custom` — sets the 5-hour token cap |
| `Custom5hCapTokens`        | 5h cap when `Plan` = `Custom` |
| `WeeklyCapTokens`          | Weekly token budget; `0` = auto (40× the 5h cap) |
| `WeeklyResetDay`           | Weekday the weekly limit resets (`Monday`, …) |
| `WeeklyResetHour`          | Hour 0–23 (local) of the weekly reset |
| `RefreshSeconds`           | Poll interval (default 15) |
| `CountCacheReadsInWindow`  | Include cache-read tokens in the gauges (default `false`) |
| `ClaudeDirOverride`        | Custom path to the `.claude` dir; empty = auto |

## How the numbers are computed

- **5-hour window** — ccusage-style billing blocks: a block starts at the first
  activity (floored to the hour) and lasts 5h; a gap longer than 5h opens a new
  block. The gauge shows the active block's tokens ÷ the plan's 5h cap. When you've
  been idle past the window end, it reads 0 (a fresh window is available).
- **Week** — tokens logged since the most recent weekly reset ÷ your weekly budget.
- Turns are de-duplicated by `message.id` + `requestId` (Claude Code can log the
  same turn in more than one transcript), and logs are read incrementally so polling
  stays cheap.

## Caveats (please read)

- The **5-hour caps** (Pro ≈44k, Max5 ≈88k, Max20 ≈220k tokens/window) are the
  community-observed figures, not an official API. Treat the gauge as a close
  estimate, not gospel.
- Anthropic does **not** publish the **weekly** limit as a token number (it's
  hours-based and opaque), so the weekly bar is *usage ÷ a budget you set*. Calibrate
  `WeeklyCapTokens` by noting where you actually start getting warned, then adjust.
- Cache-read tokens are **excluded** by default (they count far more cheaply toward
  limits). Flip `CountCacheReadsInWindow` if you want them in.
- Cost/pricing is intentionally not shown.

## Project layout

    AgentCake.sln
    src/
      AgentCake.csproj    WinForms, net10.0-windows, no NuGet deps
      Program.cs          entry point, single-instance guard
      TrayAppContext.cs   tray icon, timer, menu, run-at-login
      DetailsForm.cs      double-click popup with both gauges
      UsageReader.cs      incremental JSONL scan + aggregation
      UsageMath.cs        pure 5h-window + weekly-reset math (unit-tested)
      UsageModels.cs      record + totals + snapshot types
      AppSettings.cs      JSON settings in %APPDATA%\AgentCake
      IconRenderer.cs     draws the two-bar tray glyph
    tests/
      AgentCake.Tests.csproj
      UsageMathTests.cs   xUnit tests for the window/reset math
