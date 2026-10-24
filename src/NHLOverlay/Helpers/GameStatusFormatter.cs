using System.Globalization;
using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>Builds the short status label shown on a game card ("3RD 12:41", "FINAL/OT", "02:00" ...).</summary>
public static class GameStatusFormatter
{
    public static string FormatStatus(HockeyGame game, TimeZoneInfo localZone, DateTimeOffset now, CultureInfo culture, bool showClock = true)
    {
        switch (game.Status)
        {
            case GameStatus.Postponed:
                return "PPD";
            case GameStatus.Suspended:
                return "SUSP";
            case GameStatus.Cancelled:
                return "CANCELED";
            case GameStatus.Scheduled:
            case GameStatus.PreGame:
                return FormatStartTime(game.StartTimeUtc, localZone, now, culture);
            case GameStatus.Intermission:
                return $"{PeriodLabel(game)} INT";
            case GameStatus.Shootout:
                return "SO";
            case GameStatus.Live:
            case GameStatus.Overtime:
                var label = PeriodLabel(game);
                if (!game.ClockRunning && game.SecondsRemaining == 0)
                    return $"END {label}";
                return showClock && !string.IsNullOrEmpty(game.TimeRemaining) ? $"{label} {game.TimeRemaining}" : label;
            case GameStatus.Final:
                var endedIn = game.LastPeriodType ?? game.PeriodType;
                return endedIn switch
                {
                    PeriodType.Shootout => "FINAL/SO",
                    PeriodType.Overtime => $"FINAL/{PeriodLabel(game.Period, PeriodType.Overtime, game.MaxRegulationPeriods)}",
                    _ => "FINAL",
                };
            default:
                return game.StartTimeUtc > now ? FormatStartTime(game.StartTimeUtc, localZone, now, culture) : "";
        }
    }

    /// <summary>
    /// Start time in the user's time zone. Prefixed with the weekday when the game is not on the
    /// user's current local date (e.g. a Saturday NHL game shown as "Sun 02:00" in Finland).
    /// </summary>
    public static string FormatStartTime(DateTimeOffset startUtc, TimeZoneInfo localZone, DateTimeOffset now, CultureInfo culture)
    {
        var localStart = TimeZoneInfo.ConvertTime(startUtc, localZone);
        var localNow = TimeZoneInfo.ConvertTime(now, localZone);
        var time = localStart.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        return localStart.Date == localNow.Date ? time : $"{localStart.ToString("ddd", culture)} {time}";
    }

    public static string PeriodLabel(HockeyGame game) => PeriodLabel(game.Period, game.PeriodType, game.MaxRegulationPeriods);

    public static string PeriodLabel(int? number, PeriodType type, int maxRegulationPeriods)
    {
        if (type == PeriodType.Shootout) return "SO";
        if (number is not { } n || n <= 0) return type == PeriodType.Overtime ? "OT" : "";

        if (type == PeriodType.Overtime || n > maxRegulationPeriods)
        {
            var overtime = n - maxRegulationPeriods;
            return overtime <= 1 ? "OT" : $"{overtime}OT";
        }

        return n switch
        {
            1 => "1ST",
            2 => "2ND",
            3 => "3RD",
            _ => $"{n}TH",
        };
    }
}
