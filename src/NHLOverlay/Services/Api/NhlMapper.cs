using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services.Api;

/// <summary>Converts NHL wire DTOs into domain models, tolerating missing or unexpected values.</summary>
internal static class NhlMapper
{
    private const string NhlWebsite = "https://www.nhl.com";

    public static IReadOnlyList<HockeyGame> MapGames(IEnumerable<GameDto?>? games)
    {
        var result = new List<HockeyGame>();
        foreach (var dto in games ?? [])
        {
            if (dto is null || dto.Id <= 0) continue;
            try
            {
                result.Add(MapGame(dto));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                // One malformed game must not hide the rest of the scoreboard.
                Log.Warn($"Skipping game {dto.Id}: unexpected data.", ex);
            }
        }

        return result;
    }

    public static HockeyGame MapGame(GameDto dto)
    {
        var periodType = ParsePeriodType(dto.PeriodDescriptor?.PeriodType);
        var inIntermission = dto.Clock?.InIntermission ?? false;
        return new HockeyGame
        {
            Id = dto.Id,
            GameType = dto.GameType ?? 0,
            NhlDate = DateOnly.TryParseExact(dto.GameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : default,
            StartTimeUtc = DateTimeOffset.TryParse(dto.StartTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)
                ? start.ToUniversalTime()
                : default,
            Status = DeriveStatus(dto.GameState, dto.GameScheduleState, periodType, inIntermission),
            RawGameState = dto.GameState ?? "",
            RawScheduleState = dto.GameScheduleState ?? "",
            Away = MapTeam(dto.AwayTeam),
            Home = MapTeam(dto.HomeTeam),
            Period = dto.PeriodDescriptor?.Number ?? dto.Period,
            PeriodType = periodType,
            MaxRegulationPeriods = dto.PeriodDescriptor?.MaxRegulationPeriods is int max && max > 0 ? max : 3,
            TimeRemaining = dto.Clock?.TimeRemaining,
            SecondsRemaining = dto.Clock?.SecondsRemaining,
            ClockRunning = dto.Clock?.Running ?? false,
            InIntermission = inIntermission,
            LastPeriodType = dto.GameOutcome?.LastPeriodType is { } last ? ParsePeriodType(last) : null,
            Goals = (dto.Goals ?? []).Where(g => g?.PlayerId is > 0).Select(g => MapGoal(g!)).ToList(),
            GameCenterUrl = string.IsNullOrWhiteSpace(dto.GameCenterLink) ? null : NhlWebsite + dto.GameCenterLink,
        };
    }

    /// <summary>
    /// NHL gameState values: FUT (future), PRE (pre-game), LIVE, CRIT (late/critical live), FINAL, OFF (official).
    /// gameScheduleState: OK, PPD (postponed), SUSP (suspended), CNCL (cancelled).
    /// </summary>
    public static GameStatus DeriveStatus(string? gameState, string? scheduleState, PeriodType periodType, bool inIntermission)
    {
        switch (scheduleState?.Trim().ToUpperInvariant())
        {
            case "PPD": return GameStatus.Postponed;
            case "SUSP": return GameStatus.Suspended;
            case "CNCL": return GameStatus.Cancelled;
        }

        switch (gameState?.Trim().ToUpperInvariant())
        {
            case "FUT":
                return GameStatus.Scheduled;
            case "PRE":
                return GameStatus.PreGame;
            case "LIVE":
            case "CRIT":
                if (inIntermission) return GameStatus.Intermission;
                return periodType switch
                {
                    PeriodType.Overtime => GameStatus.Overtime,
                    PeriodType.Shootout => GameStatus.Shootout,
                    _ => GameStatus.Live,
                };
            case "FINAL":
            case "OFF":
                return GameStatus.Final;
            default:
                return GameStatus.Unknown;
        }
    }

    public static PeriodType ParsePeriodType(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "REG" => PeriodType.Regulation,
        "OT" => PeriodType.Overtime,
        "SO" => PeriodType.Shootout,
        _ => PeriodType.Unknown,
    };

    public static IReadOnlyList<TeamStanding> MapStandings(IEnumerable<StandingDto?>? rows) =>
        (rows ?? [])
        .Where(r => !string.IsNullOrWhiteSpace(r?.TeamAbbrev?.Default))
        .Select(r => new TeamStanding
        {
            Abbrev = r!.TeamAbbrev!.Default!,
            FullName = r.TeamName?.Default ?? r.TeamAbbrev.Default!,
            CommonName = r.TeamCommonName?.Default ?? r.TeamName?.Default ?? r.TeamAbbrev.Default!,
            LogoUrl = r.TeamLogo,
            Conference = r.ConferenceAbbrev ?? "",
            ConferenceName = r.ConferenceName ?? NhlTeams.ConferenceName(r.ConferenceAbbrev ?? ""),
            Division = r.DivisionAbbrev ?? "",
            DivisionName = r.DivisionName ?? NhlTeams.DivisionName(r.DivisionAbbrev ?? ""),
            GamesPlayed = r.GamesPlayed ?? 0,
            Wins = r.Wins ?? 0,
            Losses = r.Losses ?? 0,
            OtLosses = r.OtLosses ?? 0,
            Points = r.Points ?? 0,
            PointPct = r.PointPctg ?? 0,
            RegulationWins = r.RegulationWins ?? 0,
            GoalDifferential = r.GoalDifferential ?? 0,
            DivisionRank = r.DivisionSequence ?? 0,
            ConferenceRank = r.ConferenceSequence ?? 0,
            LeagueRank = r.LeagueSequence ?? 0,
            WildcardRank = r.WildcardSequence ?? 0,
            LastTen = r.L10Wins is null ? "" : $"{r.L10Wins}-{r.L10Losses ?? 0}-{r.L10OtLosses ?? 0}",
            Streak = string.IsNullOrEmpty(r.StreakCode) ? "" : $"{r.StreakCode}{r.StreakCount}",
        })
        .ToList();

    public static PlayByPlay MapPlayByPlay(PlayByPlayDto dto)
    {
        var away = dto.AwayTeam?.Abbrev ?? "";
        var home = dto.HomeTeam?.Abbrev ?? "";
        string? TeamOf(int? id) => id is null ? null : id == dto.AwayTeam?.Id ? away : id == dto.HomeTeam?.Id ? home : null;

        var names = (dto.RosterSpots ?? [])
            .Where(r => r is not null && r.PlayerId > 0)
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.First();
                var first = r.FirstName?.Default ?? "";
                return first.Length > 0 ? $"{first[0]}. {r.LastName?.Default}" : r.LastName?.Default ?? "";
            });

