using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>A watched-country player in one game, with tonight's points (and goalie line, if any).</summary>
public sealed record WatchedPlayer(long PlayerId, string ShortName, string LastName, string FullName, string Country, string Team, long GameId, int Goals, int Assists, GoalieLine? Goalie, bool IsGoalie)
{
    public int Points => Goals + Assists;

    /// <summary>"1+1" for skaters, "24/26" (saves/shots) for goalies who played, "" otherwise.</summary>
    public string Stat => IsGoalie
        ? Goalie is { Played: true } g ? $"{g.Saves}/{g.ShotsAgainst}" : ""
        : Points > 0 ? $"{Goals}+{Assists}" : "";

    /// <summary>Has something to show tonight: points, or played in goal.</summary>
    public bool HasStat => Stat.Length > 0;
}

/// <summary>Finds watched-country players in games. Pure logic over rosters, goal lists and boxscores.</summary>
public static class CountryWatch
{
    public static IReadOnlyList<WatchedPlayer> ForGame(
        HockeyGame game,
        IReadOnlySet<string> countries,
        Func<string, IReadOnlyList<RosterPlayer>> roster,
        Func<long, GameBoxscore?> boxscore)
    {
        if (countries.Count == 0) return [];

        var goals = game.Goals.Where(g => !g.IsShootout).ToList();
        var goalsBy = goals.GroupBy(g => g.PlayerId).ToDictionary(g => g.Key, g => g.Count());
        var assistsBy = goals.SelectMany(g => g.Assists).GroupBy(a => a.PlayerId).ToDictionary(g => g.Key, g => g.Count());
        var goalies = boxscore(game.Id)?.Goalies.ToDictionary(g => g.PlayerId) ?? [];

        return new[] { game.Away.Abbrev, game.Home.Abbrev }
            .SelectMany(team => roster(team))
            .Where(p => countries.Contains(p.Country))
            .DistinctBy(p => p.Id)
            .Select(p => new WatchedPlayer(
                p.Id, p.ShortName, p.LastName, p.FullName, p.Country, p.Team, game.Id,
                goalsBy.GetValueOrDefault(p.Id), assistsBy.GetValueOrDefault(p.Id),
                goalies.GetValueOrDefault(p.Id), p.IsGoalie))
            .OrderByDescending(p => p.Points)
            .ThenByDescending(p => p.Goals)
            .ThenBy(p => p.LastName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Watched-country goalies on the rosters of a game (their stats need the boxscore).</summary>
    public static bool HasWatchedGoalie(HockeyGame game, IReadOnlySet<string> countries, Func<string, IReadOnlyList<RosterPlayer>> roster) =>
        new[] { game.Away.Abbrev, game.Home.Abbrev }.SelectMany(roster).Any(p => p.IsGoalie && countries.Contains(p.Country));
}
