using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Background refresh loop. Guarantees:
/// <list type="bullet">
/// <item>One poll at a time (single loop; refresh requests are coalesced into one wake-up).</item>
/// <item>NHL dates whose games are all official/inactive are cached and never re-requested.</item>
/// <item>The last good data is kept and re-published (marked stale) when a request fails.</item>
/// <item>Exponential backoff after failures, honoring Retry-After.</item>
/// <item>Slower polling when nothing is live and the next game is far away (request budget).</item>
/// </list>
/// </summary>
public sealed class ScorePollingService : IDisposable
{
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan PreGameLead = TimeSpan.FromMinutes(10);

    private readonly INhlApiClient _api;
    private readonly GameStateService _state;
    private readonly Func<TimeSpan> _interval;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo? _eastern;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<DateOnly, DateCache> _cache = [];

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _consecutiveFailures;
    private DateTimeOffset? _lastSuccessUtc;
    private string? _lastError;
    private DateTimeOffset? _lastErrorUtc;

    private sealed record DateCache(IReadOnlyList<HockeyGame> Games, bool IsSettled);

    public ScorePollingService(INhlApiClient api, GameStateService state, Func<TimeSpan> interval, TimeProvider time, TimeZoneInfo? eastern = null)
    {
        _api = api;
        _state = state;
        _interval = interval;
        _time = time;
        _eastern = eastern;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    /// <summary>Requests an immediate refresh. Multiple calls before the poll runs collapse into one.</summary>
    public void RequestRefresh()
    {
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Unexpected error while refreshing scores.", ex);
                delay = _interval();
            }

            try
            {
                await _wake.WaitAsync(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Runs one refresh, publishes a snapshot and returns the delay until the next one.</summary>
    internal async Task<TimeSpan> PollOnceAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var dates = SelectDates(now);
        foreach (var obsolete in _cache.Keys.Where(d => !dates.Contains(d)).ToList())
            _cache.Remove(obsolete);

        string? error = null;
        TimeSpan? retryAfter = null;
        foreach (var date in dates)
        {
            if (_cache.TryGetValue(date, out var cached) && cached.IsSettled)
                continue; // Finished games don't change; no request needed.

            try
            {
                var fetched = await _api.GetGamesForDateAsync(date, ct).ConfigureAwait(false);
                _cache[date] = new DateCache(fetched, IsSettled(fetched));
            }
            catch (NhlApiException ex)
            {
                error ??= ex.Message;
                if (ex.RetryAfter is { } ra && (retryAfter is null || ra > retryAfter))
                    retryAfter = ra;
                Log.Warn($"Refreshing NHL date {date:yyyy-MM-dd} failed: {ex.Message}");
            }
        }

        var games = dates.Where(_cache.ContainsKey)
                         .SelectMany(d => _cache[d].Games)
                         .DistinctBy(g => g.Id)
                         .ToList();

        TimeSpan delay;
        if (error is null)
        {
            _consecutiveFailures = 0;
            _lastSuccessUtc = now;
            delay = NextDelay(games, now, _interval());
        }
        else
        {
            _consecutiveFailures++;
            _lastError = error;
            _lastErrorUtc = now;
            delay = Backoff(_interval(), _consecutiveFailures, retryAfter);
        }

        _state.Publish(new ScoreboardSnapshot
        {
            Games = games,
            HasData = _cache.Count > 0,
            IsStale = error is not null,
            LastSuccessUtc = _lastSuccessUtc,
            LastError = _lastError,
            LastErrorUtc = _lastErrorUtc,
            NextRefreshUtc = now + delay,
        });
        return delay;
    }

    /// <summary>
    /// The calendar-relevant NHL dates, plus a recent date that still has unfinished games
    /// (e.g. a long overtime game running past the cutoff), so no in-progress game disappears.
    /// </summary>
    internal List<DateOnly> SelectDates(DateTimeOffset now)
    {
        var dates = NhlCalendar.GetRelevantDates(now, _eastern).ToList();
        var earliest = dates[0].AddDays(-1);
        foreach (var (date, cache) in _cache)
        {
            if (!dates.Contains(date) && date >= earliest && !cache.IsSettled)
                dates.Add(date);
        }

        dates.Sort();
        return dates;
    }

    /// <summary>A date is settled when every game is official (OFF) or postponed/suspended/cancelled.</summary>
    internal static bool IsSettled(IReadOnlyList<HockeyGame> games) =>
        games.Count > 0 && games.All(g => IsOfficial(g) || g.Status.IsInactive());

    private static bool IsOfficial(HockeyGame game) =>
        string.Equals(game.RawGameState, "OFF", StringComparison.OrdinalIgnoreCase);

    internal static TimeSpan NextDelay(IReadOnlyCollection<HockeyGame> games, DateTimeOffset now, TimeSpan interval)
    {
        var active = games.Any(g => g.Status.IsInProgress()
                                    || g.Status is GameStatus.PreGame or GameStatus.Unknown
                                    || (g.Status == GameStatus.Final && !IsOfficial(g)));
        if (active) return interval;

        // Nothing live: sleep until shortly before the next scheduled start, but never longer than
        // the idle interval (so schedule changes are still noticed) or shorter than the user interval.
        var nextStart = games.Where(g => g.Status == GameStatus.Scheduled)
                             .Select(g => (DateTimeOffset?)g.StartTimeUtc)
                             .Min();
        var untilWarmup = nextStart is { } start ? start - PreGameLead - now : IdleInterval;
        if (untilWarmup < interval) return interval;
        return untilWarmup < IdleInterval ? untilWarmup : IdleInterval > interval ? IdleInterval : interval;
    }

    internal static TimeSpan Backoff(TimeSpan interval, int consecutiveFailures, TimeSpan? retryAfter)
    {
        var factor = Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 10));
        var delay = TimeSpan.FromTicks((long)Math.Min(interval.Ticks * factor, MaxBackoff.Ticks));
        if (delay < interval) delay = interval;
        if (retryAfter is { } ra && ra > delay)
            delay = ra < MaxRetryAfter ? ra : MaxRetryAfter;
        return delay;
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Loop faulted or was cancelled while shutting down; nothing to do.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }
}
