using NHLOverlay.Helpers;
using NHLOverlay.Models;
using static NHLOverlay.Tests.TestGames;

namespace NHLOverlay.Tests;

public class GameOrderingTests
{
    [Fact]
    public void Orders_live_then_upcoming_then_final_then_inactive()
    {
        var t = new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero);
        var games = new[]
        {
            Game(1, GameStatus.Final, "OFF", t.AddHours(-5)),
            Game(2, GameStatus.Postponed, "FUT", t.AddHours(-1)),
            Game(3, GameStatus.Scheduled, "FUT", t.AddHours(2)),
            Game(4, GameStatus.Live, "LIVE", t),
            Game(5, GameStatus.Scheduled, "FUT", t.AddHours(1)),
            Game(6, GameStatus.Intermission, "LIVE", t.AddHours(-1)),
        };

        var ordered = GameOrdering.Sort(games).Select(g => g.Id);

        Assert.Equal([6L, 4L, 5L, 3L, 1L, 2L], ordered);
    }
}
