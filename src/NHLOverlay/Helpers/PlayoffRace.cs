using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>Where a team stands in the playoff race.</summary>
public sealed record RaceStatus(string Position, string Detail, bool InPlayoffSpot);

/// <summary>
/// Playoff-race arithmetic on NHL standings: the top three of each division plus two wildcards per conference
/// make the playoffs. Distances are in points, with games in hand noted (a positive number means this team
/// has played fewer games).
/// </summary>
public static class PlayoffRace
{
    public const int WildcardSpots = 2;

    public static RaceStatus Describe(TeamStanding team, IReadOnlyList<TeamStanding> all)
    {
        var conference = all.Where(t => t.Conference == team.Conference && t.HasStats).ToList();
        var firstOut = conference.FirstOrDefault(t => t.WildcardRank == WildcardSpots + 1);
        var lastIn = conference.FirstOrDefault(t => t.WildcardRank == WildcardSpots);

        if (team.DivisionRank is >= 1 and <= 3)
        {
            return new RaceStatus(
                $"{Ordinal(team.DivisionRank)} {team.DivisionName}",
                firstOut is null ? "" : Distance(team, firstOut, "the cut line"),
                InPlayoffSpot: true);
        }

        if (team.WildcardRank is >= 1 and <= WildcardSpots)
        {
            return new RaceStatus(
                $"Wildcard {team.WildcardRank}",
                firstOut is null ? "" : Distance(team, firstOut, "the cut line"),
                InPlayoffSpot: true);
        }

        return new RaceStatus(
            $"{Ordinal(team.DivisionRank)} {team.DivisionName}",
            lastIn is null ? "Outside the playoff spots" : Distance(team, lastIn, "the last wildcard"),
            InPlayoffSpot: false);
    }

    /// <summary>
    /// "6 pts above the cut line, 2 more games played" / "level with the last wildcard" /
    /// "3 pts behind the last wildcard, 1 game in hand".
    /// </summary>
    internal static string Distance(TeamStanding team, TeamStanding other, string target)
    {
        var points = team.Points - other.Points;
        var text = points == 0
            ? $"level with {target}"
            : $"{Math.Abs(points)} pt{(Math.Abs(points) == 1 ? "" : "s")} {(points > 0 ? "above" : "behind")} {target}";
        var inHand = other.GamesPlayed - team.GamesPlayed;
        if (inHand != 0)
            text += inHand > 0
                ? $", {inHand} game{(inHand == 1 ? "" : "s")} in hand"
                : $", {-inHand} more game{(inHand == -1 ? "" : "s")} played";
        return text;
    }

    public static IReadOnlyList<TeamStanding> Division(IReadOnlyList<TeamStanding> all, string division) =>
        all.Where(t => t.Division == division && t.HasStats).OrderBy(t => t.DivisionRank).ToList();

    /// <summary>Wildcard order for a conference (division top-three teams excluded).</summary>
    public static IReadOnlyList<TeamStanding> Wildcards(IReadOnlyList<TeamStanding> all, string conference) =>
        all.Where(t => t.Conference == conference && t.WildcardRank > 0 && t.HasStats).OrderBy(t => t.WildcardRank).ToList();

    public static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        },
    };
}
