using System.Net;
using System.Text;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public class PlayerWatchTests
{
    private const long McDavid = 8478402;

    private static PlayerCareer Career(long id, int goals, int assists, IEnumerable<int>? recentPoints = null, IEnumerable<long>? counted = null) => new()
    {
        PlayerId = id,
        Name = $"Player {id}",
        Goals = goals,
        Assists = assists,
        Points = goals + assists,
        CountedGameIds = (counted ?? []).ToList(),
        RecentGames = (recentPoints ?? []).Select((p, i) => new GameLogEntry(1000 + i, 0, p)).ToList(),
    };

    private static GameGoal Goal(long scorer, int away, int home, params long[] assists) => new()
    {
        PlayerId = scorer,
        ScorerName = $"Player {scorer}",
        ScorerShortName = $"P. {scorer}",
        TeamAbbrev = "EDM",
        Period = 1,
        PeriodType = PeriodType.Regulation,
        TimeInPeriod = "10:00",
        AwayScore = away,
        HomeScore = home,
        Assists = assists.Select(a => new GoalAssist(a, $"A. {a}")).ToList(),
    };

    private static HockeyGame Game(GameStatus status, params GameGoal[] goals) => new()
    {
        Id = 7,
        Status = status,
        RawGameState = status == GameStatus.Final ? "OFF" : "LIVE",
        Away = new HockeyTeam { Abbrev = "EDM", Name = "Oilers", Score = goals.Length },
        Home = new HockeyTeam { Abbrev = "TOR", Name = "Maple Leafs", Score = 0 },
        Period = 2,
        PeriodType = PeriodType.Regulation,
        SecondsRemaining = 600,
        TimeRemaining = "10:00",
        Goals = goals,
    };

    [Fact]
    public async Task Parses_career_totals_and_reads_the_game_log_for_long_streaks()
    {
        var handler = new StubHandler(r => Fixtures.Json(Fixtures.Read(r.RequestUri!.AbsolutePath.EndsWith("/landing")
            ? "player_8478402.json" : "gamelog_8478402.json")));
        using var api = new NhlApiService(new HttpClient(handler));

        var career = await api.GetPlayerCareerAsync(McDavid, CancellationToken.None);

        Assert.Equal("Connor McDavid", career.Name);
        Assert.Equal((417, 834, 1251), (career.Goals, career.Assists, career.Points));
        Assert.Equal(10, career.PointStreakBefore(2026020176)); // points in all ten games so far
        Assert.True(career.Includes(2026020163));
        Assert.Contains(handler.Requests, u => u.AbsolutePath.EndsWith("/game-log/now"));
    }

    [Theory]
    [InlineData(0, 1, new[] { 1 })]
    [InlineData(49, 50, new[] { 50 })]
    [InlineData(99, 101, new[] { 100 })]
    [InlineData(498, 499, new int[0])]
    [InlineData(499, 500, new[] { 500 })]
    public void Finds_goal_milestones(int before, int after, int[] expected) =>
        Assert.Equal(expected, Milestones.Crossed(before, after, Milestones.NextGoals));

    [Fact]
    public void Describes_milestones()
    {
        Assert.Equal("first NHL goal", Milestones.Describe(1, "goals"));
        Assert.Equal("500th career goal", Milestones.Describe(500, "goals"));
        Assert.Equal("1300th career point", Milestones.Describe(1300, "points"));
    }

    [Fact]
    public void Announces_a_milestone_goal_once_after_the_baseline()
    {
        var tracker = new PlayerWatchTracker();
        var careers = new Dictionary<long, PlayerCareer> { [97] = Career(97, 499, 610) };
        PlayerWatchTracker.Result Update(HockeyGame g) => tracker.Update([g], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());

        Assert.Empty(Update(Game(GameStatus.Live)).Events);
        var result = Update(Game(GameStatus.Live, Goal(97, 1, 0)));

        var milestone = Assert.Single(result.Events);
        Assert.Equal(GameEventKind.Milestone, milestone.Kind);
        Assert.Equal("500th career goal · EDM 1-0 TOR", milestone.Body);
        Assert.Equal("Milestone! Player 97, 500th career goal.", milestone.Speech);
        Assert.Empty(Update(Game(GameStatus.Live, Goal(97, 1, 0))).Events);
    }

    [Fact]
    public void Does_not_double_count_when_the_profile_already_includes_tonight()
    {
        var tracker = new PlayerWatchTracker();
        // Profile refreshed after the goal: 500 goals including tonight's game 7.
        var careers = new Dictionary<long, PlayerCareer> { [97] = Career(97, 500, 610, counted: [7]) };
        tracker.Update([Game(GameStatus.Live)], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());

        var result = tracker.Update([Game(GameStatus.Live, Goal(97, 1, 0))], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());

        Assert.Equal("500th career goal · EDM 1-0 TOR", Assert.Single(result.Events).Body);
    }

    [Fact]
    public void Announces_a_point_streak_on_the_first_point_tonight()
    {
        var tracker = new PlayerWatchTracker();
        var careers = new Dictionary<long, PlayerCareer> { [97] = Career(97, 10, 10, recentPoints: [1, 2, 1, 1, 0]), [29] = Career(29, 10, 10, recentPoints: [1, 0]) };
        var settings = new PlayerWatchSettings { MinStreak = 5 };
        tracker.Update([Game(GameStatus.Live)], _ => true, careers.GetValueOrDefault, settings);

        var events = tracker.Update([Game(GameStatus.Live, Goal(29, 1, 0, 97))], _ => true, careers.GetValueOrDefault, settings).Events;

        var streak = Assert.Single(events);
        Assert.Equal("POINT STREAK  Player 97", streak.Title);
        Assert.Equal("5 games in a row · EDM 1-0 TOR", streak.Body);
        // A second point the same night is not another streak alert.
        Assert.Empty(tracker.Update([Game(GameStatus.Live, Goal(29, 1, 0, 97), Goal(97, 2, 0))], _ => true, careers.GetValueOrDefault, settings).Events);
    }

    [Fact]
    public void Waits_for_missing_stats_and_checks_again()
    {
        var tracker = new PlayerWatchTracker();
        var careers = new Dictionary<long, PlayerCareer>();
        tracker.Update([Game(GameStatus.Live)], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());

        var first = tracker.Update([Game(GameStatus.Live, Goal(97, 1, 0))], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());
        Assert.Equal([97L], first.NeedStats);
        Assert.Empty(first.Events);

        careers[97] = Career(97, 0, 0);
        var second = tracker.Update([Game(GameStatus.Live, Goal(97, 1, 0))], _ => true, careers.GetValueOrDefault, new PlayerWatchSettings());
        Assert.Equal("First NHL goal · EDM 1-0 TOR", Assert.Single(second.Events).Body);
    }

    [Fact]
    public void Ignores_players_out_of_scope()
    {
        var tracker = new PlayerWatchTracker();
        var careers = new Dictionary<long, PlayerCareer> { [97] = Career(97, 499, 600) };
        tracker.Update([Game(GameStatus.Live)], _ => false, careers.GetValueOrDefault, new PlayerWatchSettings());
        Assert.Empty(tracker.Update([Game(GameStatus.Live, Goal(97, 1, 0))], _ => false, careers.GetValueOrDefault, new PlayerWatchSettings()).Events);
    }

    [Fact]
    public void Shows_players_close_to_a_milestone()
    {
        var players = new[] { new RosterPlayer(97, "Connor", "McDavid", "C", "CAN", "EDM"), new RosterPlayer(29, "Leon", "Draisaitl", "C", "DEU", "EDM") };
        var careers = new Dictionary<long, PlayerCareer> { [97] = Career(97, 499, 600), [29] = Career(29, 300, 898) };

        var lines = PlayerWatchTracker.Approaching(Game(GameStatus.Scheduled), players, careers.GetValueOrDefault).ToList();

        Assert.Equal(["C. McDavid 1 goal from 500", "L. Draisaitl 2 pts from 1,200 points"], lines);
    }

    [Fact]
    public void Shutout_watch_late_in_the_third_then_completed()
    {
        var tracker = new ShutoutTracker();
        var box = new GameBoxscore(7, [new GoalieLine(31, "S. Skinner", "EDM", 25, 25, 0, "50:00", null)], false);
        var early = Game(GameStatus.Live, Goal(97, 1, 0)) with { Period = 3, SecondsRemaining = 900, TimeRemaining = "15:00" };
        Assert.Empty(tracker.Update([early], _ => box).Events);

        var late = early with { SecondsRemaining = 400, TimeRemaining = "06:40" };
        var watch = Assert.Single(tracker.Update([late], _ => box).Events);
        Assert.Equal("SHUTOUT WATCH  EDM 1-0 TOR", watch.Title);
        Assert.Equal("S. Skinner, 25 saves (EDM) · 06:40 left", watch.Body);
        Assert.Empty(tracker.Update([late], _ => box).Events);

        var final = late with { Status = GameStatus.Final, RawGameState = "OFF" };
        var done = Assert.Single(tracker.Update([final], _ => box).Events);
        Assert.Equal("SHUTOUT  EDM 1-0 TOR", done.Title);
        Assert.Equal("SHUTOUT  S. Skinner (EDM)", ShutoutTracker.CardLine(final, box));
    }

    [Fact]
    public void Shutout_waits_for_the_boxscore_and_names_a_combined_shutout()
    {
        var tracker = new ShutoutTracker();
        var game = Game(GameStatus.Live, Goal(97, 1, 0)) with { Period = 3, SecondsRemaining = 300 };
        tracker.Update([Game(GameStatus.Live, Goal(97, 1, 0))], _ => null);

        var waiting = tracker.Update([game], _ => null);
        Assert.Empty(waiting.Events);
        Assert.Single(waiting.NeedBoxscore);

        var box = new GameBoxscore(7, [new GoalieLine(31, "S. Skinner", "EDM", 15, 15, 0, "30:00", null), new GoalieLine(30, "C. Pickard", "EDM", 10, 10, 0, "20:00", null)], false);
        Assert.Equal("S. Skinner and C. Pickard, 25 saves (EDM) · 10:00 left", Assert.Single(tracker.Update([game], _ => box).Events).Body);
    }

    [Fact]
    public void Restart_during_a_shutout_is_a_silent_baseline()
    {
        var tracker = new ShutoutTracker();
        var box = new GameBoxscore(7, [new GoalieLine(31, "S. Skinner", "EDM", 25, 25, 0, "50:00", null)], false);
        var late = Game(GameStatus.Live, Goal(97, 1, 0)) with { Period = 3, SecondsRemaining = 300 };

        Assert.Empty(tracker.Update([late], _ => box).Events);
        Assert.Empty(tracker.Update([late], _ => box).Events);
    }
}
