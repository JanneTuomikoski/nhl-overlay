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
}
