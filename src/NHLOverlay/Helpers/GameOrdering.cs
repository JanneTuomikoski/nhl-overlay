using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>Default card ordering: live games, then upcoming, then finished, then postponed/cancelled.</summary>
public static class GameOrdering
{
    public static IReadOnlyList<HockeyGame> Sort(IEnumerable<HockeyGame> games) =>
        games.OrderBy(Rank)
             .ThenBy(g => g.StartTimeUtc)
             .ThenBy(g => g.Id)
             .ToList();

    private static int Rank(HockeyGame game) => game.Status switch
    {
        _ when game.Status.IsInProgress() => 0,
        _ when game.Status.IsUpcoming() => 1,
        GameStatus.Final => 2,
        GameStatus.Unknown => 3,
        _ => 4,
    };
}
