using System.Globalization;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public class HatTrickTests
{
    private static GameGoal Goal(long playerId, string team, int period = 1, PeriodType type = PeriodType.Regulation, string time = "10:00") => new()
    {
        PlayerId = playerId,
        ScorerName = $"Player {playerId}",
        ScorerShortName = $"P. {playerId}",
        TeamAbbrev = team,
        Period = period,
        PeriodType = type,
        TimeInPeriod = time,
    };

    private static HockeyGame GameWith(long id, params GameGoal[] goals) => new()
    {
        Id = id,
        Status = GameStatus.Live,
        Away = new HockeyTeam { Abbrev = "EDM", Score = goals.Count(g => g.TeamAbbrev == "EDM") },
        Home = new HockeyTeam { Abbrev = "TOR", Score = goals.Count(g => g.TeamAbbrev == "TOR") },
        Goals = goals,
    };

    [Fact]
    public async Task Finds_a_real_two_goal_game()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.LiveDay));
        var games = await api.GetGamesForDateAsync(new DateOnly(2026, 10, 24), CancellationToken.None);

        var michkov = Assert.Single(HatTrickTracker.Analyze(games.Single(g => g.Id == 2026020168)));
        Assert.Equal(8484387, michkov.PlayerId);
        Assert.Equal("Matvei Michkov", michkov.Name);
        Assert.True(michkov.IsWatch);
    }

    [Fact]
    public async Task Never_counts_shootout_goals_in_real_data()
    {
        using var api = Fixtures.ApiReturning(Fixtures.Read(Fixtures.FinalDay));
        var games = await api.GetGamesForDateAsync(new DateOnly(2026, 10, 22), CancellationToken.None);
        var shootoutGame = games.Single(g => g.Id == 2026020163);

        var counted = HatTrickTracker.Analyze(shootoutGame).Sum(p => p.Goals);
        var regulationGoalsByTwoGoalScorers = shootoutGame.Goals.Where(g => !g.IsShootout).GroupBy(g => g.PlayerId).Where(g => g.Count() >= 2).Sum(g => g.Count());
        Assert.Equal(regulationGoalsByTwoGoalScorers, counted);
    }

    [Fact]
    public void Two_regulation_goals_plus_a_shootout_goal_is_still_a_watch()
    {
        var game = GameWith(1, Goal(97, "EDM"), Goal(97, "EDM", 2), Goal(97, "EDM", 5, PeriodType.Shootout, "00:00"));
        var player = Assert.Single(HatTrickTracker.Analyze(game));
        Assert.Equal(2, player.Goals);
        Assert.False(player.IsHatTrick);
    }

    [Fact]
    public void Detects_multiple_watches_on_opposing_teams_and_completed_hat_tricks()
    {
        var game = GameWith(1, Goal(97, "EDM"), Goal(16, "TOR"), Goal(97, "EDM", 2), Goal(16, "TOR", 2), Goal(29, "EDM", 3), Goal(97, "EDM", 3, time: "15:00"));
        var players = HatTrickTracker.Analyze(game);

        Assert.Equal(2, players.Count);
        Assert.Equal((97L, 3), (players[0].PlayerId, players[0].Goals));
        Assert.True(players[0].IsHatTrick);
        Assert.Equal((16L, 2), (players[1].PlayerId, players[1].Goals));
        Assert.Equal("TOR", players[1].TeamAbbrev);
    }

    [Fact]
    public void Reassigned_goal_moves_the_count()
    {
        var tracker = new HatTrickTracker();
        Assert.Single(tracker.Update([GameWith(1, Goal(97, "EDM"), Goal(97, "EDM", 2))])[1]);

        // Scoring change: the second goal goes to a teammate.
        var after = tracker.Update([GameWith(1, Goal(97, "EDM"), Goal(29, "EDM", 2))]);
        Assert.False(after.ContainsKey(1));
    }

    [Fact]
    public void Keeps_the_last_result_when_goal_data_is_temporarily_missing()
    {
        var tracker = new HatTrickTracker();
        tracker.Update([GameWith(1, Goal(97, "EDM"), Goal(97, "EDM", 2), Goal(97, "EDM", 3))]);

        var missing = GameWith(1) with { Away = new HockeyTeam { Abbrev = "EDM", Score = 3 } };
        var result = tracker.Update([missing]);

        Assert.True(result[1].Single().IsHatTrick);
    }

    [Fact]
    public void Scoreless_game_without_goals_has_no_watch()
    {
        var tracker = new HatTrickTracker();
        Assert.Empty(tracker.Update([GameWith(1)]));
    }

    [Fact]
    public void Card_shows_watch_and_completed_text_and_honors_scope()
    {
        var game = GameWith(1, Goal(97, "EDM"), Goal(97, "EDM", 2), Goal(16, "TOR"), Goal(16, "TOR", 2), Goal(16, "TOR", 3));
        var hatTricks = new Dictionary<long, IReadOnlyList<PlayerGameGoals>> { [1] = HatTrickTracker.Analyze(game) };
        var settings = new AppSettings();
        settings.Appearance.ShowTeamLogos = false;
        var card = new GameCardViewModel(1, null!);

        card.Update(game, Context(settings, hatTricks));
        Assert.Equal(HatTrickState.Completed, card.HatTrickState);
        Assert.True(card.HighlightHatTrick);
        Assert.Equal($"HAT TRICK!  P. 16 (3){Environment.NewLine}HAT-TRICK WATCH  P. 97 (2)", card.HatTrickText);
        Assert.Contains("Player 97 · 2 goals", card.HatTrickTextLong);

        settings.HatTrick.ShowPlayerName = false;
        card.Update(game, Context(settings, hatTricks));
        Assert.Equal("HAT TRICK!", card.HatTrickText);

        settings.HatTrick.Scope = TeamScope.FavoritesOnly;
        card.Update(game, Context(settings, hatTricks));
        Assert.Equal(HatTrickState.None, card.HatTrickState);

        card.Update(game, Context(settings, hatTricks, favorites: ["EDM"]));
        Assert.Equal(HatTrickState.Completed, card.HatTrickState);
    }

    [Fact]
    public void Finished_games_keep_completed_hat_tricks_but_drop_watches()
    {
        var game = GameWith(1, Goal(97, "EDM"), Goal(97, "EDM", 2), Goal(16, "TOR"), Goal(16, "TOR", 2), Goal(16, "TOR", 3)) with { Status = GameStatus.Final };
        var hatTricks = new Dictionary<long, IReadOnlyList<PlayerGameGoals>> { [1] = HatTrickTracker.Analyze(game) };

        var players = Context(new AppSettings(), hatTricks).HatTricksFor(game);

        Assert.Equal([16L], players.Select(p => p.PlayerId));
    }

    private static CardContext Context(AppSettings settings, IReadOnlyDictionary<long, IReadOnlyList<PlayerGameGoals>> hatTricks, string[]? favorites = null) =>
        new(settings, (favorites ?? []).ToHashSet(), _ => null, hatTricks, DateTimeOffset.UtcNow, Zones.Helsinki, CultureInfo.InvariantCulture);
}
