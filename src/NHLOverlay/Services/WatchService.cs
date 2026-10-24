using System.Windows.Threading;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Runs the shutout, milestone and point-streak watches on every scoreboard update: decides which players are in
/// scope, prefetches their career stats (favorite teams' and watched-country skaters playing today), requests
/// boxscores for shutout watches, and hands the resulting events to the notifier.
/// </summary>
public sealed class WatchService
{
    private readonly GameStateService _state;
    private readonly SettingsService _settings;
    private readonly PlayerStatsService _stats;
    private readonly BoxscoreService _boxscores;
    private readonly RosterService _rosters;
    private readonly Action<IReadOnlyList<GameEvent>> _deliver;
    private readonly Dispatcher _dispatcher;
    private readonly PlayerWatchTracker _players = new();
    private readonly ShutoutTracker _shutouts = new();
    private readonly Lock _gate = new();

    public WatchService(
        GameStateService state,
        SettingsService settings,
        PlayerStatsService stats,
        BoxscoreService boxscores,
        RosterService rosters,
        Action<IReadOnlyList<GameEvent>> deliver,
        Dispatcher dispatcher)
    {
        _state = state;
        _settings = settings;
        _stats = stats;
        _boxscores = boxscores;
        _rosters = rosters;
        _deliver = deliver;
        _dispatcher = dispatcher;
        state.Changed += (_, snapshot) => Run(snapshot);
        stats.Changed += (_, _) =>
        {
            Run(_state.Current);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        boxscores.Changed += (_, _) => Run(_state.Current);
    }

    /// <summary>Raised (background thread) when card lines may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Card lines: shutout watch and players close to a milestone. UI thread.</summary>
    public IReadOnlyList<string> CardLines(HockeyGame game)
    {
        if (!game.IsNhl) return []; // career stats and boxscores are NHL data
        var watch = _settings.Current.PlayerWatch;
        var lines = new List<string>();
        if (watch.ShutoutWatch && ShutoutTracker.CardLine(game, _boxscores.Get(game.Id)) is { } shutout)
            lines.Add(shutout);
        if (watch.Milestones)
            lines.AddRange(PlayerWatchTracker.Approaching(game, ScopePlayers(game), _stats.Get).Take(2));
        return lines;
    }

    private void Run(ScoreboardSnapshot snapshot)
    {
        if (!snapshot.HasData) return;
        var settings = _settings.Current.PlayerWatch;
        var events = new List<GameEvent>();
        lock (_gate)
        {
            var games = snapshot.Games.Where(g => g.IsNhl).ToList();

            if (settings.Milestones || settings.PointStreaks)
            {
                var scope = games.SelectMany(ScopePlayers).Select(p => p.Id).ToHashSet();
                _stats.Request(scope); // prefetch: favorite teams' and watched-country skaters playing today
                Func<long, bool> inScope = settings.Scope == PlayerWatchScope.AllPlayers ? _ => true : scope.Contains;
                var result = _players.Update(games, inScope, _stats.Get, settings);
                events.AddRange(result.Events);
                _stats.Request(result.NeedStats);
            }

            if (settings.ShutoutWatch)
            {
                var result = _shutouts.Update(games, _boxscores.Get);
                events.AddRange(result.Events);
                foreach (var game in result.NeedBoxscore) _boxscores.Request(game);
                // Keep the goalie line on cards fresh while a watch is on.
                foreach (var game in games.Where(g => ShutoutTracker.Shutouts(g).Any())) _boxscores.Request(game);
            }
        }

        if (events.Count > 0) _dispatcher.InvokeAsync(() => _deliver(events));
    }

    /// <summary>Skaters on favorite teams and from watched countries in a game.</summary>
    private IEnumerable<RosterPlayer> ScopePlayers(HockeyGame game)
    {
        var settings = _settings.Current;
        var favorites = settings.Teams.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> countries = settings.CountryWatch.Enabled ? [.. settings.CountryWatch.Countries] : [];
        return new[] { game.Away.Abbrev, game.Home.Abbrev }
            .SelectMany(team => _rosters.Team(team))
            .Where(p => !p.IsGoalie && (favorites.Contains(p.Team) || countries.Contains(p.Country)));
    }
}
