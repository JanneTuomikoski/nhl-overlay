using System.Text.Json.Serialization;

namespace NHLOverlay.Services.Api;

// Wire-format DTOs for https://api-web.nhle.com/v1/. Verified against live responses (October 2026).
// Every member is nullable: the API is unofficial and fields come and go, so missing data must
// never fail deserialization. Mapping to domain models happens in NhlMapper.

internal sealed class ScoreResponseDto
{
    public string? CurrentDate { get; set; }
    public string? PrevDate { get; set; }
    public string? NextDate { get; set; }
    public List<GameDto>? Games { get; set; }
}

internal sealed class GameDto
{
    public long Id { get; set; }
    public int? GameType { get; set; }
    public string? GameDate { get; set; }

    [JsonPropertyName("startTimeUTC")]
    public string? StartTimeUtc { get; set; }

    public string? GameState { get; set; }
    public string? GameScheduleState { get; set; }
    public TeamDto? AwayTeam { get; set; }
    public TeamDto? HomeTeam { get; set; }
    public ClockDto? Clock { get; set; }
    public int? Period { get; set; }
    public PeriodDescriptorDto? PeriodDescriptor { get; set; }
    public GameOutcomeDto? GameOutcome { get; set; }
    public List<GoalDto>? Goals { get; set; }
    public string? GameCenterLink { get; set; }
}

internal sealed class TeamDto
{
    public int? Id { get; set; }
    public LocalizedStringDto? Name { get; set; }
    public string? Abbrev { get; set; }
    public int? Score { get; set; }
    public int? Sog { get; set; }
    public string? Logo { get; set; }
}

/// <summary>Localized text, e.g. <c>{"default": "Kings", "fr": "..."}</c>.</summary>
internal sealed class LocalizedStringDto
{
    public string? Default { get; set; }
}

internal sealed class ClockDto
{
    public string? TimeRemaining { get; set; }
    public int? SecondsRemaining { get; set; }
    public bool? Running { get; set; }
    public bool? InIntermission { get; set; }
}

internal sealed class PeriodDescriptorDto
{
    public int? Number { get; set; }
    public string? PeriodType { get; set; }
    public int? MaxRegulationPeriods { get; set; }
}

internal sealed class GameOutcomeDto
{
    public string? LastPeriodType { get; set; }
}

internal sealed class PlayerLandingDto
{
    public long PlayerId { get; set; }
    public LocalizedStringDto? FirstName { get; set; }
    public LocalizedStringDto? LastName { get; set; }
    public string? CurrentTeamAbbrev { get; set; }
    public CareerTotalsDto? CareerTotals { get; set; }
    public List<GameLogDto>? Last5Games { get; set; }
}

internal sealed class CareerTotalsDto
{
    public CareerLineDto? RegularSeason { get; set; }
}

internal sealed class CareerLineDto
{
    public int? Goals { get; set; }
    public int? Assists { get; set; }
    public int? Points { get; set; }
    public int? GamesPlayed { get; set; }
}

internal sealed class GameLogResponseDto
{
    public List<GameLogDto>? GameLog { get; set; }
}

internal sealed class GameLogDto
{
    public long GameId { get; set; }
    public int? Goals { get; set; }
    public int? Assists { get; set; }
}

internal sealed class PlayByPlayDto
{
    public long Id { get; set; }
    public int? GameType { get; set; }
    public PbpTeamDto? AwayTeam { get; set; }
    public PbpTeamDto? HomeTeam { get; set; }
    public List<PlayDto>? Plays { get; set; }
    public List<RosterSpotDto>? RosterSpots { get; set; }
}

internal sealed class PbpTeamDto
{
    public int? Id { get; set; }
    public string? Abbrev { get; set; }
}

internal sealed class PlayDto
{
    public long? EventId { get; set; }
    public int? SortOrder { get; set; }
    public PeriodDescriptorDto? PeriodDescriptor { get; set; }
    public string? TimeInPeriod { get; set; }
    public string? SituationCode { get; set; }
    public string? TypeDescKey { get; set; }
    public string? HomeTeamDefendingSide { get; set; }
    public PlayDetailsDto? Details { get; set; }
}

internal sealed class PlayDetailsDto
{
    public int? XCoord { get; set; }
    public int? YCoord { get; set; }
    public string? ShotType { get; set; }
    public int? EventOwnerTeamId { get; set; }
    public long? ShootingPlayerId { get; set; }
    public long? ScoringPlayerId { get; set; }
    public long? Assist1PlayerId { get; set; }
    public long? Assist2PlayerId { get; set; }
    public long? GoalieInNetId { get; set; }
    public long? HittingPlayerId { get; set; }
    public long? HitteePlayerId { get; set; }
    public long? BlockingPlayerId { get; set; }
    public long? CommittedByPlayerId { get; set; }
    public long? DrawnByPlayerId { get; set; }
    public long? PlayerId { get; set; }
    public string? DescKey { get; set; }
    public int? Duration { get; set; }
    public int? AwayScore { get; set; }
    public int? HomeScore { get; set; }
}

