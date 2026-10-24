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
/// <item>Each date (per league) is refreshed on its own schedule: at the user interval while a game is
/// live, otherwise not until shortly before its next game (checked hourly for schedule changes), and
/// only every few hours when it has no games at all. The loop sleeps until the earliest due date or the
/// morning rollover.</item>
/// </list>
/// </summary>
public sealed class ScorePollingService : IDisposable
{
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan IdleRecheck = TimeSpan.FromHours(1);
    internal static readonly TimeSpan NoGamesRecheck = TimeSpan.FromHours(6);
    internal static readonly TimeSpan PreGameLead = TimeSpan.FromMinutes(10);

    /// <summary>Longest single wait, so a long sleep still notices the wall clock jumping (PC sleep/resume).</summary>
    private static readonly TimeSpan MaxWaitSlice = TimeSpan.FromMinutes(1);

    private readonly INhlApiClient _api;
    private readonly GameStateService _state;
    private readonly Func<TimeSpan> _interval;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _localZone;
    private readonly TimeZoneInfo? _eastern;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<DateOnly, DateCache> _cache = [];
    private readonly Dictionary<DateOnly, DateCache> _liigaCache = [];
    private readonly ILiigaClient? _liiga;
    private readonly Func<bool> _liigaEnabled;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _consecutiveFailures;
    private int _liigaFailures;
    private bool _lastFetchFailed;
    private int _forceRefresh; // 1 = refetch every unsettled date on the next poll
    private DateTimeOffset? _lastSuccessUtc;
    private string? _lastError;
    private DateTimeOffset? _lastErrorUtc;
    private int _viewedDayNumber; // DateOnly.DayNumber of an extra date the user is viewing; 0 = none

    private sealed record DateCache(IReadOnlyList<HockeyGame> Games, bool IsSettled);

    private readonly Dictionary<DateOnly, DateTimeOffset> _due = [];
    private readonly Dictionary<DateOnly, DateTimeOffset> _liigaDue = [];

    public ScorePollingService(
        INhlApiClient api,
        GameStateService state,
        Func<TimeSpan> interval,
        TimeProvider time,
        TimeZoneInfo? localZone = null,
        TimeZoneInfo? eastern = null,
        ILiigaClient? liiga = null,
        Func<bool>? liigaEnabled = null)
    {
        _api = api;
        _state = state;
        _interval = interval;
        _time = time;
        _localZone = localZone ?? TimeZoneInfo.Local;
        _eastern = eastern;
        _liiga = liiga;
        _liigaEnabled = liigaEnabled ?? (() => false);
    }

    /// <summary>
    /// Also load <paramref name="date"/> (e.g. when the user browses back a few days). Null clears it.
    /// Yesterday and today are always loaded, so this is only needed for other dates.
    /// </summary>
    public void SetViewedDate(DateOnly? date)
    {
        var dayNumber = date?.DayNumber ?? 0;
        if (Interlocked.Exchange(ref _viewedDayNumber, dayNumber) != dayNumber && date is not null)
            Nudge();
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    /// <summary>Requests an immediate refresh of every unsettled date. Multiple calls before the poll runs collapse into one.</summary>
    public void RequestRefresh()
    {
        Volatile.Write(ref _forceRefresh, 1);
        Nudge();
    }

    /// <summary>Wakes the loop to fetch only what is missing or due (e.g. a newly viewed date, Liiga switched on).</summary>
    public void Nudge()
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
                var dueUtc = _time.GetUtcNow() + delay;
                while (_time.GetUtcNow() < dueUtc)
                {
                    var left = dueUtc - _time.GetUtcNow();
                    if (await _wake.WaitAsync(left < MaxWaitSlice ? left : MaxWaitSlice, ct).ConfigureAwait(false)) break;
                }
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
        var today = NhlCalendar.GetTodayNhlDate(now, _localZone, _eastern);
        var dates = SelectDates(today);
        var force = Interlocked.Exchange(ref _forceRefresh, 0) == 1;
        foreach (var obsolete in _cache.Keys.Where(d => !dates.Contains(d)).ToList())
            _cache.Remove(obsolete);
        foreach (var obsolete in _due.Keys.Where(d => !dates.Contains(d)).ToList())
            _due.Remove(obsolete);

        string? error = null;
        TimeSpan? retryAfter = null;
        var failed = new List<DateOnly>();
        var fetchedAny = false;
        foreach (var date in dates)
        {
            if (_cache.TryGetValue(date, out var cached) && cached.IsSettled)
                continue; // Finished games don't change; no request needed.
            if (!force && _due.TryGetValue(date, out var due) && now < due)
                continue; // Not due yet (nothing live, next game far away).

            fetchedAny = true;
            try
            {
                var fetched = await _api.GetGamesForDateAsync(date, ct).ConfigureAwait(false);
                _cache[date] = new DateCache(fetched, IsSettled(fetched));
                _due[date] = now + NextDelay(fetched, now, _interval());
            }
            catch (NhlApiException ex)
            {
                error ??= ex.Message;
                if (ex.RetryAfter is { } ra && (retryAfter is null || ra > retryAfter))
                    retryAfter = ra;
                failed.Add(date);
                Log.Warn($"Refreshing NHL date {date:yyyy-MM-dd} failed: {ex.Message}");
            }
        }

        if (fetchedAny)
        {
            if (error is null)
            {
                _consecutiveFailures = 0;
                _lastSuccessUtc = now;
            }
            else
            {
                _consecutiveFailures++;
                _lastError = error;
                _lastErrorUtc = now;
                var retry = now + Backoff(_interval(), _consecutiveFailures, retryAfter);
                foreach (var date in failed) _due[date] = retry;
            }

            _lastFetchFailed = error is not null;
        }

        await PollLiigaAsync(dates, now, force, ct).ConfigureAwait(false);

        var byDate = dates.Where(d => _cache.ContainsKey(d) || _liigaCache.ContainsKey(d)).ToDictionary(
            d => d,
            d => (IReadOnlyList<HockeyGame>)(_cache.TryGetValue(d, out var nhl) ? nhl.Games : [])
                .Concat(_liigaCache.TryGetValue(d, out var liiga) ? liiga.Games : [])
                .ToList());
        var todayView = BuildTodayView(today, byDate);

        // Sleep until the earliest due date, or the morning rollover when a new slate becomes "today".
        var wake = NhlCalendar.GetNextHockeyDayStart(now, _localZone);
        foreach (var due in _due.Values.Concat(_liigaDue.Values))
            if (due < wake) wake = due;
        var delay = wake - now;
        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);

