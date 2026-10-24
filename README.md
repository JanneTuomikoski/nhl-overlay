# NHL Live Overlay

A lightweight, always-on-top Windows overlay that shows live NHL scores while you play
(or do anything else). Native C# / WPF on .NET 10. No Electron, no installer, no admin rights.

> Status: phases 1-3 of the roadmap are done (foundation, live scoreboard, overlay behavior), plus
> logos, an expanded view and day navigation. See [Roadmap](#roadmap) for what is still missing.

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

The app lives in the **system tray**: left-click the icon to show/hide the overlay, right-click for
*Interaction mode*, *Expanded view*, *Refresh now*, *Settings...* and *Exit*. Closing the overlay only
hides it. Only one instance runs at a time.

### Interaction mode vs. click-through

| Mode | Mouse | Visible controls |
|------|-------|------------------|
| Interaction (blue outline) | The overlay takes clicks: drag the header, click a game for its goals, change days | Day arrows, view toggle, settings, resize grip |
| Click-through | Every click goes to the game underneath | None, just scores |

The overlay never takes keyboard focus from the game in either mode. Switch modes with **Ctrl+Alt+I**
or the tray menu.

### Hotkeys (global, configurable in Settings > Overlay)

| Default | Action |
|---------|--------|
| Ctrl+Alt+N | Show / hide the overlay |
| Ctrl+Alt+I | Toggle interaction / click-through mode |

If another application already owns a combination, the settings window says so (and it is logged).
Hotkeys need Ctrl, Alt or Win so they can never steal a plain game key.

### Size, scale and multiple monitors

- **Ctrl + mouse wheel** over the overlay scales everything (75-300 %); logos are vectors and stay sharp.
- Drag the **bottom-right grip** to change the width.
- **Expanded view** (header button, tray or settings) shows logos, team names, shots on goal and every
  goal with PP/SH/EN tags. It suits a large or vertical monitor; the list scrolls if it is taller
  than the screen.
- The position is stored in physical pixels, so it is restored on the right monitor even when monitors
  use different scaling. If that monitor is gone, the overlay returns to the top-right of the primary
  screen. It is kept inside the screen when it grows, and snaps to screen edges when dragged near them.

### Today, tonight and yesterday

"Today" is your **hockey day**: 08:00 to 08:00 local time. In Finland, tonight's games that start at
02:00 count as today (shown as just `2.00`), and the view switches to the next slate in the morning.
A game from last night that is still being played stays on the today view until it is final.

Use the **< >** arrows in the header to browse up to 7 days back (and tomorrow). Yesterday is always
preloaded, so it opens instantly. The view returns to today automatically after 2 minutes so live
scores are never hidden for long; click the day label to return sooner.

### What the compact cards show

```
LAK  2 - 1  NJD    2ND 08:14     live: period + clock (green)
TOR  0 - 0  EDM    1ST INT       intermission (light blue)
FLA  3 - 3  NYR    OT 03:12      overtime / shootout (orange)
ANA  5 - 4  PHI    FINAL/SO      finished, with OT/SO suffix (grey)
SJS  @  BOS        2.00          upcoming tonight: local start time
SJS  @  BOS        SUN 20.00     upcoming on another day
```

Team logos appear next to the abbreviations. Times use your Windows time zone and regional format.

## Settings

Stored as readable JSON in `%AppData%\NHLOverlay\settings.json`. Invalid or missing values fall back
to defaults per section; a corrupt file is backed up as `settings.corrupt-<timestamp>.json`.

| Tab        | Setting                         | Default |
|------------|---------------------------------|---------|
| General    | Launch with Windows             | off (per-user `Run` registry key) |
| General    | Show overlay on startup         | on (off = start in tray) |
| General    | Refresh interval                | 30 s (15 / 30 / 60 / 120) |
| Appearance | View                            | Compact (or Expanded) |
| Appearance | Background opacity              | 85 % |
| Appearance | UI scale                        | 100 % (75-300 %) |
| Appearance | Team logos / game clock         | on |
| Appearance | Shots on goal in compact view   | off |
| Overlay    | Click-through                   | off |
| Overlay    | Always on top                   | on |
| Overlay    | Lock position and size          | off |
| Overlay    | Snap to screen edges            | on |
| Overlay    | Hotkeys                         | Ctrl+Alt+N, Ctrl+Alt+I |

**Portable mode:** put an empty file named `portable` (or a `settings.json`) next to `NHLOverlay.exe`.
If that folder is writable, settings and the logo cache are stored there instead.

Logs: `logs\nhloverlay.log` in the settings folder (capped at about 1 MB, one rollover file).
Logo cache: `%LocalAppData%\NHLOverlay\cache\logos` (refreshed every 30 days; safe to delete).

## NHL API usage

Data comes from the public NHL web API `https://api-web.nhle.com/v1/` (no key required, but not an
officially supported third-party API; it may change without notice).

- `GET /score/{yyyy-MM-dd}` for every refresh. Unlike `/scoreboard/now`, it includes the game clock,
  shots on goal and each game's goal list with player ids (used for goal details now and Hat-Trick
  Watch next), so no per-game requests are needed.
