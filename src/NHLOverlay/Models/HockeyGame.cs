namespace NHLOverlay.Models;

public enum League
{
    Nhl,
    Liiga,
}

/// <summary>Strongly typed snapshot of a single game (NHL, or Liiga when enabled).</summary>
public sealed record HockeyGame
{
    /// <summary>Stable game id within its league, e.g. NHL 2026020169.</summary>
    public long Id { get; init; }

    public League League { get; init; } = League.Nhl;

    public bool IsNhl => League == League.Nhl;

    /// <summary>1 = preseason, 2 = regular season, 3 = playoffs.</summary>
    public int GameType { get; init; }

    /// <summary>The NHL's schedule date (North American), not the user's local calendar date.</summary>
    public DateOnly NhlDate { get; init; }

    /// <summary>Actual scheduled start as an absolute instant.</summary>
    public DateTimeOffset StartTimeUtc { get; init; }

    public GameStatus Status { get; init; }

    /// <summary>Original NHL values, kept for diagnostics.</summary>
    public string RawGameState { get; init; } = "";

    public string RawScheduleState { get; init; } = "";

    public HockeyTeam Away { get; init; } = new();

    public HockeyTeam Home { get; init; } = new();

    public int? Period { get; init; }

    public PeriodType PeriodType { get; init; }

    public int MaxRegulationPeriods { get; init; } = 3;

    /// <summary>Game clock as displayed by the NHL, e.g. "12:41".</summary>
    public string? TimeRemaining { get; init; }

    public int? SecondsRemaining { get; init; }

    public bool ClockRunning { get; init; }

    public bool InIntermission { get; init; }

    /// <summary>For finished games: the period type the game ended in (REG/OT/SO).</summary>
    public PeriodType? LastPeriodType { get; init; }

    public IReadOnlyList<GameGoal> Goals { get; init; } = [];

    public string? GameCenterUrl { get; init; }

    public bool HasScore => Away.Score.HasValue && Home.Score.HasValue;
}
