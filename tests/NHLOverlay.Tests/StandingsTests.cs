using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public sealed class StandingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));

    private static async Task<IReadOnlyList<TeamStanding>> Standings()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.Standings));
        return await api.GetStandingsAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("CAR", "1st Metropolitan", "6 pts above the cut line, 2 more games played", true)]
    [InlineData("TOR", "Wildcard 1", "2 pts above the cut line, 2 more games played", true)]
    [InlineData("NJD", "Wildcard 2", "level with the cut line", true)]
    [InlineData("MTL", "5th Atlantic", "level with the last wildcard", false)]
    [InlineData("CBJ", "8th Metropolitan", "6 pts behind the last wildcard, 2 games in hand", false)]
    public async Task Describes_the_playoff_race(string team, string position, string detail, bool inSpot)
    {
        var all = await Standings();
        var race = PlayoffRace.Describe(all.Single(t => t.Abbrev == team), all);

        Assert.Equal(position, race.Position);
        Assert.Equal(detail, race.Detail);
        Assert.Equal(inSpot, race.InPlayoffSpot);
    }

    [Fact]
    public async Task Builds_division_and_wildcard_tables()
    {
        var all = await Standings();

        Assert.Equal(["CAR", "NYR", "NYI", "NJD", "WSH", "PIT", "PHI", "CBJ"], PlayoffRace.Division(all, "M").Select(t => t.Abbrev));
        Assert.Equal(["TOR", "NJD", "MTL"], PlayoffRace.Wildcards(all, "E").Take(3).Select(t => t.Abbrev));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(21, "21st")]
    public void Ordinals(int n, string expected) => Assert.Equal(expected, PlayoffRace.Ordinal(n));

    [Fact]
    public async Task Parses_the_playoff_bracket()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read("bracket_2026.json"));
        var series = await api.GetPlayoffBracketAsync(2026, CancellationToken.None);

        Assert.Equal(15, series.Count(s => s.HasTeams));
        var final = series.Single(s => s.Abbrev == "SCF");
        Assert.Equal((4, "CAR", 4, "VGK", 2, "CAR"), (final.Round, final.TopSeed, final.TopSeedWins, final.BottomSeed, final.BottomSeedWins, final.Winner));
        Assert.Equal("ANA", series.Single(s => s.Abbrev == "R1" && s.TopSeed == "EDM").Winner);
    }

    [Fact]
    public async Task Parses_a_team_season_schedule()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read("season_EDM_2026-10-24.json"));
        var games = await api.GetTeamScheduleAsync("EDM", CancellationToken.None);

        Assert.Equal(88, games.Count);
        var next = games.Where(g => g.Status.IsUpcoming()).OrderBy(g => g.StartTimeUtc).First();
        Assert.Equal(2026020176, next.Id);
        Assert.Equal(("TOR", "EDM"), (next.Away.Abbrev, next.Home.Abbrev));
    }

    [Fact]
    public async Task Standings_view_shows_favorites_race_and_next_games()
    {
        var api = new FakeNhlApi { Standings = await Standings() };
        using var schedule = Fixtures.ApiReturning(Fixtures.Read("season_EDM_2026-10-24.json"));
        api.Schedules["EDM"] = await schedule.GetTeamScheduleAsync("EDM", CancellationToken.None);

        using var teams = new TeamDirectoryService(api, _directory, TimeProvider.System);
        await teams.RefreshAsync(CancellationToken.None);
        using var schedules = new ScheduleService(api, _directory, TimeProvider.System);
        await schedules.FetchScheduleAsync("EDM", CancellationToken.None);
        using var logos = new LogoService(_directory, new HttpClient(new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))));
        var view = new StandingsViewModel(teams, schedules, logos);
        var settings = new AppSettings();
        settings.Teams.Favorites = ["EDM"];

        var now = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(3));
        view.Update(settings, new ScoreboardSnapshot { TodayNhlDate = new DateOnly(2026, 10, 24) }, now, Zones.Helsinki, CultureInfo.InvariantCulture);

        var edm = Assert.Single(view.Favorites);
        Assert.Equal("Edmonton Oilers", edm.Name);
        Assert.Equal("02:00  vs TOR", edm.NextGames[0]); // 23:00 UTC is 02:00 in Helsinki, tonight's hockey day
        Assert.Equal(5, edm.NextGames.Count);
        Assert.Equal(["PACIFIC"], view.Divisions.Select(d => d.Title));
        Assert.True(view.Divisions[0].Rows.Single(r => r.Abbrev == "EDM").IsFavorite);
        Assert.Equal(["WESTERN WILDCARD"], view.Wildcards.Select(w => w.Title));
        Assert.False(view.HasSeries); // October: no bracket
    }

    [Fact]
    public void Bracket_is_only_wanted_during_the_playoffs()
    {
        Assert.Null(StandingsViewModel.PlayoffYear(new ScoreboardSnapshot { TodayNhlDate = new DateOnly(2026, 10, 24) }));
        Assert.Equal(2027, StandingsViewModel.PlayoffYear(new ScoreboardSnapshot { TodayNhlDate = new DateOnly(2027, 5, 2) }));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