        var plays = (dto.Plays ?? [])
            .Where(p => p is not null)
            .Select(p =>
            {
                var d = p!.Details;
                var type = ParsePlayType(p.TypeDescKey);
                return new PlayEvent
                {
                    EventId = p.EventId ?? 0,
                    SortOrder = p.SortOrder ?? 0,
                    Type = type,
                    Period = p.PeriodDescriptor?.Number ?? 0,
                    PeriodType = ParsePeriodType(p.PeriodDescriptor?.PeriodType),
                    TimeInPeriod = p.TimeInPeriod ?? "",
                    SituationCode = p.SituationCode ?? "",
                    Team = TeamOf(d?.EventOwnerTeamId),
                    X = d?.XCoord,
                    Y = d?.YCoord,
                    HomeDefendingSide = p.HomeTeamDefendingSide,
                    PrimaryPlayerId = type switch
                    {
                        PlayType.Goal => d?.ScoringPlayerId,
                        PlayType.Hit => d?.HittingPlayerId,
                        PlayType.Penalty => d?.CommittedByPlayerId ?? d?.PlayerId,
                        PlayType.BlockedShot => d?.ShootingPlayerId,
                        _ => d?.ShootingPlayerId ?? d?.PlayerId,
                    },
                    SecondaryPlayerId = type switch
                    {
                        PlayType.Goal => d?.Assist1PlayerId,
                        PlayType.Hit => d?.HitteePlayerId,
                        PlayType.Penalty => d?.DrawnByPlayerId,
                        PlayType.BlockedShot => d?.BlockingPlayerId,
                        _ => d?.GoalieInNetId,
                    },
                    TertiaryPlayerId = type == PlayType.Goal ? d?.Assist2PlayerId : null,
                    ShotType = d?.ShotType,
                    PenaltyDescription = d?.DescKey?.Replace('-', ' '),
                    PenaltyMinutes = d?.Duration ?? 0,
                    AwayScore = d?.AwayScore,
                    HomeScore = d?.HomeScore,
                };
            })
            .OrderBy(p => p.SortOrder)
            .ToList();

