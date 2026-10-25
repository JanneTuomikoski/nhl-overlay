# NHL Live Overlay

A lightweight, always-on-top Windows overlay for live NHL scores (and optionally Finnish Liiga), made to sit
on top of a game such as World of Warcraft, or on a second monitor. Native C# / WPF on .NET 10: no Electron,
no installer, no admin rights, no injection into games.

Highlights: Hat-Trick Watch, notifications with voice, a gaming profile, a Finnish players watch, a morning
recap, a spoiler shield, a focus dashboard with a live shot map, standings with the playoff race, and
shutout / milestone / point-streak watches.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build
  (`winget install Microsoft.DotNet.SDK.10`). The published build needs no .NET installation.

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

Output: `src/NHLOverlay/bin/Release/net10.0-windows/win-x64/publish/NHLOverlay.exe` (about 170 MB, runs
without .NET installed). Add `-p:EnableCompressionInSingleFile=true` for a ~73 MB exe; this costs roughly
130 MB more RAM at run time, which is why it is off by default.

## Everyday use

The app lives in the **system tray**. Left-click the icon to show or hide the overlay; right-click for
interaction mode, expanded view, the game filter, Do Not Disturb, refresh, settings and exit.

Changes in the settings window show on the overlay right away. Save keeps them; Cancel (or closing the
window) undoes them. Hotkeys and "launch with Windows" take effect on Save.

### Interaction mode vs. click-through

| Mode | Mouse | What you see |
|------|-------|--------------|
| Interaction (blue outline) | The overlay takes clicks: drag the header, click a game for details, change days | Day arrows, standings, view toggle, settings, resize grip |
| Click-through | Every click goes to the game underneath | Scores only |

The overlay never takes keyboard focus from the game, in either mode.

### Hotkeys (global; change them in Settings > Overlay)

| Default | Action |
|---------|--------|
| Ctrl+Alt+N | Show / hide the overlay |
| Ctrl+Alt+I | Interaction / click-through (also overrides the gaming profile's click-through) |
| Ctrl+Alt+D | Do Not Disturb: hold all alerts, show a summary when switched off |

Conflicts with other applications are shown in the settings window. Ctrl, Alt or Win is required so a hotkey
can never take a plain game key.

### Views, size and monitors

- **Compact** (one line per game) and **expanded** (logos, team names, shots, every goal with PP/SH/EN tags).
  Switch with the header button, the tray or Settings > Appearance.
- **Standings** (list icon in the header): favorite teams' position, form, distance to the playoff cut line
  with games in hand, and their next five games; division and wildcard tables; the playoff bracket in spring.
- **Ctrl + mouse wheel** over the overlay scales it (75-300 %); drag the bottom-right grip to change its width.
  Logos are vectors and stay sharp.
- The position is stored in physical pixels per monitor (correct across mixed DPI). If the monitor is gone the
  overlay returns to the primary screen; it is kept inside the screen when it grows and snaps to edges.

### Today, tonight and yesterday

"Today" is your **hockey day**, 08:00 to 08:00 local time: in Finland tonight's 02:00 games count as today.
The **< >** arrows browse 7 days back and to tomorrow; yesterday is preloaded so it opens instantly. The view
returns to today after 2 minutes. A game from last night that is still running stays on today's view.

### Click a game (interaction mode)

Compact cards open their goal list plus **Hide score** (spoiler shield), **Focus** (dashboard) and **NHL.com**.
Expanded cards have the same as icon buttons.

## Features

**Favorites and filters.** Pick teams in Settings > Teams (Liiga teams too when Liiga is on). Favorites are
highlighted, listed first in their group and drive the favorites-only notifications. The today view can be
filtered: all, favorites, live, live and upcoming, hat-trick watch, completed.

**Hat-Trick Watch.** Players with two goals in a game get an amber card highlight while the game is live; three
goals show "HAT TRICK!" for the rest of the game day. Counts come from the game's own goal list (stable player
ids; shootout goals never count; scoring changes are followed; a briefly missing goal list keeps the last result).

**Notifications.** Per event and channel (Settings > Notifications): puck drop, goals, hat-trick watch, hat trick,
overtime/shootout, close game, final, "worth switching to", watched players' points, shutout watch, milestones and
point streaks; each to the overlay, a Windows notification, a sound (synthesized horn/chime/fanfare) and/or the
Windows voice (Microsoft David/Zira). Events are deduplicated by stable keys; on startup and at the morning
rollover the current state is only recorded, never replayed. Bursts after a reconnect are summarized. Quiet hours
mute sound and voice. "Worth switching to" scores late ties, comebacks and hat-trick chances, at most once per
10 minutes.

**Gaming profile.** When a listed game (default: Wow, WowClassic) is the active window: compact view,
click-through, and your choice of sounds, voice and Windows notifications. Only the active window's process name
is read (a WinEvent hook, no polling, nothing touches the game).

**Finnish players watch.** "FINNS TONIGHT" lists Finnish players with points, and goalies with saves, above the
games; cards show them too, and their goals and assists can alert you. Other countries can be chosen in Settings >
Players. Nationality is the birth country on the team roster.

