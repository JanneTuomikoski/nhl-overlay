namespace NHLOverlay.Models;

/// <summary>Things worth telling the user about. Each kind has its own notification rule.</summary>
public enum GameEventKind
{
    GameStarted,
    Goal,
    HatTrickWatch,
    HatTrickCompleted,
    Overtime,
    CloseGame,
    GameFinal,
    WorthWatching,
}

/// <summary>
/// A detected change in game state. <see cref="Key"/> is a stable identity used to make sure the same event
/// is never announced twice (e.g. "goal:2026020169:2:08:14:EDM").
/// </summary>
public sealed record GameEvent(
    GameEventKind Kind,
    string Key,
    HockeyGame Game,
    string Title,
    string Body,
    string Speech)
{
    /// <summary>Teams involved, used for the favorites-only scope.</summary>
    public IReadOnlyList<string> Teams => [Game.Away.Abbrev, Game.Home.Abbrev];
}
