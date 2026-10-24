using System.Globalization;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public sealed class RecapAndSpoilerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));

    private static async Task<IReadOnlyList<HockeyGame>> FinalDay()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.FinalDay));
        return await api.GetGamesForDateAsync(new DateOnly(2026, 10, 22), CancellationToken.None);
    }

    private static Recap Build(IReadOnlyList<HockeyGame> games, string[] favorites, Func<HockeyGame, bool>? shielded = null, Func<long, GameBoxscore?>? box = null) =>
        RecapBuilder.Build(new DateOnly(2026, 10, 22), games, favorites.ToHashSet(), shielded ?? (_ => false), box ?? (_ => null), _ => [], ["FIN"]);

    [Fact]
    public async Task Recap_summarizes_a_real_night()
    {
        var recap = Build(await FinalDay(), ["EDM"]);

        Assert.Equal(12, recap.FinishedGames);
        Assert.Equal(["CAR 5-6 EDM SO  EDM W"], recap.FavoriteResults);
        Assert.Contains("Shutout: BOS · NSH 0-6 BOS", recap.Highlights);
        Assert.Contains("Shutout: PIT · FLA 0-4 PIT", recap.Highlights);
        Assert.Contains("Beyond regulation: 2 in overtime, 1 in a shootout", recap.Highlights);
        Assert.Contains("Highest scoring: CAR 5-6 EDM SO", recap.Highlights);
        Assert.Equal("FINNS", recap.CountryTitle);
    }

    [Fact]
    public async Task Recap_names_the_shutout_goalie_from_the_boxscore()
    {
        var games = await FinalDay();
        var nsh = games.Single(g => g.Away.Abbrev == "NSH" && g.Home.Abbrev == "BOS");
        var box = new GameBoxscore(nsh.Id, [new GoalieLine(1, "J. Swayman", "BOS", 24, 24, 0, "60:00", "W"), new GoalieLine(2, "J. Saros", "NSH", 30, 36, 6, "60:00", "L")], true);

        var recap = Build(games, [], box: id => id == nsh.Id ? box : null);

        Assert.Contains("Shutout: J. Swayman (BOS), 24 saves · NSH 0-6 BOS", recap.Highlights);
    }

    [Fact]
    public async Task Recap_leaves_out_shielded_games()
    {
        var games = await FinalDay();
        var recap = Build(games, ["EDM"], shielded: g => g.Home.Abbrev == "EDM" || g.Away.Abbrev == "EDM");

        Assert.Empty(recap.FavoriteResults);
        Assert.Equal(1, recap.HiddenGames);
        Assert.DoesNotContain(recap.Highlights, h => h.Contains("EDM"));
    }

    [Fact]
    public void Shield_by_team_or_by_game_and_reveal_overrides_it()
    {
        var settings = new SettingsService(_directory, isPortable: false);
        settings.Update(s => s.Spoilers.AlwaysShieldTeams = ["EDM"]);
        var state = new AppStateService(_directory, TimeProvider.System);
        var spoilers = new SpoilerService(state, settings);
        var edm = new HockeyGame { Id = 1, Away = new HockeyTeam { Abbrev = "EDM" }, Home = new HockeyTeam { Abbrev = "TOR" } };
        var other = new HockeyGame { Id = 2, Away = new HockeyTeam { Abbrev = "BOS" }, Home = new HockeyTeam { Abbrev = "MTL" } };

        Assert.True(spoilers.IsShielded(edm));
        Assert.False(spoilers.IsShielded(other));

        spoilers.Shield(other.Id);
        spoilers.Reveal(edm.Id);
        Assert.True(spoilers.IsShielded(other));
        Assert.False(spoilers.IsShielded(edm));

        // Survives a restart.
        var restarted = new SpoilerService(new AppStateService(_directory, TimeProvider.System), settings);
        Assert.True(restarted.IsShielded(other));
        Assert.False(restarted.IsShielded(edm));

        settings.Update(s => s.Spoilers.Enabled = false);
        Assert.False(restarted.IsShielded(other));
    }

    [Fact]
    public async Task Recap_shows_in_the_morning_until_dismissed_and_above_yesterday()
    {
        var games = await FinalDay();
        var settings = new SettingsService(_directory, isPortable: false);
        var state = new AppStateService(_directory, TimeProvider.System);
        var spoilers = new SpoilerService(state, settings);
        var api = new FakeNhlApi();
        using var boxscores = new BoxscoreService(api, TimeProvider.System);
        using var rosters = new RosterService(api, _directory, TimeProvider.System);
        var countryWatch = new CountryWatchService(new GameStateService(), settings, rosters, boxscores);
        var recaps = new RecapService(settings, spoilers, boxscores, countryWatch, state, Zones.Helsinki);
        var snapshot = new ScoreboardSnapshot
        {
            HasData = true,
            TodayNhlDate = new DateOnly(2026, 10, 23),
            GamesByDate = new Dictionary<DateOnly, IReadOnlyList<HockeyGame>> { [new DateOnly(2026, 10, 22)] = games },
        };
        DateTimeOffset Helsinki(int hour) => new(2026, 10, 23, hour, 0, 0, TimeSpan.FromHours(3));

        Assert.NotNull(recaps.For(snapshot, 0, Helsinki(9)));
        Assert.Null(recaps.For(snapshot, 0, Helsinki(15))); // after the default 14:00
        Assert.NotNull(recaps.For(snapshot, -1, Helsinki(15)));
        Assert.Null(recaps.For(snapshot, -2, Helsinki(9)));

        recaps.Dismiss(new DateOnly(2026, 10, 22));
        Assert.Null(recaps.For(snapshot, 0, Helsinki(9)));
        Assert.NotNull(recaps.For(snapshot, -1, Helsinki(9)));
    }

    [Fact]
    public async Task Shielded_card_hides_score_status_details_and_goals()
    {
        var game = (await FinalDay()).Single(g => g.Id == 2026020163); // CAR 5-6 EDM, shootout
        var settings = new AppSettings();
        settings.Appearance.ShowTeamLogos = false;
        var card = new GameCardViewModel(game.Id, null!);
        var context = new CardContext(settings, new HashSet<string>(), _ => null, new Dictionary<long, IReadOnlyList<PlayerGameGoals>>(),
            DateTimeOffset.UtcNow, Zones.Helsinki, CultureInfo.InvariantCulture) { IsShielded = _ => true };

        card.Update(game, context);

        Assert.True(card.IsShielded);
        Assert.False(card.HasScore);
        Assert.Equal("", card.AwayScore);
        Assert.Equal("HIDDEN", card.StatusText);
        Assert.Empty(card.Goals);
        Assert.False(card.HasShots);
        Assert.Equal("Reveal score", card.ShieldActionLabel);
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
