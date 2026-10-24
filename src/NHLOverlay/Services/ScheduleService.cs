using System.Text.Json;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Season schedules for favorite teams (next games) and, during the playoffs, the bracket. Schedules are
/// cached on disk for half a day; the bracket is refreshed every half hour while it is shown.
/// </summary>
public sealed class ScheduleService : IDisposable
{
    internal static readonly TimeSpan ScheduleMaxAge = TimeSpan.FromHours(12);
    internal static readonly TimeSpan BracketMaxAge = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INhlApiClient _api;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedSchedule> _schedules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _queuedTeams = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _work = new(0);
    private (DateTimeOffset Fetched, int Year, IReadOnlyList<PlayoffSeries> Series)? _bracket;
    private int _queuedBracketYear;
    private CancellationTokenSource? _cts;

    private sealed record CachedSchedule(DateTimeOffset SavedUtc, List<HockeyGame> Games);

    public ScheduleService(INhlApiClient api, string cacheDirectory, TimeProvider time)
    {
        _api = api;
        _directory = Path.Combine(cacheDirectory, "schedules");
        _time = time;
    }

    /// <summary>Raised on a background thread when a schedule or the bracket was loaded.</summary>
    public event EventHandler? Changed;

    /// <summary>Increments whenever data changes (cheap change detection for views).</summary>
    public int Version { get; private set; }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    /// <summary>The team's next games (not yet started), soonest first.</summary>
    public IReadOnlyList<HockeyGame> Upcoming(string team, DateTimeOffset now, int count)
    {
        lock (_gate)
        {
            if (!_schedules.TryGetValue(team, out var schedule)) return [];
            return schedule.Games.Where(g => g.StartTimeUtc > now && g.Status.IsUpcoming())
                .OrderBy(g => g.StartTimeUtc).Take(count).ToList();
        }
    }

    public IReadOnlyList<PlayoffSeries> Bracket
    {
        get
        {
            lock (_gate) return _bracket?.Series ?? [];
        }
    }

    /// <summary>Queues missing or stale schedules for <paramref name="teams"/>, and the bracket when it is relevant.</summary>
    public void Ensure(IEnumerable<string> teams, int? bracketYear)
    {
        var now = _time.GetUtcNow();
        var added = 0;
        lock (_gate)
        {
            if (_schedules.Count == 0) LoadCache();
            foreach (var team in teams.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fresh = _schedules.TryGetValue(team, out var schedule) && now - schedule.SavedUtc < ScheduleMaxAge;
                if (!fresh && _queuedTeams.Add(team)) added++;
            }

            if (bracketYear is int year && _queuedBracketYear == 0
                && (_bracket is not { } b || b.Year != year || now - b.Fetched >= BracketMaxAge))
            {
                _queuedBracketYear = year;
                added++;
            }
        }

        if (added > 0) _work.Release(added);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _work.WaitAsync(ct).ConfigureAwait(false);
                string? team;
                int year;
                lock (_gate)
                {
                    team = _queuedTeams.FirstOrDefault();
                    year = team is null ? _queuedBracketYear : 0;
                }

                if (team is not null)
                {
                    await FetchScheduleAsync(team, ct).ConfigureAwait(false);
                    lock (_gate) _queuedTeams.Remove(team);
                }
                else if (year != 0)
                {
                    await FetchBracketAsync(year, ct).ConfigureAwait(false);
                    lock (_gate) _queuedBracketYear = 0;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task FetchScheduleAsync(string team, CancellationToken ct)
    {
        try
        {
            var games = await _api.GetTeamScheduleAsync(team, ct).ConfigureAwait(false);
            var schedule = new CachedSchedule(_time.GetUtcNow(), games.ToList());
            lock (_gate)
            {
                _schedules[team] = schedule;
                Version++;
            }

            Save(team, schedule);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Schedule for {team} unavailable: {ex.Message}");
        }
    }

    internal async Task FetchBracketAsync(int year, CancellationToken ct)
    {
        try
        {
            var series = await _api.GetPlayoffBracketAsync(year, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _bracket = (_time.GetUtcNow(), year, series);
                Version++;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Playoff bracket {year} unavailable: {ex.Message}");
        }
    }

    /// <summary>Called under the lock on first use.</summary>
    private void LoadCache()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var schedule = JsonSerializer.Deserialize<CachedSchedule>(File.ReadAllText(file), JsonOptions);
                if (schedule?.Games is { Count: > 0 }) _schedules[Path.GetFileNameWithoutExtension(file)] = schedule;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Ignoring unreadable schedule cache {file}.", ex);
            }
        }

        Version++;
    }

    private void Save(string team, CachedSchedule schedule)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{team.ToUpperInvariant()}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(schedule, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not cache the {team} schedule.", ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _work.Dispose();
    }
}
