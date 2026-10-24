namespace NHLOverlay.Helpers;

/// <summary>
/// Maps "now" to the NHL schedule dates worth showing.
/// <para>
/// NHL schedule dates are North American dates. For a European user an NHL "Saturday" game can start
/// early on Sunday morning local time, so the local calendar date must never be used to pick the NHL
/// date. Instead we evaluate the current instant in US Eastern time (handles DST on both continents)
/// and also keep the previous NHL date until midday Eastern, so last night's late games and results
/// remain visible through the European morning.
/// </para>
/// </summary>
public static class NhlCalendar
{
    /// <summary>Eastern-time hour before which the previous NHL date is still included.</summary>
    public const int PreviousDateCutoffHour = 12;

    public static TimeZoneInfo EasternTimeZone { get; } = FindEasternTimeZone();

    public static IReadOnlyList<DateOnly> GetRelevantDates(DateTimeOffset now, TimeZoneInfo? eastern = null)
    {
        var easternNow = TimeZoneInfo.ConvertTime(now, eastern ?? EasternTimeZone);
        var today = DateOnly.FromDateTime(easternNow.DateTime);
        return easternNow.Hour < PreviousDateCutoffHour ? [today.AddDays(-1), today] : [today];
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
