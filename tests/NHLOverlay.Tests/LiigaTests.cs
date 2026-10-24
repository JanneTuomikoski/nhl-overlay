using NHLOverlay.Models;
using NHLOverlay.Services;
using static NHLOverlay.Tests.TestGames;

namespace NHLOverlay.Tests;

public class LiigaTests
{
    private static async Task<IReadOnlyList<HockeyGame>> Day(string json)
    {
        using var api = new LiigaApiService(new HttpClient(new StubHandler(_ => Fixtures.Json(json))));
        return await api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None);
    }

    [Fact]
    public async Task Parses_a_real_liiga_day()
    {
        var games = await Day(Fixtures.Read("liiga_2026-10-24.json"));

        Assert.Equal(8, games.Count);
        Assert.All(games, g => Assert.Equal(League.Liiga, g.League));
        Assert.All(games, g => Assert.Equal(GameStatus.Final, g.Status));
        Assert.All(games, g => Assert.Equal("OFF", g.RawGameState));

        var ilves = games.Single(g => g.Home.Abbrev == "ILV");
        Assert.Equal(("JUK", "Jukurit", 2, 1), (ilves.Away.Abbrev, ilves.Away.Name, ilves.Away.Score, ilves.Home.Score));
        Assert.Equal(PeriodType.Shootout, ilves.LastPeriodType);
        Assert.Equal(2, ilves.Goals.Count(g => !g.IsShootout));
        var shootout = Assert.Single(ilves.Goals, g => g.IsShootout);
        Assert.Equal((2, 1), (shootout.AwayScore, shootout.HomeScore));
        Assert.Contains("width=128", ilves.Home.LogoUrl);
    }

    [Fact]
    public async Task Skips_goal_events_that_did_not_change_the_score()
    {
        var games = await Day(Fixtures.Read("liiga_2026-10-24.json"));
        var tps = games.Single(g => g.Home.Abbrev == "TPS");

        // TPS 1-3 Lukko: the second TPS event is "RL0", a missed penalty shot.
        Assert.Equal(4, tps.Goals.Count);
        Assert.Equal(1, tps.Goals.Count(g => g.TeamAbbrev == "TPS"));
        Assert.All(tps.Goals, g => Assert.Equal(PeriodType.Regulation, g.PeriodType));
    }

    [Fact]
    public async Task Converts_elapsed_game_time_to_period_clock()
    {
        var games = await Day(Fixtures.Read("liiga_2026-10-24.json"));
        var ilves = games.Single(g => g.Home.Abbrev == "ILV");

        var first = ilves.Goals[0]; // gameTime 2439 in the 3rd period
        Assert.Equal((3, "00:39"), (first.Period, first.TimeInPeriod));
        Assert.Equal("ÄSS", games.Single(g => g.Home.Abbrev == "JYP").Away.Abbrev);
    }

    [Theory]
    [InlineData(2, 1500, GameStatus.Live, "15:00")]
    [InlineData(1, 1200, GameStatus.Intermission, "00:00")]
    [InlineData(4, 3720, GameStatus.Overtime, "03:00")]
    public async Task Maps_live_states(int period, int gameTime, GameStatus status, string clock)
    {
        var json = $$"""
            { "games": [ {
              "id": 1, "start": "2026-10-24T15:30:00Z", "started": true, "ended": false,
              "currentPeriod": {{period}}, "gameTime": {{gameTime}},
              "periods": [
                { "index": 1, "category": "NORMAL", "startTime": 0, "endTime": 1200 },
                { "index": 2, "category": "NORMAL", "startTime": 1200, "endTime": 2400 },
                { "index": 3, "category": "NORMAL", "startTime": 2400, "endTime": 3600 },
                { "index": 4, "category": "OVERTIME", "startTime": 3600, "endTime": 3900 } ],
              "homeTeam": { "teamId": "362185137:tappara", "teamName": "Tappara", "goals": 1, "goalEvents": [] },
              "awayTeam": { "teamId": "651304385:tps", "teamName": "TPS", "goals": 1, "goalEvents": [] } } ] }
            """;
        var game = Assert.Single(await Day(json));

        Assert.Equal(status, game.Status);
        Assert.Equal(clock, game.TimeRemaining);
        Assert.Equal(("TPS", "TAP"), (game.Away.Abbrev, game.Home.Abbrev));
        Assert.Equal("LIVE", game.RawGameState);
    }

    [Fact]
    public async Task Upcoming_game_has_no_score()
    {
        var json = """{ "games": [ { "id": 2, "start": "2026-10-28T16:30:00Z", "started": false, "ended": false, "homeTeam": { "teamId": "x:hifk", "teamName": "HIFK", "goals": 0 }, "awayTeam": { "teamId": "y:kalpa", "teamName": "KalPa", "goals": 0 } } ] }""";
        var game = Assert.Single(await Day(json));

        Assert.Equal(GameStatus.Scheduled, game.Status);
        Assert.False(game.HasScore);
        Assert.Equal(new DateOnly(2026, 10, 28), game.NhlDate);
    }

    [Fact]
    public async Task Polling_merges_liiga_games_and_a_liiga_failure_does_not_mark_nhl_stale()
    {
        var morning = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
        var today = new DateOnly(2026, 10, 25);
        var nhl = new FakeNhlApi();
        nhl.Games[today] = [Game(1, GameStatus.Scheduled, "FUT", morning.AddHours(15))];
        var liiga = new FakeLiiga();
        liiga.Games[today] = [Game(9, GameStatus.Scheduled, "FUT", morning.AddHours(8)) with { League = League.Liiga }];
        var state = new GameStateService();
        var enabled = true;
        using var service = new ScorePollingService(nhl, state, () => TimeSpan.FromSeconds(30), new ManualTimeProvider(morning),
            Zones.Helsinki, Zones.Eastern, liiga, () => enabled);

        await service.PollOnceAsync(CancellationToken.None);
        Assert.Equal([1L, 9L], state.Current.Games.Select(g => g.Id).Order());

        liiga.Failure = new NhlApiException("liiga down");
        await service.PollOnceAsync(CancellationToken.None);
        Assert.False(state.Current.IsStale);
        Assert.Contains(state.Current.Games, g => g.Id == 9); // cached Liiga games stay

        enabled = false;
        await service.PollOnceAsync(CancellationToken.None);
        Assert.Equal([1L], state.Current.Games.Select(g => g.Id));
    }

    private sealed class FakeLiiga : ILiigaClient
    {
        public Dictionary<DateOnly, IReadOnlyList<HockeyGame>> Games { get; } = [];

        public NhlApiException? Failure { get; set; }

        public Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly date, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            return Task.FromResult(Games.TryGetValue(date, out var games) ? games : (IReadOnlyList<HockeyGame>)[]);
        }
    }
}
