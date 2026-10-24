namespace NHLOverlay.Models;

/// <summary>A team with its current standings line. Stats are zero when only the built-in team list is known.</summary>
public sealed record TeamStanding
{
    public string Abbrev { get; init; } = "";

    /// <summary>"Los Angeles Kings".</summary>
    public string FullName { get; init; } = "";

    /// <summary>"Kings".</summary>
    public string CommonName { get; init; } = "";

    public string? LogoUrl { get; init; }

    /// <summary>"E" or "W".</summary>
    public string Conference { get; init; } = "";

    public string ConferenceName { get; init; } = "";

    /// <summary>"A", "M", "C" or "P".</summary>
    public string Division { get; init; } = "";

    public string DivisionName { get; init; } = "";

    public int GamesPlayed { get; init; }

    public int Wins { get; init; }

    public int Losses { get; init; }

    public int OtLosses { get; init; }

    public int Points { get; init; }

    public double PointPct { get; init; }

    public int RegulationWins { get; init; }

    public int GoalDifferential { get; init; }

    public int DivisionRank { get; init; }

    public int ConferenceRank { get; init; }

    public int LeagueRank { get; init; }

    /// <summary>0 for division top-three teams; 1, 2, ... for the wildcard order within the conference.</summary>
    public int WildcardRank { get; init; }

    /// <summary>Last ten games, e.g. "7-2-1".</summary>
    public string LastTen { get; init; } = "";

    /// <summary>e.g. "W3", "L1", "OT2".</summary>
    public string Streak { get; init; } = "";

    public bool HasStats => GamesPlayed > 0;

    public string Record => $"{Wins}-{Losses}-{OtLosses}";
}
