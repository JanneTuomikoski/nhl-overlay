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
        TeamAbbrev = dto.TeamAbbrev ?? "",
        Period = dto.PeriodDescriptor?.Number ?? dto.Period ?? 0,
        PeriodType = ParsePeriodType(dto.PeriodDescriptor?.PeriodType),
        TimeInPeriod = dto.TimeInPeriod ?? "",
        AwayScore = dto.AwayScore ?? 0,
        HomeScore = dto.HomeScore ?? 0,
        Strength = dto.Strength,
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
