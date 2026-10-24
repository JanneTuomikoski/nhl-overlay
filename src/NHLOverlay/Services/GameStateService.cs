using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>Immutable view of the scoreboard at one point in time.</summary>
public sealed record ScoreboardSnapshot
{
    public static readonly ScoreboardSnapshot Empty = new();

    /// <summary>The NHL date played during the user's current hockey day (see <see cref="Helpers.NhlCalendar"/>).</summary>
    public DateOnly TodayNhlDate { get; init; }

    /// <summary>
    /// The "today" view: every game of <see cref="TodayNhlDate"/> plus any game from the previous date that
    /// is still in progress (or not yet official), so a late game never disappears at the day rollover.
    /// </summary>
    public IReadOnlyList<HockeyGame> Games { get; init; } = [];

    /// <summary>All loaded NHL dates (always yesterday and today, plus a date the user navigated to).</summary>
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<HockeyGame>> GamesByDate { get; init; } =
        new Dictionary<DateOnly, IReadOnlyList<HockeyGame>>();

    /// <summary>Hat-Trick Watch results by game id: players with two or more goals (see <see cref="HatTrickTracker"/>).</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<PlayerGameGoals>> HatTricks { get; init; } =
        new Dictionary<long, IReadOnlyList<PlayerGameGoals>>();

    /// <summary>When every request of a refresh last succeeded. Null until the first success.</summary>
    public DateTimeOffset? LastSuccessUtc { get; init; }

    /// <summary>True once any data has been received (the games may still be partially stale).</summary>
    public bool HasData { get; init; }

    /// <summary>True when the most recent refresh failed and cached data is being shown.</summary>
    public bool IsStale { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? LastErrorUtc { get; init; }

    public DateTimeOffset? NextRefreshUtc { get; init; }
}

/// <summary>
/// Single source of truth for the current game state. The polling service publishes snapshots here;
/// the overlay (and later hat-trick detection and notifications) subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class GameStateService
{
    private readonly Lock _gate = new();
    private readonly HatTrickTracker _hatTricks = new();
    private ScoreboardSnapshot _current = ScoreboardSnapshot.Empty;

    public ScoreboardSnapshot Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <summary>Raised on a background thread; UI subscribers must marshal to the dispatcher.</summary>
    public event EventHandler<ScoreboardSnapshot>? Changed;

    /// <summary>Derives per-game analysis (hat tricks) and makes the snapshot current.</summary>
    public void Publish(ScoreboardSnapshot snapshot)
    {
        lock (_gate)
        {
            var allGames = snapshot.GamesByDate.Values.SelectMany(g => g).Concat(snapshot.Games).ToList();
            snapshot = snapshot with { HatTricks = _hatTricks.Update(allGames) };
            _current = snapshot;
        }

        Changed?.Invoke(this, snapshot);
    }
}
