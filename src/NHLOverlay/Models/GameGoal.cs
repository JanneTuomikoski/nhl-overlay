namespace NHLOverlay.Models;

/// <summary>A goal scored in a specific game.</summary>
public sealed record GameGoal
{
    /// <summary>NHL play event id. The score endpoint omits it; the landing endpoint provides it.</summary>
    public long? EventId { get; init; }

    /// <summary>Stable NHL player id of the scorer.</summary>
    public long PlayerId { get; init; }

    public string ScorerName { get; init; } = "";

    public string TeamAbbrev { get; init; } = "";

    public int Period { get; init; }

    public PeriodType PeriodType { get; init; }

    public string TimeInPeriod { get; init; } = "";

    public int AwayScore { get; init; }

    public int HomeScore { get; init; }

    /// <summary>"ev", "pp", "sh" etc. as reported by the API.</summary>
    public string? Strength { get; init; }

    /// <summary>Shootout goals do not count toward player goal totals (or hat tricks).</summary>
    public bool IsShootout => PeriodType == PeriodType.Shootout;
}
