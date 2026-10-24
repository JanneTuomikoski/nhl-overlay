namespace NHLOverlay.Models;

/// <summary>A player on a team's current roster.</summary>
public sealed record RosterPlayer(long Id, string FirstName, string LastName, string Position, string Country, string Team)
{
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>"A. Barkov".</summary>
    public string ShortName => FirstName.Length > 0 ? $"{FirstName[0]}. {LastName}" : LastName;

    public bool IsGoalie => Position == "G";
}

/// <summary>A goalie's line from a game's boxscore.</summary>
public sealed record GoalieLine(long PlayerId, string Name, string Team, int Saves, int ShotsAgainst, int GoalsAgainst, string TimeOnIce, string? Decision)
{
    public bool Played => ShotsAgainst > 0 || (TimeOnIce.Length > 0 && TimeOnIce != "00:00");

    public double SavePct => ShotsAgainst > 0 ? (double)Saves / ShotsAgainst : 1;
}

/// <summary>The parts of a boxscore the app uses.</summary>
public sealed record GameBoxscore(long GameId, IReadOnlyList<GoalieLine> Goalies, bool IsOfficial);