        return new PlayByPlay(dto.Id, dto.GameType ?? 0, away, home, plays, names);
    }

    public static PlayType ParsePlayType(string? key) => key switch
    {
        "goal" => PlayType.Goal,
        "shot-on-goal" => PlayType.ShotOnGoal,
        "missed-shot" => PlayType.MissedShot,
        "blocked-shot" => PlayType.BlockedShot,
        "hit" => PlayType.Hit,
        "penalty" => PlayType.Penalty,
        "faceoff" => PlayType.Faceoff,
        "giveaway" => PlayType.Giveaway,
        "takeaway" => PlayType.Takeaway,
        "stoppage" => PlayType.Stoppage,
        "period-start" => PlayType.PeriodStart,
        "period-end" => PlayType.PeriodEnd,
        "game-end" => PlayType.GameEnd,
        _ => PlayType.Other,
    };

    public static IReadOnlyList<PlayoffSeries> MapBracket(BracketDto? dto) =>
        (dto?.Series ?? [])
        .Where(s => s is not null)
        .Select(s => new PlayoffSeries(
            s!.PlayoffRound ?? 0,
            s.SeriesAbbrev ?? "",
            s.SeriesTitle ?? "",
            s.TopSeedTeam?.Abbrev,
            s.TopSeedWins ?? 0,
            s.BottomSeedTeam?.Abbrev,
            s.BottomSeedWins ?? 0,
            s.WinningTeamId is int winner && winner > 0
                ? winner == s.TopSeedTeam?.Id ? s.TopSeedTeam?.Abbrev : winner == s.BottomSeedTeam?.Id ? s.BottomSeedTeam?.Abbrev : null
                : null))
        .ToList();

    public static IReadOnlyList<RosterPlayer> MapRoster(RosterDto? dto, string team) =>
        new[] { dto?.Forwards, dto?.Defensemen, dto?.Goalies }
            .Where(group => group is not null)
            .SelectMany(group => group!)
            .Where(p => p is not null && p.Id > 0)
            .Select(p => new RosterPlayer(
                p.Id,
                p.FirstName?.Default ?? "",
                p.LastName?.Default ?? "",
                p.PositionCode ?? "",
                p.BirthCountry ?? "",
                team))
            .ToList();

    public static GameBoxscore MapBoxscore(BoxscoreDto dto, string awayAbbrev, string homeAbbrev)
    {
        static IEnumerable<GoalieLine> Goalies(BoxscoreTeamDto? team, string abbrev) =>
            (team?.Goalies ?? []).Where(g => g is not null && g.PlayerId > 0).Select(g => new GoalieLine(
                g.PlayerId, g.Name?.Default ?? "", abbrev, g.Saves ?? 0, g.ShotsAgainst ?? 0, g.GoalsAgainst ?? 0, g.Toi ?? "", g.Decision));

        return new GameBoxscore(
            dto.Id,
            Goalies(dto.PlayerByGameStats?.AwayTeam, awayAbbrev).Concat(Goalies(dto.PlayerByGameStats?.HomeTeam, homeAbbrev)).ToList(),
            string.Equals(dto.GameState, "OFF", StringComparison.OrdinalIgnoreCase));
    }

    private static HockeyTeam MapTeam(TeamDto? dto) => new()
    {
        Id = dto?.Id ?? 0,
        Abbrev = dto?.Abbrev ?? "?",
        Name = dto?.Name?.Default ?? dto?.Abbrev ?? "",
        Score = dto?.Score,
        ShotsOnGoal = dto?.Sog,
        LogoUrl = dto?.Logo,
    };

    private static GameGoal MapGoal(GoalDto dto) => new()
    {
        EventId = dto.EventId,
        PlayerId = dto.PlayerId ?? 0,
        ScorerName = FullName(dto),
        ScorerShortName = dto.Name?.Default ?? FullName(dto),
        TeamAbbrev = dto.TeamAbbrev ?? "",
        Period = dto.PeriodDescriptor?.Number ?? dto.Period ?? 0,
        PeriodType = ParsePeriodType(dto.PeriodDescriptor?.PeriodType),
        TimeInPeriod = dto.TimeInPeriod ?? "",
        AwayScore = dto.AwayScore ?? 0,
        HomeScore = dto.HomeScore ?? 0,
        Strength = dto.Strength,
        IsEmptyNet = string.Equals(dto.GoalModifier, "empty-net", StringComparison.OrdinalIgnoreCase),
        Assists = (dto.Assists ?? [])
            .Where(a => a?.PlayerId is > 0)
            .Select(a => new GoalAssist(a!.PlayerId!.Value, a.Name?.Default ?? ""))
            .ToList(),
    };

    private static string FullName(GoalDto dto)
    {
        var first = dto.FirstName?.Default;
        var last = dto.LastName?.Default;
        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
            return $"{first} {last}";
        return dto.Name?.Default ?? last ?? "";
    }
}
