namespace NHLOverlay.Models;

/// <summary>Goals by one player in one game (shootout goals excluded).</summary>
public sealed record PlayerGameGoals(long GameId, long PlayerId, string Name, string ShortName, string TeamAbbrev, int Goals)
{
    /// <summary>Exactly two goals: one more makes a hat trick.</summary>
    public bool IsWatch => Goals == 2;

    public bool IsHatTrick => Goals >= 3;
}