        _state.Publish(new ScoreboardSnapshot
        {
            TodayNhlDate = today,
            Games = todayView,
            GamesByDate = byDate,
            HasData = byDate.ContainsKey(today),
            IsStale = _lastFetchFailed,
            LastSuccessUtc = _lastSuccessUtc,
            LastError = _lastError,
            LastErrorUtc = _lastErrorUtc,
            NextRefreshUtc = now + delay,
        });
        return delay;
    }

    /// <summary>
    /// Liiga games for the same dates (Liiga plays in Finland, so its calendar date is the hockey day). A Liiga
    /// failure is logged but never marks the NHL data stale or slows NHL polling down.
    /// </summary>
    private async Task PollLiigaAsync(IReadOnlyList<DateOnly> dates, DateTimeOffset now, bool force, CancellationToken ct)
    {
        if (_liiga is null || !_liigaEnabled())
        {
            _liigaCache.Clear();
            _liigaDue.Clear();
            return;
        }

        foreach (var obsolete in _liigaCache.Keys.Where(d => !dates.Contains(d)).ToList())
            _liigaCache.Remove(obsolete);
        foreach (var obsolete in _liigaDue.Keys.Where(d => !dates.Contains(d)).ToList())
            _liigaDue.Remove(obsolete);

        foreach (var date in dates)
        {
            if (_liigaCache.TryGetValue(date, out var cached) && cached.IsSettled) continue;
            if (!force && _liigaDue.TryGetValue(date, out var due) && now < due) continue;
            try
            {
                var games = await _liiga.GetGamesForDateAsync(date, ct).ConfigureAwait(false);
                _liigaCache[date] = new DateCache(games, IsSettled(games));
                _liigaDue[date] = now + NextDelay(games, now, _interval());
                _liigaFailures = 0;
                continue;
            }
            catch (NhlApiException ex)
            {
                Log.Warn($"Liiga {date:yyyy-MM-dd} failed: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Unexpected error reading Liiga {date:yyyy-MM-dd}.", ex);
            }

            _liigaDue[date] = now + Backoff(_interval(), ++_liigaFailures, null);
        }
    }

    /// <summary>Yesterday and today (NHL dates), plus the date the user is currently viewing, if any.</summary>
    internal List<DateOnly> SelectDates(DateOnly today)
    {
        var dates = new List<DateOnly> { today.AddDays(-1), today };
        var viewed = Volatile.Read(ref _viewedDayNumber);
        if (viewed != 0 && !dates.Contains(DateOnly.FromDayNumber(viewed)))
            dates.Add(DateOnly.FromDayNumber(viewed));
        dates.Sort();
        return dates;
    }

    /// <summary>
    /// Today's games plus yesterday's games that are still being played or not yet official, so a late
    /// game stays on screen through the morning rollover instead of vanishing mid-game.
    /// </summary>
    internal static IReadOnlyList<HockeyGame> BuildTodayView(DateOnly today, IReadOnlyDictionary<DateOnly, IReadOnlyList<HockeyGame>> byDate)
    {
        var view = new List<HockeyGame>();
        if (byDate.TryGetValue(today.AddDays(-1), out var yesterday))
            view.AddRange(yesterday.Where(g => g.Status.IsInProgress() || (g.Status.IsFinal() && !IsOfficial(g))));
        if (byDate.TryGetValue(today, out var games))
            view.AddRange(games);
        return view.DistinctBy(g => g.Id).ToList();
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

        // Nothing live: sleep until shortly before the next scheduled start, but at most an hour (so
        // schedule changes are still noticed), and never shorter than the user interval. A date with no
        // upcoming games (an empty day, the off-season) is only checked every few hours.
        var nextStart = games.Where(g => g.Status == GameStatus.Scheduled)
                             .Select(g => (DateTimeOffset?)g.StartTimeUtc)
                             .Min();
        if (nextStart is not { } start) return NoGamesRecheck;
        var untilWarmup = start - PreGameLead - now;
        if (untilWarmup < interval) return interval;
        return untilWarmup < IdleRecheck ? untilWarmup : IdleRecheck;
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
