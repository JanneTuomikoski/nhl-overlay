using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Hat-Trick Watch: players with two or more goals in a single game, counted from that game's own goal list
/// (never season totals; shootout goals excluded; keyed by stable NHL player ids, so corrected or reassigned
/// goals are reflected on the next refresh). When a game's goal list is temporarily missing even though its
/// score says goals were scored, the last known result is kept instead of dropping the indicator.
/// </summary>
public sealed class HatTrickTracker
{
    private readonly Dictionary<long, IReadOnlyList<PlayerGameGoals>> _lastKnown = [];

    /// <summary>Players with at least two goals in <paramref name="game"/>, most goals first.</summary>
    public static IReadOnlyList<PlayerGameGoals> Analyze(HockeyGame game) =>
        game.Goals
            .Where(g => !g.IsShootout && g.PlayerId > 0)
            .GroupBy(g => g.PlayerId)
            .Select(g => new PlayerGameGoals(game.Id, g.Key, g.Last().ScorerName, g.Last().ScorerShortName, g.Last().TeamAbbrev, g.Count()))
            .Where(p => p.Goals >= 2)
            .OrderByDescending(p => p.Goals)
            .ThenBy(p => p.ShortName, StringComparer.Ordinal)
            .ToList();

    /// <summary>Computes results for every game in the snapshot. Not thread-safe; call from one publisher.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<PlayerGameGoals>> Update(IReadOnlyCollection<HockeyGame> games)
    {
        var result = new Dictionary<long, IReadOnlyList<PlayerGameGoals>>();
        var loaded = new HashSet<long>();
        foreach (var game in games)
        {
            if (!loaded.Add(game.Id)) continue;
            var goalsMissing = game.Goals.Count == 0 && (game.Away.Score ?? 0) + (game.Home.Score ?? 0) > 0;
            var players = goalsMissing && _lastKnown.TryGetValue(game.Id, out var previous) ? previous : Analyze(game);
            _lastKnown[game.Id] = players;
            if (players.Count > 0) result[game.Id] = players;
        }

        // Forget games that are no longer loaded.
        foreach (var id in _lastKnown.Keys.Where(id => !loaded.Contains(id)).ToList())
            _lastKnown.Remove(id);
        return result;
    }
}
