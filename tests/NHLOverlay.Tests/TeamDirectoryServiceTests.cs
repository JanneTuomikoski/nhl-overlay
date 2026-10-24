using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public sealed class TeamDirectoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Parses_real_standings()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.Standings));
        var standings = await api.GetStandingsAsync(CancellationToken.None);

        Assert.Equal(32, standings.Count);
        var car = standings.Single(s => s.Abbrev == "CAR");
        Assert.Equal("Carolina Hurricanes", car.FullName);
        Assert.Equal("Hurricanes", car.CommonName);
        Assert.Equal("M", car.Division);
        Assert.Equal("Metropolitan", car.DivisionName);
        Assert.Equal(18, car.Points);
        Assert.Equal(12, car.GamesPlayed);
        Assert.Equal(1, car.LeagueRank);
        Assert.Equal("OT1", car.Streak);
        Assert.Matches(@"^\d+-\d+-\d+$", car.LastTen);
        Assert.Equal(standings.Select(s => s.Abbrev).Order(), NhlTeams.BuiltIn.Select(t => t.Abbrev).Order());
    }

    [Fact]
    public async Task Starts_with_built_in_teams_then_uses_and_caches_live_standings()
    {
        var api = new FakeNhlApi { Standings = [new TeamStanding { Abbrev = "EDM", FullName = "Edmonton Oilers", Points = 20, GamesPlayed = 12 }] };
        using var directory = new TeamDirectoryService(api, _directory, TimeProvider.System);
        Assert.Equal(32, directory.Teams.Count);
        Assert.False(directory.Find("EDM")!.HasStats);

        await directory.RefreshAsync(CancellationToken.None);

        Assert.Equal(20, directory.Find("edm")!.Points);
        Assert.Equal(32, directory.Teams.Count); // missing teams filled from the built-in list
        Assert.True(File.Exists(Path.Combine(_directory, "standings.json")));

        // A new instance (next app start) loads the cache without any network call.
        var offline = new FakeNhlApi { StandingsFailure = new NhlApiException("offline") };
        using var restarted = new TeamDirectoryService(offline, _directory, TimeProvider.System);
        restarted.Start(new GameStateService());
        Assert.Equal(20, restarted.Find("EDM")!.Points);
    }

    [Fact]
    public async Task Keeps_existing_data_when_standings_are_empty_or_failing()
    {
        var api = new FakeNhlApi { Standings = [new TeamStanding { Abbrev = "EDM", FullName = "Edmonton Oilers", Points = 20, GamesPlayed = 12 }] };
        using var directory = new TeamDirectoryService(api, _directory, TimeProvider.System);
        await directory.RefreshAsync(CancellationToken.None);

        api.Standings = [];
        await directory.RefreshAsync(CancellationToken.None);
        api.StandingsFailure = new NhlApiException("down");
        await directory.RefreshAsync(CancellationToken.None);

        Assert.Equal(20, directory.Find("EDM")!.Points);
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
