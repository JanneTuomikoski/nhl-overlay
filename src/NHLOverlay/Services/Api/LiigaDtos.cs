namespace NHLOverlay.Services.Api;

// Wire-format DTOs for https://liiga.fi/api/v2/games?date=yyyy-MM-dd (verified October 2026).
// Like the NHL API this is public but not an officially documented API, so everything is nullable.

internal sealed class LiigaDayDto
{
    public List<LiigaGameDto>? Games { get; set; }
}

internal sealed class LiigaGameDto
{
    public long Id { get; set; }
    public int? Season { get; set; }
    public string? Start { get; set; }
    public LiigaTeamDto? HomeTeam { get; set; }
    public LiigaTeamDto? AwayTeam { get; set; }
    public List<LiigaPeriodDto>? Periods { get; set; }
    public string? FinishedType { get; set; }
    public bool? Started { get; set; }
    public bool? Ended { get; set; }
    public int? GameTime { get; set; }
    public int? CurrentPeriod { get; set; }
    public int? PlayOffPhase { get; set; }
}

internal sealed class LiigaTeamDto
{
    public string? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int? Goals { get; set; }
    public List<LiigaGoalDto>? GoalEvents { get; set; }
    public LiigaLogosDto? Logos { get; set; }
}

internal sealed class LiigaLogosDto
{
    public string? DarkBg { get; set; }
    public string? LightBg { get; set; }
}

internal sealed class LiigaPeriodDto
{
    public int? Index { get; set; }
    public string? Category { get; set; }
    public int? StartTime { get; set; }
    public int? EndTime { get; set; }
}

internal sealed class LiigaGoalDto
{
    public long? EventId { get; set; }
    public long? ScorerPlayerId { get; set; }
    public LiigaPlayerDto? ScorerPlayer { get; set; }
    public int? GameTime { get; set; }
    public int? Period { get; set; }
    public List<string>? GoalTypes { get; set; }
    public List<LiigaPlayerDto>? AssistantPlayers { get; set; }
    public int? HomeTeamScore { get; set; }
    public int? AwayTeamScore { get; set; }
}

internal sealed class LiigaPlayerDto
{
    public long? PlayerId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
