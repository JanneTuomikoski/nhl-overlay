using System.Globalization;
using System.Windows.Threading;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public sealed class CountryWatchTests : IDisposable
{
    private static readonly IReadOnlySet<string> Finland = new HashSet<string> { "FIN" };
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));

    private static readonly RosterPlayer Barkov = new(1, "Aleksander", "Barkov", "C", "FIN", "FLA");
    private static readonly RosterPlayer Lundell = new(2, "Anton", "Lundell", "C", "FIN", "FLA");
    private static readonly RosterPlayer Reinhart = new(3, "Sam", "Reinhart", "C", "CAN", "FLA");
    private static readonly RosterPlayer Saros = new(4, "Juuse", "Saros", "G", "FIN", "NSH");
    private static readonly RosterPlayer Forsberg = new(5, "Filip", "Forsberg", "L", "SWE", "NSH");

    private static IReadOnlyList<RosterPlayer> Roster(string team) => new[] { Barkov, Lundell, Reinhart, Saros, Forsberg }.Where(p => p.Team == team).ToList();

    private static GameGoal Goal(long scorer, string team, int away, int home, params long[] assists) => new()
    {
        PlayerId = scorer,
        ScorerName = $"Player {scorer}",
        ScorerShortName = $"P. {scorer}",
        TeamAbbrev = team,
        Period = 1,
        PeriodType = PeriodType.Regulation,
        TimeInPeriod = $"{away + home:00}:00",
        AwayScore = away,
        HomeScore = home,
        Assists = assists.Select(a => new GoalAssist(a, $"A. {a}")).ToList(),
    };

    private static HockeyGame Game(params GameGoal[] goals) => new()
    {
        Id = 9,
        Status = GameStatus.Live,
        RawGameState = "LIVE",
        Away = new HockeyTeam { Abbrev = "FLA", Name = "Panthers", Score = goals.Count(g => g.TeamAbbrev == "FLA") },
        Home = new HockeyTeam { Abbrev = "NSH", Name = "Predators", Score = goals.Count(g => g.TeamAbbrev == "NSH") },
        Period = 1,
        PeriodType = PeriodType.Regulation,
        Goals = goals,
    };

    [Fact]
    public async Task Parses_roster_birth_countries()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read("roster_EDM_2026-10-24.json"));
        var roster = await api.GetRosterAsync("EDM", CancellationToken.None);

        Assert.Equal(29, roster.Count);
        var kapanen = roster.Single(p => p.Id == 8477953);
        Assert.Equal("FIN", kapanen.Country);
        Assert.Equal("R", kapanen.Position);
        Assert.Equal("EDM", kapanen.Team);
        Assert.Equal("K. Kapanen", kapanen.ShortName);
    }

    [Fact]
    public async Task Parses_boxscore_goalies()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read("boxscore_2026020168.json"));
        var box = await api.GetBoxscoreAsync(2026020168, "ANA", "PHI", CancellationToken.None);

        Assert.False(box.IsOfficial); // captured as FINAL, before the NHL marked it OFF; it will be refreshed
        var dostal = box.Goalies.Single(g => g.PlayerId == 8480843);
        Assert.Equal(("ANA", 28, 32, 4, "W"), (dostal.Team, dostal.Saves, dostal.ShotsAgainst, dostal.GoalsAgainst, dostal.Decision));
        Assert.True(dostal.Played);
        Assert.False(box.Goalies.Single(g => g.PlayerId == 8478024).Played); // Husso dressed but did not play
    }

    [Fact]
    public async Task Parses_assists()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.LiveDay));
        var games = await api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None);

        var first = games.Single(g => g.Id == 2026020168).Goals[0];
        Assert.Equal([8482142L, 8481533L], first.Assists.Select(a => a.PlayerId));
        Assert.Equal("J. Drysdale", first.Assists[0].Name);
    }

    [Fact]
    public void Finds_watched_players_with_points_and_goalie_lines()
    {
        var game = Game(Goal(1, "FLA", 1, 0, 2, 3), Goal(3, "FLA", 2, 0, 1), Goal(5, "NSH", 2, 1));
        var box = new GameBoxscore(9, [new GoalieLine(4, "J. Saros", "NSH", 20, 22, 2, "40:00", null)], false);

        var players = CountryWatch.ForGame(game, Finland, Roster, id => id == 9 ? box : null);

        Assert.Equal(["A. Barkov", "A. Lundell", "J. Saros"], players.Select(p => p.ShortName));
        Assert.Equal("1+1", players[0].Stat);
        Assert.Equal("0+1", players[1].Stat);
        Assert.Equal("20/22", players[2].Stat);
        Assert.DoesNotContain(players, p => p.Country != "FIN");
    }

    [Fact]
    public void Shootout_goals_do_not_count_as_points()
    {
        var shootout = Goal(1, "FLA", 3, 2) with { PeriodType = PeriodType.Shootout, Period = 5 };
        var players = CountryWatch.ForGame(Game(shootout), Finland, Roster, _ => null);
        Assert.False(players.Single(p => p.PlayerId == 1).HasStat);
    }

    [Fact]
    public void Detector_announces_goals_and_assists_by_watched_players_once()
    {
        var detector = new GameEventDetector();
        RosterPlayer? Find(long id) => new[] { Barkov, Lundell, Reinhart, Saros, Forsberg }.FirstOrDefault(p => p.Id == id);
        IReadOnlyList<GameEvent> Detect(HockeyGame g) => detector.Detect(new ScoreboardSnapshot { HasData = true, Games = [g] }, Find, Finland);

        Detect(Game());
        var events = Detect(Game(Goal(3, "FLA", 1, 0, 1, 2)));

        var points = events.Where(e => e.Kind == GameEventKind.CountryPlayerPoint).ToList();
        Assert.Equal(2, points.Count);
        Assert.Contains(points, e => e.Title == "FIN ASSIST  A. Barkov · FLA 1-0 NSH" && e.GoalKey is null);
        Assert.Contains(points, e => e.Body == "Anton Lundell (FLA) · 0+1 tonight");

        var own = Detect(Game(Goal(3, "FLA", 1, 0, 1, 2), Goal(1, "FLA", 2, 0)));
        var barkovGoal = Assert.Single(own, e => e.Kind == GameEventKind.CountryPlayerPoint);
        Assert.Equal("Finnish goal! Aleksander Barkov scores for the Panthers.", barkovGoal.Speech);
        Assert.Equal(own.Single(e => e.Kind == GameEventKind.Goal).GoalKey, barkovGoal.GoalKey);

        Assert.DoesNotContain(Detect(Game(Goal(3, "FLA", 1, 0, 1, 2), Goal(1, "FLA", 2, 0))), e => e.Kind == GameEventKind.CountryPlayerPoint);
    }

    [Fact]
    public void Detector_announces_a_live_goal_point_once_the_roster_arrives()
    {
        var detector = new GameEventDetector();
        var rosterLoaded = false;
        RosterPlayer? Find(long id) => rosterLoaded ? new[] { Barkov, Reinhart }.FirstOrDefault(p => p.Id == id) : null;
        IReadOnlyList<GameEvent> Detect(HockeyGame g) => detector.Detect(new ScoreboardSnapshot { HasData = true, Games = [g] }, Find, Finland);

        Detect(Game());
        Assert.DoesNotContain(Detect(Game(Goal(1, "FLA", 1, 0))), e => e.Kind == GameEventKind.CountryPlayerPoint);

        rosterLoaded = true;
        Assert.Single(Detect(Game(Goal(1, "FLA", 1, 0))), e => e.Kind == GameEventKind.CountryPlayerPoint);
        Assert.DoesNotContain(Detect(Game(Goal(1, "FLA", 1, 0))), e => e.Kind == GameEventKind.CountryPlayerPoint);
    }

    [Fact]
    public void Detector_never_replays_baseline_goals_when_the_roster_arrives()
    {
        var detector = new GameEventDetector();
        var rosterLoaded = false;
        RosterPlayer? Find(long id) => rosterLoaded ? Barkov.Id == id ? Barkov : null : null;
        IReadOnlyList<GameEvent> Detect(HockeyGame g) => detector.Detect(new ScoreboardSnapshot { HasData = true, Games = [g] }, Find, Finland);

        Detect(Game(Goal(1, "FLA", 1, 0))); // restart after Barkov scored
        rosterLoaded = true;

        Assert.DoesNotContain(Detect(Game(Goal(1, "FLA", 1, 0))), e => e.Kind == GameEventKind.CountryPlayerPoint);
    }

    [Fact]
    public void Notifier_skips_the_country_alert_when_the_goal_alert_covers_the_same_goal()
    {
        var settings = new SettingsService(_directory, isPortable: false);
        settings.Update(s => s.Teams.Favorites = ["FLA"]);
        var toasts = new ToastService(Dispatcher.CurrentDispatcher);
        var service = new NotificationService(new GameStateService(), settings, new RuntimeState(), toasts,
            new SilentSound(), new SilentSpeech(), (_, _) => { }, Dispatcher.CurrentDispatcher);
        var game = Game();
        GameEvent Make(GameEventKind kind, string key, string? goalKey) => new(kind, key, game, key, "", "") { GoalKey = goalKey };

        service.Deliver([Make(GameEventKind.Goal, "goal-1", "goal-1"), Make(GameEventKind.CountryPlayerPoint, "cp-scorer", "goal-1"), Make(GameEventKind.CountryPlayerPoint, "cp-assist", null)]);

        Assert.Equal(["cp-assist", "goal-1"], toasts.Toasts.Select(t => t.Title).Order());
    }

    [Fact]
    public async Task Roster_service_fetches_missing_teams_once_and_reloads_from_disk()
    {
        var api = new FakeNhlApi();
        api.Rosters["FLA"] = [Barkov, Lundell];
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using (var rosters = new RosterService(api, _directory, time))
        {
            await rosters.FetchAsync("FLA", CancellationToken.None);
            Assert.Equal("FIN", rosters.Find(1)!.Country);
        }

        using var restarted = new RosterService(api, _directory, time);
        restarted.Start(new GameStateService());
        Assert.Equal(2, restarted.Team("FLA").Count);
        restarted.Ensure(["FLA"]); // fresh: nothing queued
        await Task.Delay(100);
        Assert.Equal(1, api.RosterRequests.Count(t => t == "FLA"));
    }

    [Fact]
    public async Task A_nationality_override_counts_a_player_born_elsewhere()
    {
        var kapanen = new RosterPlayer(7, "Oliver", "Kapanen", "C", "SWE", "MTL");
        var raty = new RosterPlayer(8, "Aatu", "Räty", "C", "FIN", "MTL");
        var api = new FakeNhlApi();
        api.Rosters["MTL"] = [kapanen, raty];
        using var rosters = new RosterService(api, _directory, new ManualTimeProvider(DateTimeOffset.UtcNow));
        await rosters.FetchAsync("MTL", CancellationToken.None);
        var changes = 0;
        rosters.Changed += (_, _) => changes++;

        rosters.SetCountryOverrides([new CountryOverride(7, "Oliver Kapanen", "MTL", "FIN")]);
        rosters.SetCountryOverrides([new CountryOverride(7, "Oliver Kapanen", "MTL", "FIN")]); // unchanged: no event

        Assert.Equal(1, changes);
        Assert.Equal("FIN", rosters.Find(7)!.Country);
        Assert.All(rosters.Team("MTL"), p => Assert.Equal("FIN", p.Country));
        var game = Game(Goal(7, "MTL", 1, 0)) with { Away = new HockeyTeam { Abbrev = "MTL", Score = 1 } };
        Assert.Contains(CountryWatch.ForGame(game, Finland, rosters.Team, _ => null), p => p.PlayerId == 7 && p.Stat == "1+0");

        // The settings search shows the birth country and ignores accents.
        Assert.Equal("SWE", Assert.Single(rosters.Search("kapa", 5)).Country);
        Assert.Equal(8, Assert.Single(rosters.Search("raty", 5)).Id);
    }

    [Fact]
    public void Country_overrides_are_normalized()
    {
        var watch = new CountryWatchSettings
        {
            Overrides = [new(7, "Oliver Kapanen", "MTL", " swe"), new(7, "Oliver Kapanen", "MTL", "fin"), new(0, "Nobody", "", "FIN"), new(9, "X", "", "FINLAND")],
        }.Normalize();

        Assert.Equal([new CountryOverride(7, "Oliver Kapanen", "MTL", "FIN")], watch.Overrides);
    }

    [Fact]
    public void Card_shows_watched_players()
    {
        var game = Game(Goal(1, "FLA", 1, 0, 2));
        var players = CountryWatch.ForGame(game, Finland, Roster, _ => null);
        var settings = new AppSettings();
        settings.Appearance.ShowTeamLogos = false;
        var card = new GameCardViewModel(9, null!);

        card.Update(game, new CardContext(settings, new HashSet<string>(), _ => null, new Dictionary<long, IReadOnlyList<PlayerGameGoals>>(),
            DateTimeOffset.UtcNow, Zones.Helsinki, CultureInfo.InvariantCulture) { WatchedPlayers = _ => players });

        Assert.Equal("FIN  Barkov 1+0 · Lundell 0+1", card.CountryText);
        Assert.Equal("FIN  A. Barkov 1+0 · A. Lundell 0+1 · J. Saros", card.CountryTextLong);
    }

    [Fact]
    public void Section_header_toggles_its_league_and_collapsing_hides_the_card()
    {
        string? toggled = null;
        var card = new GameCardViewModel(9, null!, toggleSection: key => toggled = key) { SectionKey = "LIIGA FINAL", SectionTitle = "FINAL" };

        card.ToggleSectionCommand.Execute(null);
        card.IsSectionCollapsed = true;

        Assert.Equal("LIIGA FINAL", toggled);
        Assert.False(card.IsBodyVisible);
        Assert.Equal("3 games · 1 live", OverlayViewModel.SectionSummary(
            [Game(), Game() with { Id = 10, Status = GameStatus.Final }, Game() with { Id = 11, Status = GameStatus.Scheduled }]));
    }

    [Fact]
    public void Ended_games_go_last_in_a_final_section_per_league()
    {
        var nhlLive = Game() with { Id = 1 };
        var nhlFinal = Game() with { Id = 2, Status = GameStatus.Final };
        var nhlNext = Game() with { Id = 3, Status = GameStatus.Scheduled };
        var liigaFinal = Game() with { Id = 4, Status = GameStatus.Final, League = League.Liiga };
        var liigaNext = Game() with { Id = 5, Status = GameStatus.Scheduled, League = League.Liiga };

        var placed = OverlayViewModel.PlaceInSections([nhlLive, nhlFinal, nhlNext, liigaFinal, liigaNext]);

        Assert.Equal([1L, 3L, 2L, 5L, 4L], placed.Select(p => p.Game.Id));
        Assert.Equal(["NHL", "NHL", "NHL FINAL", "LIIGA", "LIIGA FINAL"], placed.Select(p => p.Key));
        Assert.Equal(["NHL", "NHL", "FINAL", "LIIGA", "FINAL"], placed.Select(p => p.Title));
        Assert.Equal("LIIGA", placed[^1].Parent);
    }

    [Fact]
    public void One_league_gets_only_a_final_title_and_an_all_final_day_none()
    {
        var mixed = OverlayViewModel.PlaceInSections([Game() with { Id = 1 }, Game() with { Id = 2, Status = GameStatus.Final }]);
        Assert.Equal([null, "FINAL"], mixed.Select(p => p.Title));

        var yesterday = OverlayViewModel.PlaceInSections([Game() with { Id = 1, Status = GameStatus.Final }, Game() with { Id = 2, Status = GameStatus.Final }]);
        Assert.All(yesterday, p => Assert.Null(p.Title));
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

    private sealed class SilentSound : ISoundOutput
    {
        public void Play(AlertSound sound, double volume)
        {
        }
    }

    private sealed class SilentSpeech : ISpeechOutput
    {
        public void Configure(string voice, int rate)
        {
        }

        public void Speak(string text)
        {
        }
    }
}
