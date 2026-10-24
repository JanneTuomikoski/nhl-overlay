# NHL Live Overlay

A lightweight, always-on-top Windows overlay that shows live NHL scores while you play
(or do anything else). Native C# / WPF on .NET 10. No Electron, no installer, no admin rights.

> Status: early development. Phases 1 (foundation) and 2 (real NHL scoreboard) are done.
> See [Roadmap](#roadmap) for what is not implemented yet.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build
  (`winget install Microsoft.DotNet.SDK.10`). The published self-contained build needs no runtime.

## Build, test, run

```powershell
dotnet restore
dotnet build -c Release
dotnet test
dotnet run --project src/NHLOverlay/NHLOverlay.csproj
```

### Portable single-file build

```powershell
dotnet publish src/NHLOverlay/NHLOverlay.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The output in `src/NHLOverlay/bin/Release/net10.0-windows/win-x64/publish/` is a single
`NHLOverlay.exe` that runs without installing .NET.

## Using it

- The overlay appears in the top-right corner of the primary screen. Drag it by its header.
  Its position is remembered.
- The app lives in the **system tray**. Left-click the tray icon to show/hide the overlay;
  right-click for *Refresh now*, *Settings...* and *Exit*. Closing the overlay only hides it.
- The gear icon in the overlay header opens the settings.
- The dot in the header shows data freshness: green = up to date, amber = the last refresh
  failed and cached scores are shown, red = no data. Hover it for details.
- Only one instance runs at a time.

### What the cards show

```
LAK  2 - 1  NJD    2ND 08:14     live: period + clock (green)
TOR  0 - 0  EDM    1ST INT       intermission (light blue)
FLA  3 - 3  NYR    OT 03:12      overtime / shootout (orange)
ANA  5 - 4  PHI    FINAL/SO      finished, with OT/SO suffix (grey)
SJS  @  BOS        su 2.00       upcoming: local start time, weekday if not today
```

Start times are converted to your Windows time zone and formatted with your regional settings.

## Settings

Stored as readable JSON in `%AppData%\NHLOverlay\settings.json`. Invalid or missing values fall back
to defaults per section; a corrupt file is backed up as `settings.corrupt-<timestamp>.json`.

| Section    | Setting                 | Default |
|------------|-------------------------|---------|
| General    | Launch with Windows     | off (per-user `Run` registry key) |
| General    | Show overlay on startup | on (off = start in tray) |
| General    | Refresh interval        | 30 s (15 / 30 / 60 / 120) |
| Appearance | Background opacity      | 85 % |
| Appearance | UI scale                | 100 % (75-200 %) |
| Appearance | Show game clock         | on |
| Appearance | Show shots on goal      | off |

**Portable mode:** put an empty file named `portable` (or a `settings.json`) next to `NHLOverlay.exe`.
If that folder is writable, settings are stored there instead of `%AppData%`.

Logs: `logs\nhloverlay.log` in the settings folder (capped at about 1 MB, one rollover file).

## NHL API usage

Data comes from the public NHL web API `https://api-web.nhle.com/v1/` (no key required, but not an
officially supported third-party API; it may change without notice).

- `GET /score/{yyyy-MM-dd}` is used for every refresh. Unlike `/scoreboard/now`, it includes the game
  clock, shots on goal and each game's goal list (with player ids). The goal list will power
  Hat-Trick Watch without extra per-game requests.
- **Which NHL dates are shown:** the current instant is evaluated in US Eastern time. The previous
  NHL date stays visible until 12:00 Eastern (19:00 in Finland), so last night's games, which end
  in the early morning in Europe, are not lost when the local date changes. A previous date that
  still has an unfinished game stays until that game is final.
- **Request budget:** one request per relevant date per refresh. Dates whose games are all official
  (`OFF`) or postponed/cancelled are cached and never requested again. When nothing is live, polling
  slows to every 5 minutes and resumes the normal interval 10 minutes before the next start.
- **Failures:** the last good scoreboard stays on screen (amber dot). Retries back off
  exponentially (30 s, 60 s, 120 s ... up to 5 min) and honor `Retry-After`. Requests time out after 15 s.

## Project layout

```
NHLOverlay.sln
src/NHLOverlay/
  Models/        HockeyGame, HockeyTeam, GameGoal, GameStatus, AppSettings
  Services/      NhlApiService (+ Api/ DTOs and mapper), ScorePollingService,
                 GameStateService, SettingsService
  ViewModels/    OverlayViewModel, GameCardViewModel, SettingsViewModel
  Views/         OverlayWindow, SettingsWindow
  Controls/      GameCard
  Helpers/       NhlCalendar, GameStatusFormatter, GameOrdering, TrayIcon, Log, ...
  Themes/        Dark.xaml
tests/NHLOverlay.Tests/
  Fixtures/      captured real API responses (live day, day with OT/SO finals)
```

Business logic (parsing, state derivation, date handling, polling, settings) does not depend on WPF
and is covered by unit tests that run offline.

## Known limitations

- **Team logos are not shown yet.** The NHL serves logos only as SVG, which WPF cannot render
  natively. Options: a small built-in SVG-path converter, or the SharpVectors package.
- Not yet implemented: click-through mode, global hotkeys, favorites/filters, Hat-Trick Watch,
  notifications, expanded/ticker layouts, multi-monitor DPI edge cases (see Roadmap).
- Overlays work above windowed and borderless-fullscreen games. True exclusive fullscreen games
  draw over all other windows; that is a Windows limitation, not something this app works around.
- Clicking the overlay header gives it focus; click-through and no-activate behavior come in Phase 3.

## Roadmap

1. ~~Foundation: solution, overlay shell, settings window and persistence, tray~~
2. ~~Real NHL scoreboard: API client, parsing, polling, time zones, loading/empty/error states~~
3. Overlay behavior: click-through, global hotkeys (Ctrl+Alt+N / Ctrl+Alt+I), no-activate,
   multi-monitor and DPI handling, resize
4. Favorite teams, highlighting, sorting and filters
5. Hat-Trick Watch
6. Notifications
7. Polish: expanded mode, logos, release packaging