- **Which dates are loaded:** yesterday's and today's NHL dates (see "hockey day" above), plus a date
  you browse to. The hockey day is mapped to the NHL date being played that local evening, which is
  correct for every time zone from Hawaii to eastern Europe, across all DST changes.
- **Request budget:** one request per loaded date per refresh. Dates whose games are all official
  (`OFF`) or postponed/cancelled are cached and never requested again. When nothing is live, polling
  slows to every 5 minutes and resumes the normal interval 10 minutes before the next start.
- **Failures:** the last good scoreboard stays on screen (amber dot). Retries back off
  exponentially (30 s, 60 s, 120 s ... up to 5 min) and honor `Retry-After`. Requests time out after 15 s.
- **Logos:** `https://assets.nhle.com/logos/nhl/svg/{TEAM}_dark.svg` (dark-background variant, falling
  back to the URL the API supplies), rendered with [SharpVectors](https://github.com/ElinamLLC/SharpVectors).

## Project layout

```
NHLOverlay.sln
src/NHLOverlay/
  Models/        HockeyGame, HockeyTeam, GameGoal, GameStatus, AppSettings, HotkeyGesture
  Services/      NhlApiService (+ Api/ DTOs and mapper), ScorePollingService, GameStateService,
                 SettingsService, LogoService, HotkeyService
  ViewModels/    OverlayViewModel, GameCardViewModel, SettingsViewModel
  Views/         OverlayWindow, SettingsWindow
  Controls/      GameCard (compact), ExpandedGameCard, GoalList, HotkeyBox
  Helpers/       NhlCalendar, GameStatusFormatter, GameOrdering, GlobalHotkeys, TrayIcon,
                 NativeMethods, Log, ...
  Themes/        Dark.xaml
tests/NHLOverlay.Tests/
  Fixtures/      captured real API responses (live day, day with OT/SO finals)
```

Business logic (parsing, state derivation, date handling, polling, settings, hotkey parsing, logo
caching) does not depend on the UI and is covered by unit tests that run offline.

## Known limitations

- Overlays work above windowed and borderless-fullscreen games. True exclusive-fullscreen games draw
  over every other window; that is a Windows limitation, not something this app works around
  (no injection or hooking, so nothing for anti-cheat to object to).
- Team names in the expanded view are the NHL's short names ("Kings", not "Los Angeles Kings").
- The single-file build is large (about 165 MB) because WPF cannot be trimmed; compression is planned.

## Roadmap

1. ~~Foundation: solution, overlay shell, settings window and persistence, tray~~
2. ~~Real NHL scoreboard: API client, parsing, polling, time zones, loading/empty/error states~~
3. ~~Overlay behavior: click-through, global hotkeys, no-activate, multi-monitor/DPI, resize, scale~~
4. Favorite teams, highlighting, sorting and filters
5. Hat-Trick Watch
6. Notifications
7. Polish: release packaging, single-file compression, resource review
