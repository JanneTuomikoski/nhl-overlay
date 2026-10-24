using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services.Api;

/// <summary>
/// Converts Liiga games into the shared <see cref="HockeyGame"/> model. Liiga reports elapsed game time in
/// seconds and goal types as Finnish abbreviations (YV = power play, AV = shorthanded, TM = empty net,
/// VL = shootout winner). Goal events that did not change the score (e.g. RL0, a missed penalty shot) are
/// skipped.
/// </summary>
internal static class LiigaMapper
{
    private const int PeriodSeconds = 1200;

    public static IReadOnlyList<HockeyGame> MapDay(LiigaDayDto? dto)
    {
        var games = new List<HockeyGame>();
        foreach (var game in dto?.Games ?? [])
        {
            if (game is null || game.Id <= 0) continue;
            try
            {
                games.Add(MapGame(game));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                Log.Warn($"Skipping Liiga game {game.Id}: unexpected data.", ex);
            }
        }

        return games;
    }

    public static HockeyGame MapGame(LiigaGameDto dto)
    {
        var started = dto.Started ?? false;
        var ended = dto.Ended ?? false;
        var period = dto.CurrentPeriod ?? 0;
        var gameTime = dto.GameTime ?? 0;
        var periods = dto.Periods ?? [];
        var current = periods.FirstOrDefault(p => p?.Index == period);
        var periodType = PeriodTypeFor(period, current?.Category);
        var periodEnd = current?.EndTime ?? (period * PeriodSeconds);
        var secondsRemaining = Math.Max(0, periodEnd - gameTime);
        var inIntermission = started && !ended && period is >= 1 and <= 3 && gameTime > 0 && gameTime == periodEnd;

        var status = !started ? GameStatus.Scheduled
            : ended ? GameStatus.Final
            : inIntermission ? GameStatus.Intermission
            : periodType switch
            {
                PeriodType.Overtime => GameStatus.Overtime,
                PeriodType.Shootout => GameStatus.Shootout,
                _ => GameStatus.Live,
            };

        var away = dto.AwayTeam;
        var home = dto.HomeTeam;
        var awayAbbrev = LiigaTeams.Abbrev(away?.TeamId, away?.TeamName);
        var homeAbbrev = LiigaTeams.Abbrev(home?.TeamId, home?.TeamName);
        var start = DateTimeOffset.TryParse(dto.Start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s)
            ? s.ToUniversalTime()
            : default;

        return new HockeyGame
        {
            Id = dto.Id,
            League = League.Liiga,
            GameType = dto.PlayOffPhase is > 0 ? 3 : 2,
            NhlDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, FinnishZone).DateTime),
            StartTimeUtc = start,
            Status = status,
            RawGameState = !started ? "FUT" : ended ? "OFF" : "LIVE",
            Away = Team(away, awayAbbrev, started),
            Home = Team(home, homeAbbrev, started),
            Period = started ? period : null,
            PeriodType = started ? periodType : PeriodType.Unknown,
            MaxRegulationPeriods = 3,
            TimeRemaining = started && !ended ? Clock(secondsRemaining) : null,
            SecondsRemaining = started && !ended ? secondsRemaining : null,
            ClockRunning = started && !ended && !inIntermission,
            InIntermission = inIntermission,
            LastPeriodType = !ended ? null : dto.FinishedType switch
            {
                "ENDED_DURING_EXTENDED_GAME_TIME" => PeriodType.Overtime,
                "ENDED_DURING_WINNING_SHOT_COMPETITION" => PeriodType.Shootout,
                _ => PeriodType.Regulation,
            },
            Goals = Goals(dto, awayAbbrev, homeAbbrev),
        };
    }

    private static HockeyTeam Team(LiigaTeamDto? dto, string abbrev, bool started) => new()
    {
        Abbrev = abbrev,
        Name = dto?.TeamName ?? abbrev,
        Score = started ? dto?.Goals ?? 0 : null,
        // A smaller rendition of the (WebP) logo; the full one is far larger than an overlay needs.
        LogoUrl = dto?.Logos?.DarkBg is { Length: > 0 } logo ? logo + (logo.Contains('?') ? "&" : "?") + "width=128" : null,
    };

    private static IReadOnlyList<GameGoal> Goals(LiigaGameDto dto, string awayAbbrev, string homeAbbrev)
    {
        var events = (dto.HomeTeam?.GoalEvents ?? []).Where(g => g is not null).Select(g => (Goal: g!, Team: homeAbbrev))
            .Concat((dto.AwayTeam?.GoalEvents ?? []).Where(g => g is not null).Select(g => (Goal: g!, Team: awayAbbrev)))
            .OrderBy(e => e.Goal.GameTime ?? 0)
            .ThenBy(e => e.Goal.EventId ?? 0)
            .ToList();

        var goals = new List<GameGoal>();
        int lastAway = 0, lastHome = 0;
        foreach (var (g, team) in events)
        {
            var types = g.GoalTypes ?? [];
            var period = g.Period ?? 0;
            var shootout = period >= 5 || types.Contains("VL");
            var away = g.AwayTeamScore ?? 0;
            var home = g.HomeTeamScore ?? 0;

            if (shootout)
            {
                // Only the deciding shootout goal is reported; it counts toward the final score.
                away = dto.AwayTeam?.Goals ?? away;
                home = dto.HomeTeam?.Goals ?? home;
            }
            else if (away == lastAway && home == lastHome)
            {
                continue; // no change in score: not a goal (e.g. "RL0", an unsuccessful penalty shot)
            }
            else
            {
                (lastAway, lastHome) = (away, home);
            }

            var gameTime = g.GameTime ?? 0;
            goals.Add(new GameGoal
            {
                EventId = g.EventId,
                PlayerId = g.ScorerPlayerId ?? g.ScorerPlayer?.PlayerId ?? 0,
                ScorerName = FullName(g.ScorerPlayer),
                ScorerShortName = ShortName(g.ScorerPlayer),
                TeamAbbrev = team,
                Period = period,
                PeriodType = shootout ? PeriodType.Shootout : PeriodTypeFor(period, null),
                TimeInPeriod = shootout ? "" : Clock(Math.Max(0, gameTime - ((Math.Min(period, 4) - 1) * PeriodSeconds))),
                AwayScore = away,
                HomeScore = home,
                Strength = types.Any(t => t.StartsWith("YV", StringComparison.Ordinal)) ? "pp"
                    : types.Any(t => t.StartsWith("AV", StringComparison.Ordinal)) ? "sh"
                    : "ev",
                IsEmptyNet = types.Contains("TM"),
                Assists = (g.AssistantPlayers ?? [])
                    .Where(a => a?.PlayerId is > 0)
                    .Select(a => new GoalAssist(a!.PlayerId!.Value, ShortName(a)))
                    .ToList(),
            });
        }

        return goals;
    }

    private static PeriodType PeriodTypeFor(int period, string? category) => category switch
    {
        "OVERTIME" => PeriodType.Overtime,
        "WINNING_SHOT_COMPETITION" => PeriodType.Shootout,
        "NORMAL" => PeriodType.Regulation,
        _ => period switch
        {
            <= 0 => PeriodType.Unknown,
            <= 3 => PeriodType.Regulation,
            4 => PeriodType.Overtime,
            _ => PeriodType.Shootout,
        },
    };

    private static string Clock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

    private static string FullName(LiigaPlayerDto? p) => $"{p?.FirstName} {p?.LastName}".Trim();

    private static string ShortName(LiigaPlayerDto? p) =>
        p?.FirstName is { Length: > 0 } first ? $"{first[0]}. {p.LastName}" : p?.LastName ?? "";

    private static TimeZoneInfo FinnishZone { get; } = FindFinnishZone();

    private static TimeZoneInfo FindFinnishZone()
    {
        foreach (var id in new[] { "Europe/Helsinki", "FLE Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
