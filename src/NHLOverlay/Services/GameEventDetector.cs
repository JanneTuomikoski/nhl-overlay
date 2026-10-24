using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Turns successive scoreboard snapshots into <see cref="GameEvent"/>s.
/// <para>
/// Detection is state based: each condition (game started, in overtime, final, a player with two or three
/// goals, ...) has a stable key and fires once, the first time it is seen. The first time a game is seen
/// (app start, day rollover) its current conditions and goals are only recorded, never announced, so a
/// restart doesn't replay the night's goals.
/// </para>
/// <para>
/// Goals are identified by team, running score and period with a time tolerance: a scoring change
/// (different scorer, time corrected by a few seconds) keeps the identity and is not re-announced, while a
/// goal that was overturned and then scored again later is announced.
/// </para>
/// </summary>
public sealed class GameEventDetector
{
    /// <summary>Two goals with the same team/score/period within this many seconds are the same goal.</summary>
    internal const int GoalTimeToleranceSeconds = 90;

    private readonly Dictionary<long, List<KnownGoal>> _knownGoals = [];
    private readonly HashSet<string> _emitted = [];
    private Func<long, RosterPlayer?>? _findPlayer;
    private IReadOnlySet<string>? _countries;

    private sealed record KnownGoal(string Team, int Away, int Home, int Period, int Seconds);

    /// <param name="snapshot">The latest scoreboard.</param>
    /// <param name="findPlayer">Roster lookup for watched-country points (optional).</param>
    /// <param name="countries">Watched countries, e.g. "FIN" (optional).</param>
    public IReadOnlyList<GameEvent> Detect(ScoreboardSnapshot snapshot, Func<long, RosterPlayer?>? findPlayer = null, IReadOnlySet<string>? countries = null)
    {
        if (!snapshot.HasData) return [];
        _findPlayer = findPlayer;
        _countries = countries;

        var events = new List<GameEvent>();
        foreach (var game in snapshot.Games)
        {
            var hatTricks = snapshot.HatTricks.GetValueOrDefault(game.Id) ?? [];
            var gameEvents = new List<GameEvent>();
            var firstSighting = !_knownGoals.ContainsKey(game.Id);

            DetectGoals(game, gameEvents);
            DetectConditions(game, hatTricks, gameEvents);

            if (firstSighting) continue; // baseline: keys are now marked, nothing is announced
            events.AddRange(gameEvents);
        }

        // Forget games that left the today view (their keys stay, so they never re-fire).
        var current = snapshot.Games.Select(g => g.Id).ToHashSet();
        foreach (var id in _knownGoals.Keys.Where(id => !current.Contains(id)).ToList())
            _knownGoals.Remove(id);

        return events.OrderBy(e => Priority(e.Kind)).ToList();
    }

    private void DetectGoals(HockeyGame game, List<GameEvent> events)
    {
        if (!_knownGoals.TryGetValue(game.Id, out var known))
        {
            known = [];
            _knownGoals[game.Id] = known;
        }

        var goals = game.Goals.Where(g => !g.IsShootout).ToList();
        var seen = new List<KnownGoal>();
        foreach (var goal in goals)
        {
            var identity = new KnownGoal(goal.TeamAbbrev, goal.AwayScore, goal.HomeScore, goal.Period, Seconds(goal.TimeInPeriod));
            seen.Add(identity);
            var match = known.FirstOrDefault(k => SameGoal(k, identity));
            if (match is not null)
            {
                known[known.IndexOf(match)] = identity; // follow corrections
                continue;
            }

            known.Add(identity);
            var key = $"goal:{game.Id}:{identity.Period}:{identity.Seconds}:{identity.Team}:{identity.Away}-{identity.Home}";
            if (_emitted.Add(key))
                events.Add(GoalEvent(game, goal, goals, key));
            DetectCountryPoints(game, goal, goals, key, events);
        }

        // A goal that vanished while the data is consistent (goal count matches the score) was overturned.
        var scoreTotal = (game.Away.Score ?? 0) + (game.Home.Score ?? 0);
        var shootoutGoal = game.Goals.Any(g => g.IsShootout) ? 1 : 0;
        if (goals.Count > 0 && goals.Count == scoreTotal - shootoutGoal)
            known.RemoveAll(k => !seen.Any(s => SameGoal(k, s)));
    }

