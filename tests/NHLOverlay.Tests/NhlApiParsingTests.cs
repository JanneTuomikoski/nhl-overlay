using System.Net;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.Services.Api;

namespace NHLOverlay.Tests;

public class NhlApiParsingTests
{
    private static async Task<IReadOnlyList<HockeyGame>> Parse(string fixture)
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(fixture));
        return await api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None);
    }

    [Fact]
    public async Task Parses_live_game_with_clock_shots_and_period()
    {
        var games = await Parse(Fixtures.LiveDay);

        Assert.Equal(12, games.Count);
        var live = games.Single(g => g.Id == 2026020169);
        Assert.Equal(GameStatus.Live, live.Status);
        Assert.Equal("LAK", live.Away.Abbrev);
        Assert.Equal("NJD", live.Home.Abbrev);
        Assert.Equal(0, live.Away.Score);
        Assert.Equal(3, live.Away.ShotsOnGoal);
        Assert.Equal(1, live.Period);
        Assert.Equal(PeriodType.Regulation, live.PeriodType);
        Assert.Equal("08:07", live.TimeRemaining);
        Assert.Equal(487, live.SecondsRemaining);
        Assert.Equal(new DateOnly(2026, 10, 24), live.NhlDate);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero), live.StartTimeUtc);
        Assert.Equal("https://www.nhl.com/gamecenter/lak-vs-njd/2026/10/24/2026020169", live.GameCenterUrl);
    }

    [Fact]
    public async Task Parses_scheduled_games_without_scores()
    {
        var games = await Parse(Fixtures.LiveDay);

        var scheduled = games.Where(g => g.Status == GameStatus.Scheduled).ToList();
        Assert.Equal(10, scheduled.Count);
        Assert.All(scheduled, g => Assert.False(g.HasScore));
        Assert.All(scheduled, g => Assert.Empty(g.Goals));
    }

    [Fact]
    public async Task Parses_final_game_goals_with_player_ids()
    {
        var games = await Parse(Fixtures.LiveDay);

        var final = games.Single(g => g.Id == 2026020168);
        Assert.Equal(GameStatus.Final, final.Status);
        Assert.Equal(PeriodType.Regulation, final.LastPeriodType);
        Assert.Equal(9, final.Goals.Count);
        Assert.Equal(final.Away.Score + final.Home.Score, final.Goals.Count);
        Assert.All(final.Goals, g => Assert.True(g.PlayerId > 0));

        var first = final.Goals[0];
        Assert.Equal(8484387, first.PlayerId);
        Assert.Equal("Matvei Michkov", first.ScorerName);
        Assert.Equal("M. Michkov", first.ScorerShortName);
        Assert.Equal("pp", first.Strength);
        Assert.Equal("PHI", first.TeamAbbrev);
        Assert.Equal("13:59", first.TimeInPeriod);
        Assert.Equal(1, first.HomeScore);
    }

    [Fact]
    public async Task Identifies_overtime_and_shootout_finals_and_flags_shootout_goals()
    {
        var games = await Parse(Fixtures.FinalDay);

        Assert.All(games, g => Assert.Equal(GameStatus.Final, g.Status));
        Assert.Equal(PeriodType.Overtime, games.Single(g => g.Id == 2026020159).LastPeriodType);

        var shootout = games.Single(g => g.Id == 2026020163);
        Assert.Equal(PeriodType.Shootout, shootout.LastPeriodType);
        Assert.Single(shootout.Goals, g => g.IsShootout);
        Assert.Equal(10, shootout.Goals.Count(g => !g.IsShootout)); // 5-5 after overtime

        Assert.Contains(games.SelectMany(g => g.Goals), g => g.IsEmptyNet);
    }

    [Fact]
    public async Task Tolerates_missing_and_unknown_fields()
    {
        const string json = """
            { "games": [
                { "id": 1, "gameState": "LIVE", "someNewField": { "x": 1 } },
                { "id": 2, "gameState": "FUT", "awayTeam": { "abbrev": "EDM" }, "homeTeam": null, "goals": [ { "period": 1 } ] },
                { "id": 0 }
            ] }
            """;
        using var api = Fixtures.ApiReturning(json);
        var games = await api.GetGamesForDateAsync(new DateOnly(2026, 1, 1), CancellationToken.None);

        Assert.Equal(2, games.Count);
        Assert.Equal(GameStatus.Live, games[0].Status);
        Assert.Equal("?", games[0].Away.Abbrev);
        Assert.Equal("EDM", games[1].Away.Abbrev);
        Assert.Empty(games[1].Goals); // goal without a player id is ignored
    }

    [Fact]
    public async Task Empty_response_yields_no_games()
    {
        using var api = Fixtures.ApiReturning("""{ "games": [] }""");
        Assert.Empty(await api.GetGamesForDateAsync(new DateOnly(2026, 7, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Server_error_surfaces_retry_after()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Fixtures.Json("{}", HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        using var api = new NhlApiService(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<NhlApiException>(() => api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(90), ex.RetryAfter);
        Assert.Equal("https://api-web.nhle.com/v1/score/2026-10-24", handler.Requests.Single().ToString());
    }

    [Fact]
    public async Task Malformed_json_becomes_api_exception()
    {
        using var api = Fixtures.ApiReturning("<html>maintenance</html>");
        await Assert.ThrowsAsync<NhlApiException>(() => api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None));
    }

    [Fact]
    public async Task Game_center_link_must_be_a_site_relative_path()
    {
        using var api = Fixtures.ApiReturning("""
            { "games": [
                { "id": 1, "gameCenterLink": "/gamecenter/lak-vs-njd/2026/10/24/1" },
                { "id": 2, "gameCenterLink": "@evil.example/x" },
                { "id": 3, "gameCenterLink": "//evil.example/x" },
                { "id": 4, "gameCenterLink": "https://evil.example/x" },
                { "id": 5, "gameCenterLink": "" } ] }
            """);
        var games = await api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None);

        Assert.Equal("https://www.nhl.com/gamecenter/lak-vs-njd/2026/10/24/1", games.Single(g => g.Id == 1).GameCenterUrl);
        Assert.All(games.Where(g => g.Id != 1), g => Assert.Null(g.GameCenterUrl));
    }

    [Theory]
    [InlineData("FUT", "OK", "REG", false, GameStatus.Scheduled)]
    [InlineData("PRE", "OK", "REG", false, GameStatus.PreGame)]
    [InlineData("LIVE", "OK", "REG", false, GameStatus.Live)]
    [InlineData("CRIT", "OK", "REG", false, GameStatus.Live)]
    [InlineData("LIVE", "OK", "REG", true, GameStatus.Intermission)]
    [InlineData("CRIT", "OK", "OT", false, GameStatus.Overtime)]
    [InlineData("LIVE", "OK", "SO", false, GameStatus.Shootout)]
    [InlineData("FINAL", "OK", "REG", false, GameStatus.Final)]
    [InlineData("OFF", "OK", "OT", false, GameStatus.Final)]
    [InlineData("FUT", "PPD", null, false, GameStatus.Postponed)]
    [InlineData("LIVE", "SUSP", "REG", false, GameStatus.Suspended)]
    [InlineData("FUT", "CNCL", null, false, GameStatus.Cancelled)]
    [InlineData("SOMETHING_NEW", "OK", null, false, GameStatus.Unknown)]
    [InlineData(null, null, null, false, GameStatus.Unknown)]
    public void Derives_status_from_nhl_state(string? gameState, string? scheduleState, string? periodType, bool intermission, GameStatus expected)
    {
        var status = NhlMapper.DeriveStatus(gameState, scheduleState, NhlMapper.ParsePeriodType(periodType), intermission);
        Assert.Equal(expected, status);
    }
}
