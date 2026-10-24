using NHLOverlay.Models;
using NHLOverlay.Services;
using static NHLOverlay.Tests.TestGames;

namespace NHLOverlay.Tests;

public class ScorePollingServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // 10:00 Sunday in Helsinki: today's slate is Sunday Oct 25, last night was Saturday Oct 24.
    private static readonly DateTimeOffset Morning = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Yesterday = new(2026, 10, 24);
    private static readonly DateOnly Today = new(2026, 10, 25);

    private readonly FakeNhlApi _api = new();
    private readonly GameStateService _state = new();
    private readonly ManualTimeProvider _time = new(Morning);

    private ScorePollingService CreateService() => new(_api, _state, () => Interval, _time, Zones.Helsinki, Zones.Eastern);

    [Fact]
    public async Task Loads_yesterday_and_today_and_shows_today_as_the_main_view()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2)];
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(15))];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);

        var snapshot = _state.Current;
        Assert.Equal(Today, snapshot.TodayNhlDate);
        Assert.Equal([2L], snapshot.Games.Select(g => g.Id));
        Assert.Equal([1L], snapshot.GamesByDate[Yesterday].Select(g => g.Id));
        Assert.True(snapshot.HasData);
        Assert.False(snapshot.IsStale);
        Assert.Equal(Morning, snapshot.LastSuccessUtc);
    }

    [Fact]
    public async Task Today_view_keeps_last_nights_unfinished_games()
    {
        _api.Games[Yesterday] =
        [
            Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2),
            Game(2, GameStatus.Overtime, "LIVE", Morning.AddHours(-3), 2, 2),
            Game(3, GameStatus.Final, "FINAL", Morning.AddHours(-3), 4, 1),
        ];
        _api.Games[Today] = [Game(4, GameStatus.Scheduled, "FUT", Morning.AddHours(15))];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal([2L, 3L, 4L], _state.Current.Games.Select(g => g.Id).Order());
    }

    [Fact]
    public async Task Does_not_refetch_a_date_whose_games_are_all_official()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2)];
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(15))];
        using var service = CreateService();

        for (var i = 0; i < 3; i++)
        {
            await service.PollOnceAsync(CancellationToken.None);
            _time.Now += ScorePollingService.IdleRecheck;
        }

        Assert.Equal(1, _api.Requests.Count(d => d == Yesterday));
        Assert.Equal(3, _api.Requests.Count(d => d == Today));
    }

    [Fact]
    public async Task Keeps_refetching_a_date_with_a_final_that_is_not_yet_official()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "FINAL", Morning.AddHours(-3), 3, 2)];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);
        _time.Now += Interval;
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, _api.Requests.Count(d => d == Yesterday));
    }

    [Fact]
    public async Task Loads_a_viewed_date_on_demand_and_drops_it_afterwards()
    {
        var older = new DateOnly(2026, 10, 20);
        _api.Games[older] = [Game(9, GameStatus.Final, "OFF", Morning.AddDays(-5), 1, 0)];
        using var service = CreateService();

        service.SetViewedDate(older);
        await service.PollOnceAsync(CancellationToken.None);
        Assert.Equal([9L], _state.Current.GamesByDate[older].Select(g => g.Id));

        service.SetViewedDate(null);
        await service.PollOnceAsync(CancellationToken.None);
        Assert.False(_state.Current.GamesByDate.ContainsKey(older));
        Assert.Equal(1, _api.Requests.Count(d => d == older));
    }

    [Fact]
    public async Task Rolls_over_to_the_next_slate_in_the_morning()
    {
        using var service = CreateService();
        _time.Now = new DateTimeOffset(2026, 10, 25, 5, 59, 0, TimeSpan.Zero); // 07:59 in Helsinki
        await service.PollOnceAsync(CancellationToken.None);
        Assert.Equal(Yesterday, _state.Current.TodayNhlDate);

        _time.Now = new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero); // 08:00
        await service.PollOnceAsync(CancellationToken.None);
        Assert.Equal(Today, _state.Current.TodayNhlDate);
    }

    [Fact]
    public async Task Failure_keeps_cached_games_and_marks_them_stale()
    {
        _api.Games[Today] = [Game(2, GameStatus.Live, "LIVE", Morning.AddHours(-1), 1, 0)];
        using var service = CreateService();
        await service.PollOnceAsync(CancellationToken.None);

        _api.Failure = new NhlApiException("boom");
        _time.Now = Morning.AddMinutes(1);
        var delay = await service.PollOnceAsync(CancellationToken.None);

        var snapshot = _state.Current;
        Assert.Contains(snapshot.Games, g => g.Id == 2);
        Assert.True(snapshot.IsStale);
        Assert.Equal("boom", snapshot.LastError);
        Assert.Equal(Morning, snapshot.LastSuccessUtc);
        Assert.Equal(Interval, delay);
    }

    [Fact]
    public async Task Backs_off_after_repeated_failures_and_recovers()
    {
        _api.Failure = new NhlApiException("offline");
        using var service = CreateService();

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 6; i++)
        {
            delays.Add(await service.PollOnceAsync(CancellationToken.None));
            _time.Now += delays[^1];
        }

        Assert.Equal([30, 60, 120, 240, 300, 300], delays.Select(d => (int)d.TotalSeconds));
        Assert.False(_state.Current.HasData);

        _api.Failure = null;
        _api.Games[Today] = [Game(2, GameStatus.Live, "LIVE", Morning.AddHours(-1), 0, 0)];
        Assert.Equal(Interval, await service.PollOnceAsync(CancellationToken.None));
        Assert.False(_state.Current.IsStale);
    }

    [Fact]
    public void Honors_retry_after_when_longer_than_backoff() =>
        Assert.Equal(TimeSpan.FromMinutes(2), ScorePollingService.Backoff(Interval, 1, TimeSpan.FromMinutes(2)));

    [Fact]
    public void Polls_at_the_interval_while_games_are_live() =>
        Assert.Equal(Interval, ScorePollingService.NextDelay([Game(1, GameStatus.Intermission, "LIVE", Morning, 1, 1)], Morning, Interval));

    [Fact]
    public void Slows_down_when_the_next_game_is_far_away()
    {
        var games = new[] { Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 1, 2), Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(10)) };
        Assert.Equal(ScorePollingService.IdleRecheck, ScorePollingService.NextDelay(games, Morning, Interval));
    }

    [Fact]
    public void A_day_without_games_is_checked_rarely() =>
        Assert.Equal(ScorePollingService.NoGamesRecheck, ScorePollingService.NextDelay([], Morning, Interval));

    [Fact]
    public async Task Idle_dates_are_not_refetched_until_due()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2)];
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddMinutes(40))];
        using var service = CreateService();

        // Next start in 40 min: sleep until 10 minutes before it.
        Assert.Equal(TimeSpan.FromMinutes(30), await service.PollOnceAsync(CancellationToken.None));
        Assert.Equal(Morning.AddMinutes(30), _state.Current.NextRefreshUtc);

        _time.Now = Morning.AddMinutes(10);
        await service.PollOnceAsync(CancellationToken.None); // a nudge (e.g. settings saved) fetches nothing
        Assert.Equal(1, _api.Requests.Count(d => d == Today));

        _time.Now = Morning.AddMinutes(30);
        Assert.Equal(Interval, await service.PollOnceAsync(CancellationToken.None)); // warm-up: user interval
        Assert.Equal(2, _api.Requests.Count(d => d == Today));
    }

    [Fact]
    public async Task Manual_refresh_refetches_dates_that_are_not_due()
    {
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(10))];
        using var service = CreateService();
        await service.PollOnceAsync(CancellationToken.None);

        service.RequestRefresh();
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, _api.Requests.Count(d => d == Today));
    }

    [Fact]
    public async Task Off_season_sleeps_until_the_morning_rollover_at_most()
    {
        using var service = CreateService();
        _time.Now = new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.Zero); // 05:00 in Helsinki, no games anywhere

        var delay = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(3), delay); // 08:00 rollover comes before the 6 h recheck
    }

    [Fact]
    public void Next_hockey_day_starts_at_eight_local_across_dst() =>
        // 01:00 on Oct 25 in Helsinki is still the Oct 24 hockey day; DST ends at 04:00 that night.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero),
            Helpers.NhlCalendar.GetNextHockeyDayStart(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), Zones.Helsinki));

    [Fact]
    public void Wakes_up_shortly_before_the_next_game()
    {
        var games = new[] { Game(2, GameStatus.Scheduled, "FUT", Morning.AddMinutes(13)) };
        Assert.Equal(TimeSpan.FromMinutes(3), ScorePollingService.NextDelay(games, Morning, Interval));
    }

    [Fact]
    public void Polls_at_the_interval_when_a_game_is_past_its_start_but_not_live() =>
        Assert.Equal(Interval, ScorePollingService.NextDelay([Game(2, GameStatus.Scheduled, "FUT", Morning.AddMinutes(-5))], Morning, Interval));
}
