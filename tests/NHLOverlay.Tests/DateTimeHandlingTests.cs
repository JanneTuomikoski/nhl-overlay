using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Tests;

public class DateTimeHandlingTests
{
    private static DateTimeOffset Helsinki(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zones.Helsinki.GetUtcOffset(local));
    }

    [Fact]
    public void Late_saturday_night_in_finland_still_shows_the_saturday_nhl_date()
    {
        // 02:30 Sunday in Helsinki is 19:30 Saturday in New York.
        var dates = NhlCalendar.GetRelevantDates(Helsinki(2026, 10, 25, 2, 30), Zones.Eastern);
        Assert.Equal([new DateOnly(2026, 10, 24)], dates);
    }

    [Fact]
    public void Finnish_morning_includes_last_nights_nhl_date()
    {
        // 10:00 Sunday in Helsinki (EU DST already ended) is 04:00 Sunday in New York.
        var dates = NhlCalendar.GetRelevantDates(Helsinki(2026, 10, 25, 10), Zones.Eastern);
        Assert.Equal([new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25)], dates);
    }

    [Fact]
    public void Finnish_evening_shows_only_the_current_nhl_date()
    {
        var dates = NhlCalendar.GetRelevantDates(Helsinki(2026, 10, 25, 20), Zones.Eastern);
        Assert.Equal([new DateOnly(2026, 10, 25)], dates);
    }

    [Fact]
    public void Handles_the_us_dst_change_night()
    {
        // US DST ends 2026-11-01 02:00 EDT (06:00 UTC). 06:30 UTC is 01:30 EST on Nov 1.
        var dates = NhlCalendar.GetRelevantDates(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), Zones.Eastern);
        Assert.Equal([new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1)], dates);
    }

    [Fact]
    public void Start_time_converts_to_local_time_across_the_eu_dst_change()
    {
        var now = Helsinki(2026, 10, 24, 20);
        var culture = CultureInfo.InvariantCulture;

        // 23:00 UTC on Oct 24 is 02:00 EEST (UTC+3) on Sunday.
        Assert.Equal("Sun 02:00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
        // 02:00 UTC on Oct 25 is after the change: 04:00 EET (UTC+2), not 05:00.
        Assert.Equal("Sun 04:00", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
        // Same local day: time only.
        Assert.Equal("22:30", GameStatusFormatter.FormatStartTime(new DateTimeOffset(2026, 10, 24, 19, 30, 0, TimeSpan.Zero), Zones.Helsinki, now, culture));
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
