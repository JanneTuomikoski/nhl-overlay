using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Shutout watch: late in the third period (ten minutes or less) or in overtime while a team hasn't conceded,
/// and again when the shutout is complete. The goalie comes from the boxscore; until it has loaded the game is
/// reported in <see cref="Result.NeedBoxscore"/> and checked again later. Games are a silent baseline the first
/// time they are seen.
/// </summary>
public sealed class ShutoutTracker
{
    private readonly HashSet<long> _seenGames = [];
    private readonly HashSet<string> _emitted = [];

    public sealed record Result(IReadOnlyList<GameEvent> Events, IReadOnlyList<HockeyGame> NeedBoxscore);

    public Result Update(IReadOnlyList<HockeyGame> games, Func<long, GameBoxscore?> boxscore)
    {
        var events = new List<GameEvent>();
        var need = new List<HockeyGame>();
        foreach (var game in games)
        {
            var firstSighting = _seenGames.Add(game.Id);
            foreach (var (team, final) in Shutouts(game))
            {
                var key = $"{(final ? "so" : "sow")}:{game.Id}:{team.Abbrev}";
                if (firstSighting)
                {
                    _emitted.Add(key);
                    if (final) _emitted.Add($"sow:{game.Id}:{team.Abbrev}");
                    continue;
                }

                if (_emitted.Contains(key)) continue;
                if (boxscore(game.Id) is not { } box || box.Goalies.Count == 0)
                {
                    need.Add(game);
                    continue;
                }

                _emitted.Add(key);
                if (final) _emitted.Add($"sow:{game.Id}:{team.Abbrev}");
                events.Add(Event(game, team, GoalieText(box, team.Abbrev), final, key));
            }
        }

        return new Result(events, need);
    }

    /// <summary>Teams whose goalie(s) have a shutout going (late in the game) or completed one.</summary>
    internal static IEnumerable<(HockeyTeam Team, bool Final)> Shutouts(HockeyGame game)
    {
        if (!game.HasScore) yield break;
        var late = game.Status.IsInProgress() && !game.InIntermission
                   && ((game.PeriodType == PeriodType.Regulation && game.Period == 3 && game.SecondsRemaining is <= 600)
                       || game.PeriodType == PeriodType.Overtime);
        var final = game.Status.IsFinal();
        if (!late && !final) yield break;
        if (game.Home.Score == 0) yield return (game.Away, final);
        if (game.Away.Score == 0) yield return (game.Home, final);
    }

    /// <summary>"J. Saros, 28 saves" or "A. Goalie and B. Goalie, 31 saves" for a combined shutout.</summary>
    internal static string GoalieText(GameBoxscore box, string team)
    {
        var goalies = box.Goalies.Where(g => g.Team == team && g.Played).ToList();
        if (goalies.Count == 0) return team;
        return $"{string.Join(" and ", goalies.Select(g => g.Name))}, {goalies.Sum(g => g.Saves)} saves";
    }

    /// <summary>Card line for a game with a shutout going or completed, or null.</summary>
    public static string? CardLine(HockeyGame game, GameBoxscore? box)
    {
        var (team, final) = Shutouts(game).FirstOrDefault();
        if (team is null) return null;
        var goalie = box is null ? team.Abbrev : GoalieText(box, team.Abbrev).Split(',')[0];
        return final ? $"SHUTOUT  {goalie} ({team.Abbrev})" : $"SHUTOUT WATCH  {goalie} ({team.Abbrev})";
    }

    private static GameEvent Event(HockeyGame game, HockeyTeam team, string goalie, bool final, string key)
    {
        var matchup = $"{game.Away.Abbrev} {game.Away.Score}-{game.Home.Score} {game.Home.Abbrev}";
        return final
            ? new GameEvent(GameEventKind.Shutout, key, game, $"SHUTOUT  {matchup}", $"{goalie} ({team.Abbrev})",
                $"Shutout! {goalie.Split(',')[0]} blanks the opposition for the {team.Name}.")
            : new GameEvent(GameEventKind.Shutout, key, game, $"SHUTOUT WATCH  {matchup}", $"{goalie} ({team.Abbrev}) · {game.TimeRemaining} left",
                $"Shutout watch: {goalie.Split(',')[0]} for the {team.Name}.");
    }
}
