using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public class FocusAnalysisTests
{
    private static async Task<PlayByPlay> AnaPhi()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read("pbp_2026020168.json"));
        return await api.GetPlayByPlayAsync(2026020168, CancellationToken.None);
    }

    [Fact]
    public async Task Parses_play_by_play_with_player_names()
    {
        var pbp = await AnaPhi();

        Assert.Equal(("ANA", "PHI"), (pbp.AwayAbbrev, pbp.HomeAbbrev));
        var first = pbp.Plays.First(p => p.Type == PlayType.Goal);
        Assert.Equal("M. Michkov", pbp.NameOf(first.PrimaryPlayerId));
        Assert.Equal("J. Drysdale", pbp.NameOf(first.SecondaryPlayerId));
        Assert.Equal("PHI", first.Team);
        Assert.Equal("1351", first.SituationCode);
        Assert.Equal(9, pbp.Plays.Count(p => p.Type == PlayType.Goal));
    }

    [Theory]
    [InlineData("1551", "Even strength", 0)]
    [InlineData("1451", "PP PHI · 5 on 4", -1)]
    [InlineData("1541", "PP ANA · 5 on 4", 1)]
    [InlineData("1351", "PP PHI · 5 on 3", -2)]
    [InlineData("1441", "4 on 4", 0)]
    [InlineData("0651", "ANA goalie pulled · 6 on 5", 0)]
    [InlineData("1460", "PP PHI · PHI goalie pulled · 6 on 4", -1)]
    public void Describes_strength(string code, string expected, int advantage)
    {
        var situation = Situation.Parse(code);
        Assert.Equal(advantage, situation.PenaltyAdvantage);
        Assert.Equal(expected, FocusAnalysis.StrengthText(situation, "ANA", "PHI"));
    }

    [Fact]
    public async Task Shots_by_period_add_up_to_the_final_shot_totals()
    {
        var periods = FocusAnalysis.ShotsByPeriod(await AnaPhi());

        Assert.Equal(["1ST", "2ND", "3RD"], periods.Select(p => p.Label));
        Assert.Equal(26, periods.Sum(p => p.Away)); // ANA 26 SOG
        Assert.Equal(32, periods.Sum(p => p.Home)); // PHI 32 SOG
    }

    [Fact]
    public async Task Shot_map_puts_each_team_on_its_own_attacking_side()
    {
        var marks = FocusAnalysis.ShotMarks(await AnaPhi(), includeMisses: false);

        Assert.Equal(9, marks.Count(m => m.IsGoal));
        var away = marks.Where(m => m.IsAway).ToList();
        var home = marks.Where(m => !m.IsAway).ToList();
        Assert.True(away.Count(m => m.X > 0) >= away.Count * 0.9, "away shots should attack right");
        Assert.True(home.Count(m => m.X < 0) >= home.Count * 0.9, "home shots should attack left");
        Assert.DoesNotContain(marks, m => m.IsMiss);
        Assert.Contains(FocusAnalysis.ShotMarks(await AnaPhi(), includeMisses: true), m => m.IsMiss);
    }

    [Fact]
    public async Task Feed_lists_goals_penalties_periods_and_goalie_pulls_newest_first()
    {
        var feed = FocusAnalysis.Feed(await AnaPhi(), includeShots: false, includeHits: false, maxItems: 200);

        Assert.Equal("Final", feed[0].Text);
        Assert.Contains(feed, f => f.Kind == FeedKind.Goal && f.Text == "GOAL  M. Michkov (J. Drysdale, T. Zegras)  PP  0-1");
        Assert.Contains(feed, f => f.Kind == FeedKind.Goalie && f.Text == "PHI pulls the goalie");
        Assert.Contains(feed, f => f.Kind == FeedKind.Penalty);
        Assert.DoesNotContain(feed, f => f.Kind is FeedKind.Shot or FeedKind.Hit);
        Assert.Contains(FocusAnalysis.Feed(await AnaPhi(), includeShots: true, includeHits: true, maxItems: 500), f => f.Kind == FeedKind.Hit);
    }

    [Fact]
    public void Power_play_countdown_uses_the_shorthanded_teams_penalty()
    {
        var pbp = new PlayByPlay(1, 2, "ANA", "PHI",
        [
            new PlayEvent { Type = PlayType.Penalty, Period = 1, TimeInPeriod = "05:00", Team = "ANA", PenaltyMinutes = 2, SituationCode = "1551" },
            new PlayEvent { Type = PlayType.Faceoff, Period = 1, TimeInPeriod = "05:00", SituationCode = "1451" },
        ], new Dictionary<long, string>());
        var situation = Situation.Parse("1451");

        // 1st period, 06:00 elapsed (14:00 = 840 s remaining): one minute left.
        Assert.Equal(TimeSpan.FromSeconds(60), FocusAnalysis.PowerPlayRemaining(pbp, situation, 1, 840));
        // Expired by 07:30.
        Assert.Null(FocusAnalysis.PowerPlayRemaining(pbp, situation, 1, 750));
        Assert.Null(FocusAnalysis.PowerPlayRemaining(pbp, Situation.EvenStrength, 1, 840));
    }

    [Fact]
    public void Regular_season_overtime_is_five_minutes()
    {
        Assert.Equal(1200, FocusAnalysis.PeriodLength(3, 3, gameType: 2));
        Assert.Equal(300, FocusAnalysis.PeriodLength(4, 3, gameType: 2));
        Assert.Equal(1200, FocusAnalysis.PeriodLength(4, 3, gameType: 3));
        Assert.Equal(3600 + 90, FocusAnalysis.Elapsed(4, "01:30", 3, 2));
    }
}
