using NHLOverlay.Helpers;
using NHLOverlay.Models;
using static NHLOverlay.Tests.TestGames;

namespace NHLOverlay.Tests;

public class GameOrderingTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 24, 23, 0, 0, TimeSpan.Zero);

    private static HockeyGame Match(long id, GameStatus status, string away, string home, DateTimeOffset start) =>
        Game(id, status, status == GameStatus.Final ? "OFF" : "LIVE", start) with
        {
            Away = new HockeyTeam { Abbrev = away },
            Home = new HockeyTeam { Abbrev = home },
        };

    private static readonly HockeyGame[] Slate =
    [
        Match(1, GameStatus.Final, "ANA", "PHI", T.AddHours(-5)),
        Match(2, GameStatus.Postponed, "BOS", "BUF", T.AddHours(-1)),
        Match(3, GameStatus.Scheduled, "TOR", "EDM", T.AddHours(2)),
        Match(4, GameStatus.Live, "LAK", "NJD", T),
        Match(5, GameStatus.Scheduled, "SJS", "BOS", T.AddHours(1)),
        Match(6, GameStatus.Intermission, "NYR", "OTT", T.AddHours(-1)),
        Match(7, GameStatus.Final, "COL", "DAL", T.AddHours(-4)),
    ];

    [Fact]
    public void Orders_live_then_upcoming_then_final_then_inactive() =>
        Assert.Equal([6L, 4L, 5L, 3L, 1L, 7L, 2L], GameOrdering.Sort(Slate).Select(g => g.Id));

    [Fact]
    public void Favorites_come_first_within_each_group()
    {
        var favorites = new HashSet<string> { "EDM", "NJD", "DAL" };
        Assert.Equal([4L, 6L, 3L, 5L, 7L, 1L, 2L], GameOrdering.Sort(Slate, favorites).Select(g => g.Id));
    }

    [Fact]
    public void Can_sort_purely_by_start_time() =>
        Assert.Equal([1L, 7L, 2L, 6L, 4L, 5L, 3L], GameOrdering.Sort(Slate, new HashSet<string> { "EDM" }, GameSortOrder.StartTime).Select(g => g.Id));

    [Theory]
    [InlineData(GameFilter.All, new long[] { 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData(GameFilter.Favorites, new long[] { 3 })]
    [InlineData(GameFilter.Live, new long[] { 4, 6 })]
    [InlineData(GameFilter.LiveAndUpcoming, new long[] { 3, 4, 5, 6 })]
    [InlineData(GameFilter.HatTrickWatch, new long[] { 4 })]
    [InlineData(GameFilter.Completed, new long[] { 1, 7 })]
    public void Filters_games(GameFilter filter, long[] expected)
    {
        var result = GameOrdering.Filter(Slate, filter, new HashSet<string> { "EDM" }, g => g.Id == 4);
        Assert.Equal(expected, result.Select(g => g.Id).Order());
    }
}
