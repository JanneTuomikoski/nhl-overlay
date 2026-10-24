using NHLOverlay.Models;
using NHLOverlay.Services;
using static NHLOverlay.Tests.TestGames;

namespace NHLOverlay.Tests;

public class ScorePollingServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // 04:00 Eastern on Oct 25: both Oct 24 (last night) and Oct 25 are relevant.
    private static readonly DateTimeOffset Morning = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Yesterday = new(2026, 10, 24);
    private static readonly DateOnly Today = new(2026, 10, 25);

    private readonly FakeNhlApi _api = new();
    private readonly GameStateService _state = new();
    private readonly ManualTimeProvider _time = new(Morning);

    private ScorePollingService CreateService() => new(_api, _state, () => Interval, _time, Zones.Eastern);

    [Fact]
    public async Task Publishes_games_from_all_relevant_dates()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2)];
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(15))];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);

        var snapshot = _state.Current;
        Assert.Equal([1L, 2L], snapshot.Games.Select(g => g.Id).Order());
        Assert.True(snapshot.HasData);
        Assert.False(snapshot.IsStale);
        Assert.Equal(Morning, snapshot.LastSuccessUtc);
    }

    [Fact]
    public async Task Does_not_refetch_a_date_whose_games_are_all_official()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-10), 3, 2)];
        _api.Games[Today] = [Game(2, GameStatus.Scheduled, "FUT", Morning.AddHours(15))];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, _api.Requests.Count(d => d == Yesterday));
        Assert.Equal(3, _api.Requests.Count(d => d == Today));
    }

    [Fact]
    public async Task Keeps_refetching_a_date_with_a_final_that_is_not_yet_official()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "FINAL", Morning.AddHours(-3), 3, 2)];
        using var service = CreateService();

        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, _api.Requests.Count(d => d == Yesterday));
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
            delays.Add(await service.PollOnceAsync(CancellationToken.None));

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
        Assert.Equal(ScorePollingService.IdleInterval, ScorePollingService.NextDelay(games, Morning, Interval));
    }

    [Fact]
    public void Wakes_up_shortly_before_the_next_game()
    {
        var games = new[] { Game(2, GameStatus.Scheduled, "FUT", Morning.AddMinutes(13)) };
        Assert.Equal(TimeSpan.FromMinutes(3), ScorePollingService.NextDelay(games, Morning, Interval));
    }

    [Fact]
    public void Polls_at_the_interval_when_a_game_is_past_its_start_but_not_live() =>
        Assert.Equal(Interval, ScorePollingService.NextDelay([Game(2, GameStatus.Scheduled, "FUT", Morning.AddMinutes(-5))], Morning, Interval));

    [Fact]
    public async Task Keeps_an_unfinished_game_from_yesterday_after_the_cutoff()
    {
        _api.Games[Yesterday] = [Game(1, GameStatus.Overtime, "LIVE", Morning.AddHours(-5), 2, 2)];
        using var service = CreateService();
        await service.PollOnceAsync(CancellationToken.None);

        _time.Now = new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero); // 13:00 Eastern, past the cutoff
        await service.PollOnceAsync(CancellationToken.None);
        Assert.Contains(_state.Current.Games, g => g.Id == 1);

        _api.Games[Yesterday] = [Game(1, GameStatus.Final, "OFF", Morning.AddHours(-5), 3, 2)];
        await service.PollOnceAsync(CancellationToken.None); // fetched as settled...
        await service.PollOnceAsync(CancellationToken.None); // ...then dropped
        Assert.DoesNotContain(_state.Current.Games, g => g.Id == 1);
    }
}