internal sealed class RosterSpotDto
{
    public int? TeamId { get; set; }
    public long PlayerId { get; set; }
    public LocalizedStringDto? FirstName { get; set; }
    public LocalizedStringDto? LastName { get; set; }
}

internal sealed class ClubScheduleDto
{
    public List<GameDto>? Games { get; set; }
}

internal sealed class BracketDto
{
    public List<SeriesDto>? Series { get; set; }
}

internal sealed class SeriesDto
{
    public string? SeriesTitle { get; set; }
    public string? SeriesAbbrev { get; set; }
    public int? PlayoffRound { get; set; }
    public int? TopSeedWins { get; set; }
    public int? BottomSeedWins { get; set; }
    public int? WinningTeamId { get; set; }
    public SeriesTeamDto? TopSeedTeam { get; set; }
    public SeriesTeamDto? BottomSeedTeam { get; set; }
}

internal sealed class SeriesTeamDto
{
    public int? Id { get; set; }
    public string? Abbrev { get; set; }
}

internal sealed class StandingsResponseDto
{
    public List<StandingDto>? Standings { get; set; }
}

internal sealed class StandingDto
{
    public LocalizedStringDto? TeamAbbrev { get; set; }
    public LocalizedStringDto? TeamName { get; set; }
    public LocalizedStringDto? TeamCommonName { get; set; }
    public string? TeamLogo { get; set; }
    public string? ConferenceAbbrev { get; set; }
    public string? ConferenceName { get; set; }
    public string? DivisionAbbrev { get; set; }
    public string? DivisionName { get; set; }
    public int? GamesPlayed { get; set; }
    public int? Wins { get; set; }
    public int? Losses { get; set; }
    public int? OtLosses { get; set; }
    public int? Points { get; set; }
    public double? PointPctg { get; set; }
    public int? RegulationWins { get; set; }
    public int? GoalDifferential { get; set; }
    public int? DivisionSequence { get; set; }
    public int? ConferenceSequence { get; set; }
    public int? LeagueSequence { get; set; }
    public int? WildcardSequence { get; set; }
    public int? L10Wins { get; set; }
    public int? L10Losses { get; set; }
    public int? L10OtLosses { get; set; }
    public string? StreakCode { get; set; }
    public int? StreakCount { get; set; }
}

internal sealed class GoalDto
{
    public long? EventId { get; set; }
    public int? Period { get; set; }
    public PeriodDescriptorDto? PeriodDescriptor { get; set; }
    public string? TimeInPeriod { get; set; }
    public long? PlayerId { get; set; }
    public LocalizedStringDto? Name { get; set; }
    public LocalizedStringDto? FirstName { get; set; }
    public LocalizedStringDto? LastName { get; set; }
    public string? TeamAbbrev { get; set; }
    public int? AwayScore { get; set; }
    public int? HomeScore { get; set; }
    public string? Strength { get; set; }
    public string? GoalModifier { get; set; }
    public List<AssistDto>? Assists { get; set; }
}

internal sealed class AssistDto
{
    public long? PlayerId { get; set; }
    public LocalizedStringDto? Name { get; set; }
}

internal sealed class RosterDto
{
    public List<RosterPlayerDto>? Forwards { get; set; }
    public List<RosterPlayerDto>? Defensemen { get; set; }
    public List<RosterPlayerDto>? Goalies { get; set; }
}

internal sealed class RosterPlayerDto
{
    public long Id { get; set; }
    public LocalizedStringDto? FirstName { get; set; }
    public LocalizedStringDto? LastName { get; set; }
    public string? PositionCode { get; set; }
    public string? BirthCountry { get; set; }
    public int? SweaterNumber { get; set; }
}

internal sealed class BoxscoreDto
{
    public long Id { get; set; }
    public string? GameState { get; set; }
    public PlayerByGameStatsDto? PlayerByGameStats { get; set; }
}

internal sealed class PlayerByGameStatsDto
{
    public BoxscoreTeamDto? AwayTeam { get; set; }
    public BoxscoreTeamDto? HomeTeam { get; set; }
}

internal sealed class BoxscoreTeamDto
{
    public List<BoxscoreGoalieDto>? Goalies { get; set; }
}

internal sealed class BoxscoreGoalieDto
{
    public long PlayerId { get; set; }
    public LocalizedStringDto? Name { get; set; }
    public int? Saves { get; set; }
    public int? ShotsAgainst { get; set; }
    public int? GoalsAgainst { get; set; }
    public string? Toi { get; set; }
    public string? Decision { get; set; }
}
