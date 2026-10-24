namespace NHLOverlay.Helpers;

/// <summary>
/// Maps "now" to NHL schedule dates.
/// <para>
/// NHL schedule dates are North American dates, so the local calendar date must never be used directly:
/// for a European user an NHL Saturday game often starts at 02:00 on Sunday. Instead the app uses the
/// user's <i>hockey day</i>, which runs from 08:00 to 08:00 local time. Tonight's games, including those
/// after midnight, therefore count as "today", and the day only rolls over the next morning, after the
/// last games of the night have finished.
/// </para>
/// </summary>
public static class NhlCalendar
{
    /// <summary>Local hour at which a new hockey day begins.</summary>
    public const int HockeyDayStartHour = 8;

    public static TimeZoneInfo EasternTimeZone { get; } = FindEasternTimeZone();

    /// <summary>The local calendar date of the hockey day containing <paramref name="instant"/>.</summary>
    public static DateOnly GetHockeyDay(DateTimeOffset instant, TimeZoneInfo localZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, localZone);
        var day = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < HockeyDayStartHour ? day.AddDays(-1) : day;
    }

    /// <summary>The instant the hockey day after the one containing <paramref name="now"/> begins.</summary>
    public static DateTimeOffset GetNextHockeyDayStart(DateTimeOffset now, TimeZoneInfo localZone)
    {
        var next = GetHockeyDay(now, localZone).AddDays(1).ToDateTime(new TimeOnly(HockeyDayStartHour, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(next, localZone), TimeSpan.Zero);
    }

    /// <summary>
    /// The NHL schedule date played during the user's current hockey day: the NHL date containing the
    /// afternoon (16:00 local) of that day. For every time zone from Hawaii to eastern Europe this is the
    /// slate that starts that local evening or night.
    /// </summary>
    public static DateOnly GetTodayNhlDate(DateTimeOffset now, TimeZoneInfo localZone, TimeZoneInfo? eastern = null)
    {
        var hockeyDay = GetHockeyDay(now, localZone);
        var afternoon = hockeyDay.ToDateTime(new TimeOnly(16, 0), DateTimeKind.Unspecified);
        var afternoonUtc = TimeZoneInfo.ConvertTimeToUtc(afternoon, localZone);
        var easternAfternoon = TimeZoneInfo.ConvertTimeFromUtc(afternoonUtc, eastern ?? EasternTimeZone);
        return DateOnly.FromDateTime(easternAfternoon);
    }

    private static TimeZoneInfo FindEasternTimeZone()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Extremely unlikely on Windows; a fixed offset is better than crashing.
        return TimeZoneInfo.CreateCustomTimeZone("NHL-Eastern", TimeSpan.FromHours(-5), "Eastern", "Eastern");
    }
}
