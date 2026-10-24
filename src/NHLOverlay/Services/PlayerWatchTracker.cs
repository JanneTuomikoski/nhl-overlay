using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Career milestones and point streaks from tonight's goal lists plus each player's career totals.
/// A game's existing points are a silent baseline the first time it is seen (restart, day rollover); after
/// that each increase in a player's points is checked once. Players whose stats are not loaded yet are
/// reported in <see cref="Result.NeedStats"/> and checked again on a later update.
/// </summary>
public sealed class PlayerWatchTracker
{
    private readonly Dictionary<(long Game, long Player), (int Goals, int Assists)> _evaluated = [];
    private readonly HashSet<long> _seenGames = [];
    private readonly HashSet<string> _emitted = [];

    public sealed record Result(IReadOnlyList<GameEvent> Events, IReadOnlyList<long> NeedStats);

    public Result Update(
        IReadOnlyList<HockeyGame> games,
        Func<long, bool> inScope,
        Func<long, PlayerCareer?> career,
        PlayerWatchSettings settings)
    {
        var events = new List<GameEvent>();
        var needStats = new List<long>();
        foreach (var game in games.Where(g => g.Status.IsInProgress() || g.Status.IsFinal()))
        {
            var firstSighting = _seenGames.Add(game.Id);
            foreach (var (playerId, counts) in TonightCounts(game))
            {
                if (!inScope(playerId)) continue;
                var key = (game.Id, playerId);
                var last = _evaluated.GetValueOrDefault(key);
                if (firstSighting || counts == last || counts.Goals + counts.Assists < last.Goals + last.Assists)
                {
                    _evaluated[key] = counts; // baseline, unchanged, or a scoring correction
                    continue;
                }

                if (career(playerId) is not { } c)
                {
                    needStats.Add(playerId);
                    continue; // re-checked once the stats arrive
                }

                if (settings.Milestones) events.AddRange(MilestoneEvents(game, c, last, counts));
                if (settings.PointStreaks && last.Goals + last.Assists == 0 && counts.Goals + counts.Assists > 0)
                {
                    var streak = c.PointStreakBefore(game.Id) + 1;
                    if (streak >= settings.MinStreak && _emitted.Add($"streak:{game.Id}:{playerId}"))
                        events.Add(StreakEvent(game, c, streak));
                }

                _evaluated[key] = counts;
            }
        }

        return new Result(events, needStats);
    }

    /// <summary>Non-shootout goals and assists per player in one game.</summary>
    internal static Dictionary<long, (int Goals, int Assists)> TonightCounts(HockeyGame game)
    {
        var counts = new Dictionary<long, (int Goals, int Assists)>();
        foreach (var goal in game.Goals.Where(g => !g.IsShootout))
        {
            var (g, a) = counts.GetValueOrDefault(goal.PlayerId);
            counts[goal.PlayerId] = (g + 1, a);
            foreach (var assist in goal.Assists)
            {
                var (ag, aa) = counts.GetValueOrDefault(assist.PlayerId);
                counts[assist.PlayerId] = (ag, aa + 1);
            }
        }

        return counts;
    }

    /// <summary>Career totals before tonight (the profile may or may not already count tonight's game).</summary>
    internal static (int Goals, int Assists) Before(PlayerCareer c, long gameId, (int Goals, int Assists) tonight) =>
        c.Includes(gameId) ? (c.Goals - tonight.Goals, c.Assists - tonight.Assists) : (c.Goals, c.Assists);

    private IEnumerable<GameEvent> MilestoneEvents(HockeyGame game, PlayerCareer c, (int Goals, int Assists) last, (int Goals, int Assists) now)
    {
        var before = Before(c, game.Id, now);
        var checks = new (string Stat, int From, int To, Func<int, int> Next)[]
        {
            ("goals", before.Goals + last.Goals, before.Goals + now.Goals, Milestones.NextGoals),
            ("assists", before.Assists + last.Assists, before.Assists + now.Assists, Milestones.NextAssists),
            ("points", before.Goals + before.Assists + last.Goals + last.Assists, before.Goals + before.Assists + now.Goals + now.Assists, Milestones.NextPoints),
        };
        foreach (var (stat, from, to, next) in checks)
        {
            foreach (var value in Milestones.Crossed(from, to, next))
            {
                // A first goal is also a first point; announce it once, as the goal.
                if (stat == "points" && value == 1 && now.Goals > 0) continue;
                if (!_emitted.Add($"ms:{game.Id}:{c.PlayerId}:{stat}:{value}")) continue;
                var what = Milestones.Describe(value, stat);
                yield return new GameEvent(GameEventKind.Milestone, $"ms:{game.Id}:{c.PlayerId}:{stat}:{value}", game,
                    $"MILESTONE  {c.Name}",
                    $"{char.ToUpper(what[0], CultureInfo.InvariantCulture)}{what[1..]} · {Matchup(game)}",
                    $"Milestone! {c.Name}, {what}.");
            }
        }
    }

    private static GameEvent StreakEvent(HockeyGame game, PlayerCareer c, int streak) => new(
        GameEventKind.PointStreak, $"streak:{game.Id}:{c.PlayerId}", game,
        $"POINT STREAK  {c.Name}",
        $"{streak} games in a row · {Matchup(game)}",
        $"{c.Name} extends his point streak to {streak} games.");

    private static string Matchup(HockeyGame g) => $"{g.Away.Abbrev} {g.Away.Score}-{g.Home.Score} {g.Home.Abbrev}";

    /// <summary>"C. McDavid 1 goal from 500" style lines for players close to a milestone (games not yet final).</summary>
    public static IEnumerable<string> Approaching(HockeyGame game, IEnumerable<RosterPlayer> players, Func<long, PlayerCareer?> career)
    {
        if (game.Status.IsFinal() || game.Status.IsInactive()) yield break;
        var tonight = TonightCounts(game);
        foreach (var player in players.Where(p => !p.IsGoalie))
        {
            if (career(player.Id) is not { } c) continue;
            var counts = tonight.GetValueOrDefault(player.Id);
            var before = Before(c, game.Id, counts);
            var goals = before.Goals + counts.Goals;
            var points = goals + before.Assists + counts.Assists;
            var nextGoals = Milestones.NextGoals(goals);
            var nextPoints = Milestones.NextPoints(points);
            if (goals >= 49 && nextGoals - goals == 1)
                yield return $"{player.ShortName} 1 goal from {nextGoals.ToString("N0", CultureInfo.InvariantCulture)}";
            else if (points >= 98 && nextPoints - points <= 2)
                yield return $"{player.ShortName} {nextPoints - points} pt{(nextPoints - points == 1 ? "" : "s")} from {nextPoints.ToString("N0", CultureInfo.InvariantCulture)} points";
        }
    }
}
