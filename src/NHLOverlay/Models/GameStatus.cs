namespace NHLOverlay.Models;

/// <summary>
/// Normalized game state, derived from the NHL <c>gameState</c>, <c>gameScheduleState</c>,
/// period type and intermission flag. Never inferred from the local clock.
/// </summary>
public enum GameStatus
{
    Unknown,
    Scheduled,
    PreGame,
    Live,
    Intermission,
    Overtime,
    Shootout,
    Final,
    Postponed,
    Suspended,
    Cancelled,
}

public enum PeriodType
{
    Unknown,
    Regulation,
    Overtime,
    Shootout,
}

public static class GameStatusExtensions
{
    public static bool IsInProgress(this GameStatus status) =>
        status is GameStatus.Live or GameStatus.Intermission or GameStatus.Overtime or GameStatus.Shootout;

    public static bool IsUpcoming(this GameStatus status) =>
        status is GameStatus.Scheduled or GameStatus.PreGame;

    public static bool IsFinal(this GameStatus status) => status == GameStatus.Final;

    /// <summary>Postponed, suspended or cancelled: the game will not produce updates today.</summary>
    public static bool IsInactive(this GameStatus status) =>
        status is GameStatus.Postponed or GameStatus.Suspended or GameStatus.Cancelled;
}
