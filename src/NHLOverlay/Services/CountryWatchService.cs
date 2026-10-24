using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Watched-country players per game (Finnish players by default), combining rosters, goal lists and, for
/// games with a watched goalie, boxscores. Boxscores are only requested for started games that have such a
/// goalie on either roster.
/// </summary>
public sealed class CountryWatchService
{
    private readonly SettingsService _settings;
    private readonly RosterService _rosters;
    private readonly BoxscoreService _boxscores;

    public CountryWatchService(GameStateService state, SettingsService settings, RosterService rosters, BoxscoreService boxscores)
    {
        _settings = settings;
        _rosters = rosters;
        _boxscores = boxscores;
        rosters.Changed += (_, _) =>
        {
            RequestGoalieBoxscores(state.Current);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        boxscores.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        state.Changed += (_, snapshot) => RequestGoalieBoxscores(snapshot);
    }

    /// <summary>Raised (on a background thread) when roster or goalie data changed.</summary>
    public event EventHandler? Changed;

    public RosterPlayer? FindPlayer(long playerId) => _rosters.Find(playerId);

    public IReadOnlySet<string> Countries
    {
        get
        {
            var watch = _settings.Current.CountryWatch;
            return watch.Enabled ? watch.Countries.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
        }
    }

    public IReadOnlyList<WatchedPlayer> PlayersFor(HockeyGame game) =>
        CountryWatch.ForGame(game, Countries, _rosters.Team, _boxscores.Get);

    private void RequestGoalieBoxscores(ScoreboardSnapshot snapshot)
    {
        var countries = Countries;
        if (countries.Count == 0) return;
        foreach (var game in snapshot.Games.Where(g => g.Status.IsInProgress() || g.Status.IsFinal()))
        {
            if (CountryWatch.HasWatchedGoalie(game, countries, _rosters.Team))
                _boxscores.Request(game);
        }
    }
}
