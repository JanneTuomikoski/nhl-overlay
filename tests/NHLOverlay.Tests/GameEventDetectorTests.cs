using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public class GameEventDetectorTests
{
    private readonly GameEventDetector _detector = new();
    private readonly HatTrickTracker _hatTricks = new();

    private static GameGoal Goal(long player, string team, int away, int home, int period = 1, string time = "05:00", string strength = "ev") => new()
    {
        PlayerId = player,
        ScorerName = $"Player {player}",
        ScorerShortName = $"P. {player}",
        TeamAbbrev = team,
        Period = period,
        PeriodType = period > 3 ? PeriodType.Overtime : PeriodType.Regulation,
        TimeInPeriod = time,
        AwayScore = away,
        HomeScore = home,
        Strength = strength,
    };

    private static HockeyGame Game(GameStatus status, params GameGoal[] goals) => new()
    {
        Id = 7,
        Status = status,
        RawGameState = status == GameStatus.Final ? "OFF" : "LIVE",
        Away = new HockeyTeam { Abbrev = "EDM", Name = "Oilers", Score = goals.Count(g => g.TeamAbbrev == "EDM") },
        Home = new HockeyTeam { Abbrev = "TOR", Name = "Maple Leafs", Score = goals.Count(g => g.TeamAbbrev == "TOR") },
        Period = 2,
        PeriodType = PeriodType.Regulation,
        TimeRemaining = "10:00",
        SecondsRemaining = 600,
        Goals = goals,
    };

    private IReadOnlyList<GameEvent> Detect(params HockeyGame[] games)
    {
        var snapshot = new ScoreboardSnapshot { HasData = true, Games = games };
        return _detector.Detect(snapshot with { HatTricks = _hatTricks.Update(games) });
    }

    [Fact]
    public void First_snapshot_is_a_silent_baseline()
    {
        Assert.Empty(Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(16, "TOR", 1, 1))));
        Assert.Empty(Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(16, "TOR", 1, 1))));
    }

    [Fact]
    public void Announces_a_new_goal_once()
    {
        Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0)));
        var events = Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2, "08:14", "pp")));

        var goal = Assert.Single(events, e => e.Kind == GameEventKind.Goal);
        Assert.Equal("EDM GOAL  EDM 2-0 TOR", goal.Title);
        Assert.Equal("P. 97 (2) · PP · 2ND 08:14", goal.Body);
        Assert.Equal("Goal, Oilers! Player 97, his second of the game. Oilers lead 2 to 0.", goal.Speech);

        Assert.DoesNotContain(Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2, "08:14"))), e => e.Kind == GameEventKind.Goal);
    }

    [Fact]
    public void Scoring_change_is_not_a_new_goal()
    {
        Detect(Game(GameStatus.Live));
        Assert.Single(Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0, 1, "05:00"))), e => e.Kind == GameEventKind.Goal);

        // Credited to a teammate and the time corrected by 20 seconds.
        var corrected = Detect(Game(GameStatus.Live, Goal(29, "EDM", 1, 0, 1, "05:20")));
        Assert.DoesNotContain(corrected, e => e.Kind == GameEventKind.Goal);
    }

    [Fact]
    public void Overturned_goal_scored_again_later_is_announced()
    {
        Detect(Game(GameStatus.Live));
        Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0, 1, "05:00")));
        Detect(Game(GameStatus.Live)); // goal overturned (score back to 0-0)

        var events = Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0, 1, "12:00")));
        Assert.Single(events, e => e.Kind == GameEventKind.Goal);
    }

    [Fact]
    public void Temporarily_missing_goal_data_does_not_replay_goals()
    {
        Detect(Game(GameStatus.Live));
        Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0)));

        var missing = Game(GameStatus.Live) with { Away = new HockeyTeam { Abbrev = "EDM", Name = "Oilers", Score = 1 } };
        Detect(missing); // score says 1-0 but no goal list
        Assert.DoesNotContain(Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0))), e => e.Kind == GameEventKind.Goal);
    }

    [Fact]
    public void Announces_start_overtime_and_final_once_each()
    {
        var scheduled = Game(GameStatus.Scheduled) with { Away = new HockeyTeam { Abbrev = "EDM", Name = "Oilers" }, Home = new HockeyTeam { Abbrev = "TOR", Name = "Maple Leafs" } };
        Detect(scheduled);

        Assert.Equal([GameEventKind.GameStarted], Detect(Game(GameStatus.Live)).Select(e => e.Kind));

        var overtime = Game(GameStatus.Overtime, Goal(97, "EDM", 1, 0), Goal(16, "TOR", 1, 1, 3)) with { PeriodType = PeriodType.Overtime, Period = 4 };
        var events = Detect(overtime);
        Assert.Contains(events, e => e.Kind == GameEventKind.Overtime && e.Title == "OVERTIME  EDM 1-1 TOR");
        Assert.DoesNotContain(Detect(overtime), e => e.Kind == GameEventKind.Overtime);

        var final = Game(GameStatus.Final, Goal(97, "EDM", 1, 0), Goal(16, "TOR", 1, 1, 3), Goal(97, "EDM", 2, 1, 4, "01:30")) with { LastPeriodType = PeriodType.Overtime };
        var finalEvent = Assert.Single(Detect(final), e => e.Kind == GameEventKind.GameFinal);
        Assert.Equal("FINAL/OT  EDM 2-1 TOR", finalEvent.Title);
        Assert.Equal("Final: Oilers beat the Maple Leafs 2 to 1 in overtime.", finalEvent.Speech);
        Assert.Empty(Detect(final));
    }

    [Fact]
    public void Hat_trick_watch_then_completed()
    {
        Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0)));
        var watch = Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2)));
        Assert.Contains(watch, e => e.Kind == GameEventKind.HatTrickWatch);

        var completed = Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2), Goal(97, "EDM", 3, 0, 3)));
        Assert.Contains(completed, e => e.Kind == GameEventKind.HatTrickCompleted && e.Speech == "Hat trick! Player 97 has 3 goals.");
        Assert.DoesNotContain(completed, e => e.Kind == GameEventKind.HatTrickWatch);
    }

    [Fact]
    public void Jumping_from_one_to_three_goals_between_polls_only_reports_the_hat_trick()
    {
        Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0)));
        var events = Detect(Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2), Goal(97, "EDM", 3, 0, 3)));

        Assert.Contains(events, e => e.Kind == GameEventKind.HatTrickCompleted);
        Assert.DoesNotContain(events, e => e.Kind == GameEventKind.HatTrickWatch);
        Assert.Equal(2, events.Count(e => e.Kind == GameEventKind.Goal));
    }

    [Fact]
    public void Restart_during_a_game_with_a_hat_trick_shows_no_old_alert()
    {
        var game = Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(97, "EDM", 2, 0, 2), Goal(97, "EDM", 3, 0, 3));
        Assert.Empty(Detect(game));
        Assert.Empty(Detect(game));
    }

    [Fact]
    public void Close_game_and_worth_watching_fire_late_in_a_tight_third()
    {
        var early = Game(GameStatus.Live, Goal(97, "EDM", 1, 0), Goal(16, "TOR", 1, 1, 2)) with { Period = 3, SecondsRemaining = 900, TimeRemaining = "15:00" };
        Detect(early);
        Assert.DoesNotContain(Detect(early), e => e.Kind is GameEventKind.CloseGame or GameEventKind.WorthWatching);

        var late = early with { SecondsRemaining = 240, TimeRemaining = "04:00" };
        var events = Detect(late);
        Assert.Contains(events, e => e.Kind == GameEventKind.CloseGame);
        var hot = Assert.Single(events, e => e.Kind == GameEventKind.WorthWatching);
        Assert.Contains("tied 1-1", hot.Body);
    }

    [Fact]
    public void A_game_seen_for_the_first_time_mid_session_is_a_baseline_too()
    {
        Detect(Game(GameStatus.Live));
        var other = Game(GameStatus.Final, Goal(1, "EDM", 1, 0)) with { Id = 8 };
        Assert.Empty(Detect(Game(GameStatus.Live), other));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 0, 2, 1, 2, 2, 2 }, 2)] // away came back from 0-2 to 2-2
    [InlineData(new[] { 1, 0, 2, 0, 3, 0 }, 0)]       // never trailed
    [InlineData(new[] { 0, 1, 0, 2, 0, 3, 1, 3 }, 0)] // still behind
    public void Measures_comebacks(int[] runningScores, int expected)
    {
        var goals = runningScores.Chunk(2).Select((s, i) => Goal(i, s[0] > (i == 0 ? 0 : runningScores[(i - 1) * 2]) ? "EDM" : "TOR", s[0], s[1])).ToArray();
        var game = Game(GameStatus.Live, goals) with
        {
            Away = new HockeyTeam { Abbrev = "EDM", Score = runningScores[^2] },
            Home = new HockeyTeam { Abbrev = "TOR", Score = runningScores[^1] },
        };
        Assert.Equal(expected, Excitement.ComebackSize(game));
    }
}
