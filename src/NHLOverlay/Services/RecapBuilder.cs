using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>The "last night" summary.</summary>
public sealed record Recap(
    DateOnly Date,
    int FinishedGames,
    int HiddenGames,
    IReadOnlyList<string> FavoriteResults,
    IReadOnlyList<string> Highlights,
    string CountryTitle,
    IReadOnlyList<string> CountryLines)
{
    public bool IsEmpty => FinishedGames == 0;
}

/// <summary>Builds a <see cref="Recap"/> from one NHL date's finished games. Pure logic.</summary>
public static class RecapBuilder
{
    public static Recap Build(
        DateOnly date,
        IReadOnlyList<HockeyGame> games,
        IReadOnlySet<string> favorites,
        Func<HockeyGame, bool> isShielded,
        Func<long, GameBoxscore?> boxscore,
        Func<HockeyGame, IReadOnlyList<WatchedPlayer>> watchedPlayers,
        IReadOnlyList<string> watchedCountries)
    {
        var finished = games.Where(g => g.Status.IsFinal() && g.HasScore).ToList();
        var visible = finished.Where(g => !isShielded(g)).ToList();

        var favoriteResults = visible
            .Where(g => GameOrdering.IsFavorite(g, favorites))
            .OrderBy(g => g.StartTimeUtc)
            .Select(g => FavoriteLine(g, favorites))
            .ToList();

        var highlights = new List<string>();
        foreach (var (game, player) in visible.SelectMany(g => HatTrickTracker.Analyze(g).Where(p => p.IsHatTrick).Select(p => (g, p))))
            highlights.Add($"Hat trick: {player.Name} ({player.TeamAbbrev}){(player.Goals > 3 ? $", {player.Goals} goals" : "")}");

        foreach (var game in visible.Where(g => g.Away.Score == 0 || g.Home.Score == 0))
            highlights.Add(ShutoutLine(game, boxscore(game.Id)));

        // Note: MaxBy over value tuples throws on an empty sequence, so check for candidates first.
        var comebacks = visible
            .Select(g => (Game: g, Size: Excitement.ComebackSize(g)))
            .Where(c => c.Size >= 2 && Winner(c.Game) is not null)
            .ToList();
        if (comebacks.Count > 0)
        {
            var (game, size) = comebacks.MaxBy(c => c.Size);
            highlights.Add($"Comeback: {Winner(game)!.Abbrev} won from {size} down · {Score(game)}");
        }

        var overtime = visible.Count(g => (g.LastPeriodType ?? g.PeriodType) == PeriodType.Overtime);
        var shootouts = visible.Count(g => (g.LastPeriodType ?? g.PeriodType) == PeriodType.Shootout);
        if (overtime + shootouts > 0)
        {
            var parts = new List<string>();
            if (overtime > 0) parts.Add($"{overtime} in overtime");
            if (shootouts > 0) parts.Add($"{shootouts} in a shootout");
            highlights.Add($"Beyond regulation: {string.Join(", ", parts)}");
        }

        var highest = visible.MaxBy(g => g.Away.Score + g.Home.Score);
        if (highest is not null && highest.Away.Score + highest.Home.Score >= 8)
            highlights.Add($"Highest scoring: {Score(highest)}");

        var countryLines = visible
            .SelectMany(watchedPlayers)
            .Where(p => p.HasStat)
            .OrderBy(p => p.IsGoalie)
            .ThenByDescending(p => p.Points)
            .ThenByDescending(p => p.Goals)
            .Take(8)
            .Select(p => $"{p.ShortName} ({p.Team}) {p.Stat}")
            .ToList();

        return new Recap(
            date,
            finished.Count,
            finished.Count - visible.Count,
            favoriteResults,
            highlights,
            watchedCountries.Count > 0 ? HockeyCountries.PanelTitle(watchedCountries) : "",
            countryLines);
    }

    private static HockeyTeam? Winner(HockeyGame game) =>
        game.Away.Score > game.Home.Score ? game.Away : game.Home.Score > game.Away.Score ? game.Home : null;

    private static string Suffix(HockeyGame game) => (game.LastPeriodType ?? game.PeriodType) switch
    {
        PeriodType.Overtime => " OT",
        PeriodType.Shootout => " SO",
        _ => "",
    };

    private static string Score(HockeyGame game) =>
        $"{game.Away.Abbrev} {game.Away.Score}-{game.Home.Score} {game.Home.Abbrev}{Suffix(game)}";

    /// <summary>"EDM 4-2 TOR  W" from the favorite's point of view (first favorite listed wins ties).</summary>
    private static string FavoriteLine(HockeyGame game, IReadOnlySet<string> favorites)
    {
        var favorite = favorites.Contains(game.Away.Abbrev) ? game.Away : game.Home;
        var won = Winner(game) == favorite;
        var outcome = won ? "W" : (game.LastPeriodType ?? game.PeriodType) is PeriodType.Overtime or PeriodType.Shootout ? "OTL" : "L";
        return $"{Score(game)}  {favorite.Abbrev} {outcome}";
    }

    private static string ShutoutLine(HockeyGame game, GameBoxscore? box)
    {
        var shutoutTeam = game.Away.Score == 0 ? game.Home : game.Away;
        var goalie = box?.Goalies.Where(g => g.Team == shutoutTeam.Abbrev && g.Played).ToList();
        var name = goalie is { Count: 1 }
            ? $"{goalie[0].Name} ({shutoutTeam.Abbrev}), {goalie[0].Saves.ToString(CultureInfo.InvariantCulture)} saves"
            : $"{shutoutTeam.Abbrev}";
        return $"Shutout: {name} · {Score(game)}";
    }
}
