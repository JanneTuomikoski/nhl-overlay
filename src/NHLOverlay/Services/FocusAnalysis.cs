using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

public sealed record PeriodShots(string Label, int Away, int Home);

/// <summary>A shot on the rink map, normalized so the away team always attacks to the right.</summary>
public sealed record ShotMark(double X, double Y, bool IsAway, bool IsGoal, bool IsMiss, string Description);

public enum FeedKind
{
    Goal,
    Penalty,
    Shot,
    Hit,
    Period,
    Goalie,
}

public sealed record FeedItem(string When, string Team, FeedKind Kind, string Text);

/// <summary>Pure analysis of a play-by-play feed for the focus dashboard.</summary>
public static class FocusAnalysis
{
    /// <summary>Strength label for the current situation, e.g. "PP NJD · 5 on 4", "LAK goalie pulled · 6 on 5".</summary>
    public static string StrengthText(Situation s, string away, string home)
    {
        var parts = new List<string>();
        var advantage = s.PenaltyAdvantage;
        if (advantage != 0) parts.Add($"PP {(advantage > 0 ? away : home)}");
        if (!s.AwayGoalie) parts.Add($"{away} goalie pulled");
        if (!s.HomeGoalie) parts.Add($"{home} goalie pulled");

        var onIce = $"{Math.Max(s.AwaySkaters, s.HomeSkaters)} on {Math.Min(s.AwaySkaters, s.HomeSkaters)}";
        if (parts.Count == 0)
            return s.AwaySkaters == 5 && s.HomeSkaters == 5 ? "Even strength" : onIce;
        parts.Add(onIce);
        return string.Join(" · ", parts);
    }

    /// <summary>Length of a period in seconds: 20 minutes, except 5-minute regular-season overtime.</summary>
    internal static int PeriodLength(int period, int maxRegulation, int gameType) =>
        period <= maxRegulation || gameType == 3 ? 1200 : 300;

    internal static int Elapsed(int period, string timeInPeriod, int maxRegulation, int gameType)
    {
        var before = 0;
        for (var p = 1; p < period; p++) before += PeriodLength(p, maxRegulation, gameType);
        return before + GameEventDetector.Seconds(timeInPeriod);
    }

    /// <summary>
    /// Approximate time left on the current power play: the soonest-expiring active minor or major of the
    /// shorthanded team. Null when not on a power play. (A power-play goal ends a minor early; the NHL's
    /// situation code reflects that on the next play, so the countdown disappears then.)
    /// </summary>
    public static TimeSpan? PowerPlayRemaining(PlayByPlay pbp, Situation situation, int currentPeriod, int secondsRemaining, int maxRegulation = 3)
    {
        var advantage = situation.PenaltyAdvantage;
        if (advantage == 0) return null;
        var shorthanded = advantage > 0 ? pbp.HomeAbbrev : pbp.AwayAbbrev;
        var now = Elapsed(currentPeriod, "00:00", maxRegulation, pbp.GameType)
                  + PeriodLength(currentPeriod, maxRegulation, pbp.GameType) - secondsRemaining;

        var remaining = pbp.Plays
            .Where(p => p.Type == PlayType.Penalty && p.Team == shorthanded && p.PenaltyMinutes is > 0 and < 10)
            .Select(p => Elapsed(p.Period, p.TimeInPeriod, maxRegulation, pbp.GameType) + (p.PenaltyMinutes * 60) - now)
            .Where(left => left > 0)
            .DefaultIfEmpty(0)
            .Min();
        return remaining > 0 ? TimeSpan.FromSeconds(remaining) : null;
    }

    /// <summary>Shots on goal (goals included) per period.</summary>
    public static IReadOnlyList<PeriodShots> ShotsByPeriod(PlayByPlay pbp, int maxRegulation = 3) =>
        pbp.Plays
            .Where(p => p.Type is PlayType.ShotOnGoal or PlayType.Goal && p.PeriodType != PeriodType.Shootout && p.Period > 0)
            .GroupBy(p => p.Period)
            .OrderBy(g => g.Key)
            .Select(g => new PeriodShots(
                GameStatusFormatter.PeriodLabel(g.Key, g.First().PeriodType, maxRegulation),
                g.Count(p => p.Team == pbp.AwayAbbrev),
                g.Count(p => p.Team == pbp.HomeAbbrev)))
            .ToList();

    /// <summary>Located shots and goals, flipped so the away team attacks right and the home team left.</summary>
    public static IReadOnlyList<ShotMark> ShotMarks(PlayByPlay pbp, bool includeMisses)
    {
        var marks = new List<ShotMark>();
        foreach (var p in pbp.Plays)
        {
            if (p.X is not { } x || p.Y is not { } y || p.Team is null || p.PeriodType == PeriodType.Shootout) continue;
            var isMiss = p.Type == PlayType.MissedShot;
            if (p.Type is not (PlayType.Goal or PlayType.ShotOnGoal) && !(includeMisses && isMiss)) continue;

            var isAway = p.Team == pbp.AwayAbbrev;
            // The home team attacks away from the end it defends; the away team attacks the end the home team defends.
            var attacksRight = isAway ? p.HomeDefendingSide != "left" : p.HomeDefendingSide == "left";
            var wantRight = isAway;
            var flip = attacksRight != wantRight;
            var shooter = pbp.NameOf(p.PrimaryPlayerId);
            var when = $"{GameStatusFormatter.PeriodLabel(p.Period, p.PeriodType, 3)} {p.TimeInPeriod}";
            marks.Add(new ShotMark(
                flip ? -x : x,
                flip ? -y : y,
                isAway,
                p.Type == PlayType.Goal,
                isMiss,
                $"{(p.Type == PlayType.Goal ? "Goal" : isMiss ? "Missed shot" : "Shot")} · {shooter} ({p.Team}) · {when}"));
        }

        return marks;
    }