    /// <summary>A new goal scored or assisted by a watched-country player.</summary>
    private void DetectCountryPoints(HockeyGame game, GameGoal goal, IReadOnlyList<GameGoal> goals, string goalKey, List<GameEvent> events)
    {
        if (_findPlayer is null || _countries is not { Count: > 0 }) return;
        var involved = new[] { (goal.PlayerId, IsGoal: true) }.Concat(goal.Assists.Select(a => (a.PlayerId, IsGoal: false)));
        foreach (var (playerId, isGoal) in involved)
        {
            if (_findPlayer(playerId) is not { } player || !_countries.Contains(player.Country)) continue;
            if (!_emitted.Add($"cp:{goalKey}:{playerId}")) continue;
            events.Add(CountryPointEvent(game, goal, goals, player, isGoal, goalKey));
        }
    }

    private static bool SameGoal(KnownGoal a, KnownGoal b) =>
        a.Team == b.Team && a.Away == b.Away && a.Home == b.Home && a.Period == b.Period
        && Math.Abs(a.Seconds - b.Seconds) <= GoalTimeToleranceSeconds;

    private void DetectConditions(HockeyGame game, IReadOnlyList<PlayerGameGoals> hatTricks, List<GameEvent> events)
    {
        void Raise(GameEventKind kind, string key, Func<GameEvent> create)
        {
            if (_emitted.Add(key)) events.Add(create());
        }

        var inProgress = game.Status.IsInProgress();
        if (inProgress || game.Status.IsFinal())
            Raise(GameEventKind.GameStarted, $"start:{game.Id}", () => StartedEvent(game));

        foreach (var player in hatTricks)
        {
            if (player.IsHatTrick)
            {
                _emitted.Add($"htw:{game.Id}:{player.PlayerId}"); // jumping from 1 to 3 goals skips the watch
                Raise(GameEventKind.HatTrickCompleted, $"ht:{game.Id}:{player.PlayerId}", () => HatTrickEvent(game, player));
            }
            else if (player.IsWatch && inProgress)
            {
                Raise(GameEventKind.HatTrickWatch, $"htw:{game.Id}:{player.PlayerId}", () => HatTrickEvent(game, player));
            }
        }

        if (inProgress && game.PeriodType is PeriodType.Overtime or PeriodType.Shootout)
        {
            var shootout = game.PeriodType == PeriodType.Shootout;
            Raise(GameEventKind.Overtime, $"{(shootout ? "so" : "ot")}:{game.Id}", () => OvertimeEvent(game, shootout));
        }

        if (IsCloseGame(game))
            Raise(GameEventKind.CloseGame, $"close:{game.Id}", () => CloseGameEvent(game));

        var (score, reasons) = Excitement.Score(game, hatTricks);
        if (score >= Excitement.Threshold)
            Raise(GameEventKind.WorthWatching, $"hot:{game.Id}", () => WorthWatchingEvent(game, reasons));

        if (game.Status.IsFinal())
            Raise(GameEventKind.GameFinal, $"final:{game.Id}", () => FinalEvent(game));
    }

    /// <summary>Third period, ten minutes or less left, within one goal.</summary>
    internal static bool IsCloseGame(HockeyGame game) =>
        game.Status == GameStatus.Live && game.PeriodType == PeriodType.Regulation && game.Period == 3
        && game.SecondsRemaining is <= 600 && game.HasScore && Math.Abs(game.Away.Score!.Value - game.Home.Score!.Value) <= 1;

    private static int Priority(GameEventKind kind) => kind switch
    {
        GameEventKind.GameStarted => 0,
        GameEventKind.Goal => 1,
        GameEventKind.CountryPlayerPoint => 1,
        GameEventKind.HatTrickCompleted => 2,
        GameEventKind.HatTrickWatch => 3,
        GameEventKind.Overtime => 4,
        GameEventKind.CloseGame => 5,
        GameEventKind.WorthWatching => 6,
        _ => 7,
    };

    internal static int Seconds(string? time) =>
        TimeSpan.TryParseExact(time, @"mm\:ss", CultureInfo.InvariantCulture, out var span) ? (int)span.TotalSeconds : 0;

    // ---- Event texts -------------------------------------------------------------------------------

    private static string Matchup(HockeyGame game, int? away = null, int? home = null) =>
        game.HasScore || away is not null
            ? $"{game.Away.Abbrev} {away ?? game.Away.Score}-{home ?? game.Home.Score} {game.Home.Abbrev}"
            : $"{game.Away.Abbrev} @ {game.Home.Abbrev}";

    private static string Name(HockeyTeam team) => string.IsNullOrWhiteSpace(team.Name) ? team.Abbrev : team.Name;

    private static string ScoreSentence(HockeyGame game, int away, int home) =>
        away == home ? $"Tied {away} to {home}."
        : away > home ? $"{Name(game.Away)} lead {away} to {home}."
        : $"{Name(game.Home)} lead {home} to {away}.";