**Morning recap.** From 08:00 until a chosen hour (default 14:00): your teams' results, hat tricks, shutouts
(goalie from the boxscore), the biggest comeback, OT/SO games, the highest-scoring game and watched players'
points. Dismiss it with its × button; it also appears above yesterday's games.

**Spoiler shield.** "Hide score" on a card, or always for chosen teams (Settings > Recap & spoilers). A hidden game
shows no score, period details, goals, hat-trick or country lines, recap entry or alerts until you reveal it.

**Focus dashboard.** A resizable window for one game, meant for a second or vertical monitor: big score, strength
strip (power play with an approximate countdown, pulled goalie, 4 on 4), shots by period, a to-scale shot map
(away team attacks right) and a play-by-play feed. It remembers its monitor, position and size.

**Watches.** Shutout watch late in the third and on completion; career milestones (first goal or point, 50 goals,
every 100 goals/assists/points) with "1 goal from 500" card lines; point streaks of at least 5 games (adjustable).
By default for favorite teams' and watched-country players.

**Liiga (off by default).** Settings > General > Leagues. Liiga games appear in their own section with scores,
clocks, goals, logos, hat tricks, favorites and notifications. Click a section title (NHL or LIIGA, in
interaction mode) to collapse or expand that league; a collapsed section shows only its game count and stays
collapsed after a restart. Standings, the focus dashboard and the player watches are NHL only.

## Settings and files

| What | Where |
|------|-------|
| Settings | `%AppData%\NHLOverlay\settings.json` (readable JSON; a corrupt file is backed up, bad sections reset) |
| Shield marks, recap dismissal | `%AppData%\NHLOverlay\state.json` |
| Log | `%AppData%\NHLOverlay\logs\nhloverlay.log` (about 1 MB, one rollover) |
| Cache (logos, standings, rosters, schedules, player stats) | `%LocalAppData%\NHLOverlay\cache` (safe to delete) |

**Portable mode:** put an empty file named `portable` (or a `settings.json`) next to `NHLOverlay.exe`; if that
folder is writable, settings, state, logs and cache live there.

Settings > Advanced shows the API status, last refresh and last error, and can refresh now, reset, export and
import settings (import keeps this computer's window positions).

## Data sources and request budget

Public, unofficial APIs; no keys. Fields were verified against live responses in October 2026.

| Data | Endpoint | How often |
|------|----------|-----------|
| Scores, clock, shots, goals | `api-web.nhle.com/v1/score/{date}` | refresh interval (default 30 s) while a game on that date is live; otherwise from 10 min before the next start, with an hourly check for schedule changes; every 6 h on days without games; finished days are cached for good |
| Standings | `/standings/now` | a few minutes after a game ends, otherwise every 6 h |
| Rosters (birth country) | `/roster/{team}/current` | once a day per team playing |
| Goalie lines | `/gamecenter/{id}/boxscore` | only for watched-country goalies and shutouts; every 2 min while live, once when official |
| Team schedules | `/club-schedule-season/{team}/now` | favorite teams, twice a day |
| Playoff bracket | `/playoff-bracket/{year}` | during the playoffs, every 30 min while shown |
| Play-by-play | `/gamecenter/{id}/play-by-play` | only the focused game, only while the dashboard is open |
| Player careers | `/player/{id}/landing` (+ `game-log/now` for long streaks) | once a day per player in watch scope |
| Logos | `assets.nhle.com/logos/nhl/svg/*` | once per 30 days |
| Liiga | `liiga.fi/api/v2/games?date={date}` | same rules as NHL scores but on its own schedule (a live Liiga game doesn't speed up NHL requests), when enabled |

Failures keep the last good data on screen (amber dot) and back off exponentially (honoring `Retry-After`).

## Project layout

```
src/NHLOverlay/
  Models/        games, goals, settings, events, standings, rosters, play-by-play, ...
  Services/      API clients (NHL, Liiga), polling, state, event detection, notifications, sound, speech,
                 watches, recap, spoilers, rosters, boxscores, schedules, logos, hotkeys, settings
  ViewModels/    overlay, cards, standings, focus dashboard, toasts, Settings/ (one view model per tab)
  Views/         overlay, settings, focus windows
  Controls/      compact/expanded cards, goal list, standings view, hotkey box
  Helpers/       calendar (hockey day), formatting, ordering, playoff race, milestones, Win32 interop, tray, log
tests/NHLOverlay.Tests/   unit tests with captured API responses in Fixtures/ (run offline)
```

## Known limitations

- Overlays work above windowed and borderless-fullscreen games, not exclusive fullscreen (a Windows limitation).
- The NHL and Liiga APIs are unofficial and can change without notice.
- The power-play countdown is an estimate from penalty times; the strength itself comes from the NHL's
  situation code.
- Nationality uses birth country (a player born abroad who represents Finland is not counted).
- Career totals for milestones refresh once a day; a milestone is detected as soon as the goal appears.
- Not implemented: horizontal ticker layout, light theme, automatic updates.