    /// <summary>Newest-first event feed: goals, penalties, periods and goalie changes, plus shots and hits if asked.</summary>
    public static IReadOnlyList<FeedItem> Feed(PlayByPlay pbp, bool includeShots, bool includeHits, int maxItems = 60)
    {
        var items = new List<FeedItem>();
        var previous = Situation.EvenStrength;
        foreach (var p in pbp.Plays)
        {
            var when = p.Period > 0 ? $"{GameStatusFormatter.PeriodLabel(p.Period, p.PeriodType, 3)} {p.TimeInPeriod}" : "";
            var situation = Situation.Parse(p.SituationCode);

            // Goalie pulled / returned shows up as a change in the situation code.
            if (p.SituationCode.Length == 4 && p.Type != PlayType.PeriodStart)
            {
                if (previous.AwayGoalie && !situation.AwayGoalie) items.Add(new FeedItem(when, pbp.AwayAbbrev, FeedKind.Goalie, $"{pbp.AwayAbbrev} pulls the goalie"));
                if (previous.HomeGoalie && !situation.HomeGoalie) items.Add(new FeedItem(when, pbp.HomeAbbrev, FeedKind.Goalie, $"{pbp.HomeAbbrev} pulls the goalie"));
                previous = situation;
            }

            switch (p.Type)
            {
                case PlayType.Goal:
                    var assists = new[] { pbp.NameOf(p.SecondaryPlayerId), pbp.NameOf(p.TertiaryPlayerId) }.Where(n => n.Length > 0).ToList();
                    var tag = GoalTag(p, situation, pbp);
                    items.Add(new FeedItem(when, p.Team ?? "", FeedKind.Goal,
                        $"GOAL  {pbp.NameOf(p.PrimaryPlayerId)}{(assists.Count > 0 ? $" ({string.Join(", ", assists)})" : "")}"
                        + $"{(tag.Length > 0 ? "  " + tag : "")}  {p.AwayScore}-{p.HomeScore}"));
                    break;
                case PlayType.Penalty:
                    items.Add(new FeedItem(when, p.Team ?? "", FeedKind.Penalty,
                        $"{pbp.NameOf(p.PrimaryPlayerId)}  {p.PenaltyMinutes} min  {p.PenaltyDescription}".Trim()));
                    break;
                case PlayType.PeriodStart:
                    items.Add(new FeedItem("", "", FeedKind.Period, $"Start of {Describe(p)}"));
                    break;
                case PlayType.PeriodEnd:
                    items.Add(new FeedItem("", "", FeedKind.Period, $"End of {Describe(p)}"));
                    break;
                case PlayType.GameEnd:
                    items.Add(new FeedItem("", "", FeedKind.Period, "Final"));
                    break;
                case PlayType.ShotOnGoal when includeShots:
                    items.Add(new FeedItem(when, p.Team ?? "", FeedKind.Shot, $"Shot  {pbp.NameOf(p.PrimaryPlayerId)}{(p.ShotType is { } shot ? $" ({shot})" : "")}"));
                    break;
                case PlayType.Hit when includeHits:
                    items.Add(new FeedItem(when, p.Team ?? "", FeedKind.Hit, $"Hit  {pbp.NameOf(p.PrimaryPlayerId)} on {pbp.NameOf(p.SecondaryPlayerId)}"));
                    break;
            }
        }

        items.Reverse();
        return items.Take(maxItems).ToList();
    }

    private static string Describe(PlayEvent p) => p.PeriodType switch
    {
        PeriodType.Overtime => "overtime",
        PeriodType.Shootout => "the shootout",
        _ => $"the {PlayoffRace.Ordinal(p.Period)} period",
    };

    /// <summary>"PP", "SH" or "EN" from the scoring team's point of view.</summary>
    private static string GoalTag(PlayEvent goal, Situation s, PlayByPlay pbp)
    {
        var scoredByAway = goal.Team == pbp.AwayAbbrev;
        var tags = new List<string>();
        var advantage = s.PenaltyAdvantage;
        if ((advantage > 0 && scoredByAway) || (advantage < 0 && !scoredByAway)) tags.Add("PP");
        else if (advantage != 0) tags.Add("SH");
        if ((scoredByAway && !s.HomeGoalie) || (!scoredByAway && !s.AwayGoalie)) tags.Add("EN");
        return string.Join(" ", tags);
    }
}
