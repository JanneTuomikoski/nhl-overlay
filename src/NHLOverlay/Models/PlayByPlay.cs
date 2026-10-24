namespace NHLOverlay.Models;

public enum PlayType
{
    Other,
    Goal,
    ShotOnGoal,
    MissedShot,
    BlockedShot,
    Hit,
    Penalty,
    Faceoff,
    Giveaway,
    Takeaway,
    Stoppage,
    PeriodStart,
    PeriodEnd,
    GameEnd,
}

/// <summary>
/// On-ice strength from the NHL situation code "ABCD": away goalie (0/1), away skaters, home skaters,
/// home goalie (0/1). "1551" is five on five; "1451" a home power play; "0651" the away goalie pulled.
/// </summary>
public readonly record struct Situation(bool AwayGoalie, int AwaySkaters, int HomeSkaters, bool HomeGoalie)
{
    public static Situation EvenStrength => new(true, 5, 5, true);

    public static Situation Parse(string? code) =>
        code is { Length: 4 } && code.All(char.IsAsciiDigit)
            ? new Situation(code[0] == '1', code[1] - '0', code[2] - '0', code[3] == '1')
            : EvenStrength;

    /// <summary>Skater advantage counting only penalties (a pulled goalie adds an attacker but isn't a power play).</summary>
    public int PenaltyAdvantage
    {
        get
        {
            var away = AwaySkaters - (AwayGoalie ? 0 : 1);
            var home = HomeSkaters - (HomeGoalie ? 0 : 1);
            return away - home; // > 0: away power play, < 0: home power play
        }
    }
}

/// <summary>One event from the play-by-play feed.</summary>
public sealed record PlayEvent
{
    public long EventId { get; init; }

    public int SortOrder { get; init; }

    public PlayType Type { get; init; }

    public int Period { get; init; }

    public PeriodType PeriodType { get; init; }

    public string TimeInPeriod { get; init; } = "";

    public string SituationCode { get; init; } = "";

    /// <summary>Team abbreviation of the team the event belongs to (shooter's team, penalized team, ...).</summary>
    public string? Team { get; init; }

    /// <summary>Rink coordinates in feet: x -100..100, y -42.5..42.5 (null when not located).</summary>
    public int? X { get; init; }

    public int? Y { get; init; }

    /// <summary>"left" or "right": the end the home team defends in this period.</summary>
    public string? HomeDefendingSide { get; init; }

    public long? PrimaryPlayerId { get; init; }

    public long? SecondaryPlayerId { get; init; }

    public long? TertiaryPlayerId { get; init; }

    public string? ShotType { get; init; }

    public string? PenaltyDescription { get; init; }

    public int PenaltyMinutes { get; init; }

    public int? AwayScore { get; init; }

    public int? HomeScore { get; init; }
}

/// <summary>A game's play-by-play with the roster names needed to describe it.</summary>
public sealed record PlayByPlay(
    long GameId,
    int GameType,
    string AwayAbbrev,
    string HomeAbbrev,
    IReadOnlyList<PlayEvent> Plays,
    IReadOnlyDictionary<long, string> PlayerNames)
{
    public string NameOf(long? playerId) =>
        playerId is { } id && PlayerNames.TryGetValue(id, out var name) ? name : "";
}