    private static string Ordinal(int n) => n switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        _ => $"{n}th",
    };

    private static GameEvent GoalEvent(HockeyGame game, GameGoal goal, IReadOnlyList<GameGoal> allGoals, string key)
    {
        var scorerGoals = allGoals.TakeWhile(g => g != goal).Count(g => g.PlayerId == goal.PlayerId) + 1;
        var team = goal.TeamAbbrev == game.Away.Abbrev ? game.Away : game.Home;
        var tags = string.Join(" ", new[]
        {
            string.Equals(goal.Strength, "pp", StringComparison.OrdinalIgnoreCase) ? "PP" : null,
            string.Equals(goal.Strength, "sh", StringComparison.OrdinalIgnoreCase) ? "SH" : null,
            goal.IsEmptyNet ? "EN" : null,
        }.Where(t => t is not null));
        var when = $"{GameStatusFormatter.PeriodLabel(goal.Period, goal.PeriodType, game.MaxRegulationPeriods)} {goal.TimeInPeriod}";
        var body = $"{goal.ScorerShortName} ({scorerGoals}){(tags.Length > 0 ? " · " + tags : "")} · {when}";
        var speech = $"Goal, {Name(team)}! {goal.ScorerName}"
                     + (scorerGoals >= 2 ? $", his {Ordinal(scorerGoals)} of the game. " : ". ")
                     + ScoreSentence(game, goal.AwayScore, goal.HomeScore);
        return new GameEvent(GameEventKind.Goal, key, game, $"{team.Abbrev} GOAL  {Matchup(game, goal.AwayScore, goal.HomeScore)}", body, speech)
        {
            GoalKey = key,
        };
    }

    private static GameEvent CountryPointEvent(HockeyGame game, GameGoal goal, IReadOnlyList<GameGoal> allGoals, RosterPlayer player, bool isGoal, string goalKey)
    {
        // Points so far tonight, up to and including this goal.
        var upTo = allGoals.TakeWhile(g => g != goal).Append(goal).ToList();
        var goalsTonight = upTo.Count(g => g.PlayerId == player.Id);
        var assistsTonight = upTo.Count(g => g.Assists.Any(a => a.PlayerId == player.Id));
        var team = player.Team == game.Away.Abbrev ? game.Away : game.Home;
        var adjective = HockeyCountries.Adjective(player.Country);
        return new GameEvent(
            GameEventKind.CountryPlayerPoint,
            $"cp:{goalKey}:{player.Id}",
            game,
            $"{player.Country} {(isGoal ? "GOAL" : "ASSIST")}  {player.ShortName} · {Matchup(game, goal.AwayScore, goal.HomeScore)}",
            $"{player.FullName} ({player.Team}) · {goalsTonight}+{assistsTonight} tonight",
            isGoal
                ? $"{adjective} goal! {player.FullName} scores for the {Name(team)}."
                : $"{player.FullName} with an assist for the {Name(team)}.")
        {
            GoalKey = isGoal ? goalKey : null,
        };
    }

    private static GameEvent StartedEvent(HockeyGame game) => new(
        GameEventKind.GameStarted, $"start:{game.Id}", game,
        $"PUCK DROP  {game.Away.Abbrev} @ {game.Home.Abbrev}",
        $"{Name(game.Away)} at {Name(game.Home)}",
        $"Puck drop: {Name(game.Away)} at {Name(game.Home)}.");

    private static GameEvent HatTrickEvent(HockeyGame game, PlayerGameGoals player) => player.IsHatTrick
        ? new GameEvent(GameEventKind.HatTrickCompleted, $"ht:{game.Id}:{player.PlayerId}", game,
            "HAT TRICK!",
            $"{player.Name} ({player.TeamAbbrev}) · {player.Goals} goals · {Matchup(game)}",
            $"Hat trick! {player.Name} has {player.Goals} goals.")
        : new GameEvent(GameEventKind.HatTrickWatch, $"htw:{game.Id}:{player.PlayerId}", game,
            "HAT-TRICK WATCH",
            $"{player.Name} ({player.TeamAbbrev}) has 2 goals · {Matchup(game)}",
            $"Hat-trick watch: {player.Name} has two goals.");

    private static GameEvent OvertimeEvent(HockeyGame game, bool shootout) => new(
        GameEventKind.Overtime, $"{(shootout ? "so" : "ot")}:{game.Id}", game,
        $"{(shootout ? "SHOOTOUT" : "OVERTIME")}  {Matchup(game)}",
        shootout ? "Decided in a shootout" : $"{Name(game.Away)} and {Name(game.Home)} go to overtime",
        shootout
            ? $"{Name(game.Away)} and {Name(game.Home)} go to a shootout."
            : $"{Name(game.Away)} and {Name(game.Home)} go to overtime, tied {game.Away.Score} to {game.Home.Score}.");

    private static GameEvent CloseGameEvent(HockeyGame game)
    {
        var minutes = Math.Max(1, (game.SecondsRemaining ?? 0) / 60);
        return new GameEvent(GameEventKind.CloseGame, $"close:{game.Id}", game,
            $"CLOSE GAME  {Matchup(game)}",
            $"3RD {game.TimeRemaining} · {(game.Away.Score == game.Home.Score ? "tied" : "one-goal game")}",
            $"Close game. {ScoreSentence(game, game.Away.Score ?? 0, game.Home.Score ?? 0)} About {minutes} minutes left.");
    }

    private static GameEvent WorthWatchingEvent(HockeyGame game, IReadOnlyList<string> reasons) => new(
        GameEventKind.WorthWatching, $"hot:{game.Id}", game,
        $"WORTH WATCHING  {Matchup(game)}",
        string.Join(" · ", reasons),
        $"Worth watching: {Name(game.Away)} versus {Name(game.Home)}. {string.Join(", ", reasons)}.");

    private static GameEvent FinalEvent(HockeyGame game)
    {
        var away = game.Away.Score ?? 0;
        var home = game.Home.Score ?? 0;
        var (winner, loser, w, l) = away > home ? (game.Away, game.Home, away, home) : (game.Home, game.Away, home, away);
        var suffix = (game.LastPeriodType ?? game.PeriodType) switch
        {
            PeriodType.Shootout => " in a shootout",
            PeriodType.Overtime => " in overtime",
            _ => "",
        };
        var label = (game.LastPeriodType ?? game.PeriodType) switch
        {
            PeriodType.Shootout => "FINAL/SO",
            PeriodType.Overtime => "FINAL/OT",
            _ => "FINAL",
        };
        return new GameEvent(GameEventKind.GameFinal, $"final:{game.Id}", game,
            $"{label}  {Matchup(game)}",
            $"{Name(winner)} win {w}-{l}{suffix}",
            $"Final: {Name(winner)} beat the {Name(loser)} {w} to {l}{suffix}.");
    }
}

