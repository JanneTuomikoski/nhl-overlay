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
    /// Start time in the user's time zone and regional time format. Games in the current hockey day
    /// (08:00-08:00 local) show only the time, so tonight's 02:00 game is simply "2.00"; games on other
    /// days get an English weekday prefix ("SUN 2.00") to match the rest of the UI.
    /// </summary>
    public static string FormatStartTime(DateTimeOffset startUtc, TimeZoneInfo localZone, DateTimeOffset now, CultureInfo culture)
    {
        var localStart = TimeZoneInfo.ConvertTime(startUtc, localZone);
        var time = localStart.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        if (NhlCalendar.GetHockeyDay(startUtc, localZone) == NhlCalendar.GetHockeyDay(now, localZone))
            return time;
        return $"{Weekday(localStart.DayOfWeek)} {time}";
    }

    /// <summary>Header label for a viewed NHL date relative to today's NHL date.</summary>
    public static string FormatDayLabel(DateOnly date, DateOnly today, CultureInfo culture) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "TODAY",
        -1 => "YESTERDAY",
        1 => "TOMORROW",
        _ => FormatShortDate(date, culture),
    };

    /// <summary>"FRI 24.10" (English weekday, regional day/month order).</summary>
    public static string FormatShortDate(DateOnly date, CultureInfo culture) =>
        $"{Weekday(date.DayOfWeek)} {date.ToString(MonthDayPattern(culture), culture)}";

    private static string Weekday(DayOfWeek day) => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(day).ToUpperInvariant();

    /// <summary>The culture's short date pattern with the year removed: "d.M." -> "d.M", "M/d/yyyy" -> "M/d".</summary>
    internal static string MonthDayPattern(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "").Replace("yy", "");
        var separators = new[] { '.', '/', '-', ' ', ',' };
        pattern = pattern.Trim(separators);
        while (pattern.Contains("..")) pattern = pattern.Replace("..", ".");
        while (pattern.Contains("//")) pattern = pattern.Replace("//", "/");
        while (pattern.Contains("--")) pattern = pattern.Replace("--", "-");
        return pattern.Length > 0 ? pattern : "M/d";
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
