using NHLOverlay.Models;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public class GameCardViewModelTests
{
    [Fact]
    public void Formats_a_power_play_goal()
    {
        var goal = new GameGoal
        {
            PlayerId = 8478402, ScorerShortName = "C. McDavid", TeamAbbrev = "EDM", Period = 2, PeriodType = PeriodType.Regulation,
            TimeInPeriod = "08:14", AwayScore = 2, HomeScore = 1, Strength = "pp",
        };

        Assert.Equal(new GoalLine("2ND", "08:14", "EDM", "C. McDavid", "PP", "2-1"), GameCardViewModel.ToGoalLine(goal, 3));
    }

    [Fact]
    public void Formats_empty_net_overtime_and_shootout_goals()
    {
        var emptyNet = new GameGoal { ScorerShortName = "A", Period = 3, PeriodType = PeriodType.Regulation, TimeInPeriod = "19:01", Strength = "sh", IsEmptyNet = true };
        Assert.Equal("SH EN", GameCardViewModel.ToGoalLine(emptyNet, 3).Tags);

        var overtime = new GameGoal { ScorerShortName = "B", Period = 4, PeriodType = PeriodType.Overtime, TimeInPeriod = "02:10", Strength = "ev" };
        Assert.Equal("OT", GameCardViewModel.ToGoalLine(overtime, 3).Period);

        var shootout = new GameGoal { ScorerShortName = "C", Period = 5, PeriodType = PeriodType.Shootout, TimeInPeriod = "00:00", AwayScore = 5, HomeScore = 6 };
        var line = GameCardViewModel.ToGoalLine(shootout, 3);
        Assert.Equal("SO", line.Period);
        Assert.Equal("", line.Time);
    }

    [Fact]
    public void Numbers_repeat_scorers_and_lists_assists()
    {
        GameGoal Goal(long scorer, string name, int away, bool shootout = false, params GoalAssist[] assists) => new()
        {
            PlayerId = scorer, ScorerShortName = name, TeamAbbrev = "EDM", Period = shootout ? 5 : 1,
            PeriodType = shootout ? PeriodType.Shootout : PeriodType.Regulation, TimeInPeriod = "05:00", AwayScore = away, Assists = assists,
        };
        var game = new HockeyGame
        {
            Id = 1,
            Status = GameStatus.Final,
            Away = new HockeyTeam { Abbrev = "EDM", Score = 4 },
            Home = new HockeyTeam { Abbrev = "TOR", Score = 3 },
            Goals =
            [
                Goal(97, "C. McDavid", 1, false, new GoalAssist(29, "L. Draisaitl"), new GoalAssist(2, "E. Bouchard")),
                Goal(29, "L. Draisaitl", 2),
                Goal(97, "C. McDavid", 3, false, new GoalAssist(29, "L. Draisaitl")),
                Goal(97, "C. McDavid", 4, shootout: true),
            ],
        };

        var lines = GameCardViewModel.ToGoalLines(game);

        Assert.Equal(["C. McDavid", "L. Draisaitl", "C. McDavid (2)", "C. McDavid"], lines.Select(l => l.Scorer));
        Assert.Equal(["L. Draisaitl, E. Bouchard", "", "L. Draisaitl", ""], lines.Select(l => l.Assists));
        Assert.False(lines[1].HasAssists);
    }

    [Fact]
    public void Flashes_after_a_goal_but_not_for_the_first_score_it_sees()
    {
        var settings = new AppSettings();
        settings.Appearance.ShowTeamLogos = false;
        var shielded = false;
        var card = new GameCardViewModel(1, null!);
        void Show(int away, int home) => card.Update(
            new HockeyGame
            {
                Id = 1,
                Status = GameStatus.Live,
                RawGameState = "LIVE",
                Away = new HockeyTeam { Abbrev = "EDM", Score = away },
                Home = new HockeyTeam { Abbrev = "TOR", Score = home },
            },
            new CardContext(settings, new HashSet<string>(), _ => null, new Dictionary<long, IReadOnlyList<PlayerGameGoals>>(),
                DateTimeOffset.UtcNow, Zones.Helsinki, System.Globalization.CultureInfo.InvariantCulture) { IsShielded = _ => shielded });

        Show(2, 1); // startup: baseline
        Assert.False(card.IsGoalFlash);

        Show(2, 2);
        Assert.True(card.IsGoalFlash);
        Assert.True(card.HomeGoalFlash);
        Assert.False(card.AwayGoalFlash);

        Show(2, 2); // no change: still flashing until the timer ends it
        Assert.True(card.IsGoalFlash);

        shielded = true;
        Show(2, 3); // hidden score: no flash
        Assert.False(card.IsGoalFlash);
        shielded = false;
        Show(2, 3); // revealed: a new baseline, not a goal
        Assert.False(card.IsGoalFlash);

        settings.Appearance.FlashOnGoal = false;
        Show(3, 3);
        Assert.False(card.IsGoalFlash);
    }
}
