using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Tests;

public class DateTimeHandlingTests
{
    private static DateTimeOffset At(TimeZoneInfo zone, int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTimeOffset Helsinki(int year, int month, int day, int hour, int minute = 0) =>
        At(Zones.Helsinki, year, month, day, hour, minute);

    private static DateOnly TodayInHelsinki(DateTimeOffset now) => NhlCalendar.GetTodayNhlDate(now, Zones.Helsinki, Zones.Eastern);

    [Fact]
    public void Saturday_evening_in_finland_is_the_saturday_slate() =>
        Assert.Equal(new DateOnly(2026, 10, 24), TodayInHelsinki(Helsinki(2026, 10, 24, 20)));

    [Fact]
    public void Games_after_midnight_still_count_as_today()
    {
        // 02:30 on Sunday in Helsinki is still Saturday's hockey night.
        Assert.Equal(new DateOnly(2026, 10, 24), TodayInHelsinki(Helsinki(2026, 10, 25, 2, 30)));
        Assert.Equal(new DateOnly(2026, 10, 24), TodayInHelsinki(Helsinki(2026, 10, 25, 7, 59)));
    }

    [Fact]
    public void Day_rolls_over_in_the_morning()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), TodayInHelsinki(Helsinki(2026, 10, 25, 8)));
        Assert.Equal(new DateOnly(2026, 10, 25), TodayInHelsinki(Helsinki(2026, 10, 25, 23)));
    }

    [Fact]
    public void Handles_the_week_between_eu_and_us_dst_changes()
    {
        // EU is already on winter time (UTC+2) while the US is still on EDT until Nov 1.
        Assert.Equal(new DateOnly(2026, 10, 31), TodayInHelsinki(Helsinki(2026, 11, 1, 3)));
        Assert.Equal(new DateOnly(2026, 11, 1), TodayInHelsinki(Helsinki(2026, 11, 1, 12)));
    }

    [Theory]
    [InlineData("America/New_York", 23, 30)]
    [InlineData("America/Los_Angeles", 21, 0)]
    [InlineData("Europe/London", 23, 0)]
    public void North_american_and_european_evenings_map_to_the_same_slate(string zoneId, int hour, int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        Assert.Equal(new DateOnly(2026, 10, 24), NhlCalendar.GetTodayNhlDate(At(zone, 2026, 10, 24, hour, minute), zone, Zones.Eastern));
    }

    [Fact]
    public void Tonights_games_show_time_only_and_other_days_get_a_weekday()
    {
        var now = Helsinki(2026, 10, 24, 20);
        var culture = CultureInfo.InvariantCulture;

        // 23:00 UTC on Oct 24 is 02:00 EEST (UTC+3) on Sunday: tonight, so no weekday.
        Assert.Equal("02:00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
        // 02:00 UTC on Oct 25 is after the EU DST change: 04:00 EET (UTC+2), not 05:00.
        Assert.Equal("04:00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
        Assert.Equal("22:30", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
        // Sunday evening's game is a different hockey day.
        Assert.Equal("SUN 20:00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 25, 18, 0, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
    }

    [Fact]
    public void Start_time_uses_regional_time_format() =>
        Assert.Equal("2.00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), Zones.Helsinki,
            Helsinki(2026, 10, 24, 20), CultureInfo.GetCultureInfo("fi-FI")));

    [Theory]
    [InlineData(0, "fi-FI", "TODAY")]
    [InlineData(-1, "fi-FI", "YESTERDAY")]
    [InlineData(1, "fi-FI", "TOMORROW")]
    [InlineData(-2, "fi-FI", "THU 22.10")]
    [InlineData(-2, "en-US", "THU 10/22")]
    [InlineData(-2, "de-DE", "THU 22.10")]
    public void Formats_day_labels(int offset, string culture, string expected)
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Equal(expected, GameStatusFormatter.FormatDayLabel(today.AddDays(offset), today, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData(GameStatus.Live, 2, PeriodType.Regulation, "08:14", 494, true, null, "2ND 08:14")]
    [InlineData(GameStatus.Live, 3, PeriodType.Regulation, "00:00", 0, false, null, "END 3RD")]
    [InlineData(GameStatus.Intermission, 1, PeriodType.Regulation, "18:00", 1080, false, null, "1ST INT")]
    [InlineData(GameStatus.Overtime, 4, PeriodType.Overtime, "03:12", 192, true, null, "OT 03:12")]
    [InlineData(GameStatus.Overtime, 6, PeriodType.Overtime, "10:00", 600, true, null, "3OT 10:00")]
    [InlineData(GameStatus.Shootout, 5, PeriodType.Shootout, null, null, false, null, "SO")]
    [InlineData(GameStatus.Final, 3, PeriodType.Regulation, null, null, false, PeriodType.Regulation, "FINAL")]
    [InlineData(GameStatus.Final, 4, PeriodType.Overtime, null, null, false, PeriodType.Overtime, "FINAL/OT")]
    [InlineData(GameStatus.Final, 5, PeriodType.Overtime, null, null, false, PeriodType.Overtime, "FINAL/2OT")]
    [InlineData(GameStatus.Final, 5, PeriodType.Shootout, null, null, false, PeriodType.Shootout, "FINAL/SO")]
    [InlineData(GameStatus.Postponed, null, PeriodType.Unknown, null, null, false, null, "PPD")]
    public void Formats_game_status(GameStatus status, int? period, PeriodType periodType, string? clock, int? seconds, bool running, PeriodType? lastPeriod, string expected)
    {
        var game = new HockeyGame
        {
            Status = status,
            Period = period,
            PeriodType = periodType,
            TimeRemaining = clock,
            SecondsRemaining = seconds,
            ClockRunning = running,
            LastPeriodType = lastPeriod,
        };
        var text = GameStatusFormatter.FormatStatus(game, Zones.Helsinki, DateTimeOffset.UtcNow, CultureInfo.InvariantCulture);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Hides_clock_when_disabled()
    {
        var game = new HockeyGame { Status = GameStatus.Live, Period = 3, PeriodType = PeriodType.Regulation, TimeRemaining = "12:41", SecondsRemaining = 761, ClockRunning = true };
        Assert.Equal("3RD", GameStatusFormatter.FormatStatus(game, Zones.Helsinki, DateTimeOffset.UtcNow, CultureInfo.InvariantCulture, showClock: false));
    }
}
