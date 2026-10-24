namespace NHLOverlay.Models;

/// <summary>A player's regular-season career totals and current point streak, as of the last fetch.</summary>
public sealed record PlayerCareer
{
    public long PlayerId { get; init; }

    public string Name { get; init; } = "";

    public string Team { get; init; } = "";

    public int Goals { get; init; }

    public int Assists { get; init; }

    public int Points { get; init; }

    public int GamesPlayed { get; init; }

    /// <summary>Game ids already counted in the totals (most recent first), used to avoid counting tonight twice.</summary>
    public IReadOnlyList<long> CountedGameIds { get; init; } = [];

    /// <summary>Recent games, most recent first: the season game log when the streak may be long, else the last five.</summary>
    public IReadOnlyList<GameLogEntry> RecentGames { get; init; } = [];

    public DateTimeOffset FetchedUtc { get; init; }

    /// <summary>Current point streak before the given game (the game itself is not counted).</summary>
    public int PointStreakBefore(long gameId) =>
        RecentGames.Where(g => g.GameId != gameId).TakeWhile(g => g.Points > 0).Count();

    public bool Includes(long gameId) => CountedGameIds.Contains(gameId);
}

/// <summary>One game from a player's game log.</summary>
public sealed record GameLogEntry(long GameId, int Goals, int Assists)
{
    public int Points => Goals + Assists;
}
