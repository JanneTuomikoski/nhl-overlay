namespace NHLOverlay.Models;

/// <summary>One side of a game, as reported by the scoreboard.</summary>
public sealed record HockeyTeam
{
    public int Id { get; init; }

    /// <summary>Three-letter abbreviation, e.g. "EDM".</summary>
    public string Abbrev { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Null before the game has started.</summary>
    public int? Score { get; init; }

    public int? ShotsOnGoal { get; init; }

    public string? LogoUrl { get; init; }
}