/// <summary>
/// "Worth switching to" score for a live game: late-game closeness, comebacks and hat-trick chances.
/// Overtime and shootouts are announced by their own event, so they don't score here.
/// </summary>
public static class Excitement
{
    public const int Threshold = 50;

    public static (int Score, IReadOnlyList<string> Reasons) Score(HockeyGame game, IReadOnlyList<PlayerGameGoals> hatTricks)
    {
        if (game.Status != GameStatus.Live || !game.HasScore) return (0, []);

        var away = game.Away.Score!.Value;
        var home = game.Home.Score!.Value;
        var margin = Math.Abs(away - home);
        var score = 0;
        var reasons = new List<string>();

        if (game.PeriodType == PeriodType.Regulation && game.Period == 3)
        {
            var secondsLeft = game.SecondsRemaining ?? 1200;
            if (margin == 0)
            {
                score += 40;
                reasons.Add($"tied {away}-{home}");
            }
            else if (margin == 1)
            {
                score += 25;
                reasons.Add("one-goal game");
            }

            if (margin <= 1)
            {
                if (secondsLeft <= 300) score += 20;
                else if (secondsLeft <= 600) score += 10;
                reasons.Add($"3RD {game.TimeRemaining}");
            }
        }

        var comeback = ComebackSize(game);
        if (comeback >= 2)
        {
            score += 20;
            reasons.Add($"comeback from {comeback} down");
        }

        var watch = hatTricks.FirstOrDefault(p => p.IsWatch);
        if (watch is not null)
        {
            score += 15;
            reasons.Add($"hat-trick watch: {watch.ShortName}");
        }

        return (score, reasons);
    }

    /// <summary>Largest deficit overcome by a team that is now level or ahead.</summary>
    internal static int ComebackSize(HockeyGame game)
    {
        int minDiff = 0, maxDiff = 0; // away minus home over time
        foreach (var goal in game.Goals.Where(g => !g.IsShootout))
        {
            var diff = goal.AwayScore - goal.HomeScore;
            minDiff = Math.Min(minDiff, diff);
            maxDiff = Math.Max(maxDiff, diff);
        }

        var now = (game.Away.Score ?? 0) - (game.Home.Score ?? 0);
        var awayComeback = now >= 0 ? -minDiff : 0;
        var homeComeback = now <= 0 ? maxDiff : 0;
        return Math.Max(awayComeback, homeComeback);
    }
}
