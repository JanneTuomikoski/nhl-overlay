using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>Card ordering and filtering.</summary>
public static class GameOrdering
{
    private static readonly IReadOnlySet<string> NoFavorites = new HashSet<string>();

    /// <summary>
    /// Default order: live favorites, other live games, upcoming favorites, other upcoming games,
    /// finished games (favorites first), then postponed/cancelled. Ties break on start time.
    /// </summary>
    public static IReadOnlyList<HockeyGame> Sort(IEnumerable<HockeyGame> games, IReadOnlySet<string>? favorites = null, GameSortOrder order = GameSortOrder.Default)
    {
        favorites ??= NoFavorites;
        var sorted = order == GameSortOrder.StartTime
            ? games.OrderBy(g => g.StartTimeUtc)
            : games.OrderBy(g => Rank(g, favorites)).ThenBy(g => g.StartTimeUtc);
        return sorted.ThenBy(g => g.Id).ToList();
    }

    public static bool IsFavorite(HockeyGame game, IReadOnlySet<string> favorites) =>
        favorites.Contains(game.Away.Abbrev) || favorites.Contains(game.Home.Abbrev);

    public static IEnumerable<HockeyGame> Filter(IEnumerable<HockeyGame> games, GameFilter filter, IReadOnlySet<string> favorites, Func<HockeyGame, bool> hasHatTrickWatch) =>
        filter switch
        {
            GameFilter.Favorites => games.Where(g => IsFavorite(g, favorites)),
            GameFilter.Live => games.Where(g => g.Status.IsInProgress()),
            GameFilter.LiveAndUpcoming => games.Where(g => g.Status.IsInProgress() || g.Status.IsUpcoming()),
            GameFilter.HatTrickWatch => games.Where(hasHatTrickWatch),
            GameFilter.Completed => games.Where(g => g.Status.IsFinal()),
            _ => games,
        };

    public static string Describe(GameFilter filter) => filter switch
    {
        GameFilter.Favorites => "Favorite teams",
        GameFilter.Live => "Live games",
        GameFilter.LiveAndUpcoming => "Live and upcoming",
        GameFilter.HatTrickWatch => "Hat-trick watch",
        GameFilter.Completed => "Completed games",
        _ => "All games",
    };

    private static int Rank(HockeyGame game, IReadOnlySet<string> favorites)
    {
        var favorite = IsFavorite(game, favorites);
        if (game.Status.IsInProgress()) return favorite ? 0 : 1;
        if (game.Status.IsUpcoming()) return favorite ? 2 : 3;
        if (game.Status.IsFinal()) return favorite ? 4 : 5;
        return game.Status == GameStatus.Unknown ? 6 : 7;
    }
}
