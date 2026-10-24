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
}
