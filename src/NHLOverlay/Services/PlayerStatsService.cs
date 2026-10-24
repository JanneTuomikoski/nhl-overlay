using System.Text.Json;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Players' career totals and point streaks, fetched on demand one player at a time and cached on disk for
/// the current hockey day (career totals only change after games).
/// </summary>
public sealed class PlayerStatsService : IDisposable
{
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(300);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INhlApiClient _api;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<long, PlayerCareer> _players = [];
    private readonly HashSet<long> _queued = [];
    private readonly Dictionary<long, DateTimeOffset> _failed = [];
    private readonly HashSet<long> _notOnDisk = [];
    private readonly SemaphoreSlim _work = new(0);
    private CancellationTokenSource? _cts;

    public PlayerStatsService(INhlApiClient api, string cacheDirectory, TimeProvider time)
    {
        _api = api;
        _directory = Path.Combine(cacheDirectory, "players");
        _time = time;
    }

    /// <summary>Raised on a background thread after a player's stats were loaded.</summary>
    public event EventHandler? Changed;

    public PlayerCareer? Get(long playerId)
    {
        lock (_gate)
        {
            if (_players.TryGetValue(playerId, out var career)) return career;
        }

        lock (_gate)
        {
            if (_notOnDisk.Contains(playerId)) return null;
        }

        // Not in memory: try the disk cache (cheap, and avoids a request after a restart).
        var cached = LoadCached(playerId);
        lock (_gate)
        {
            if (cached is null) _notOnDisk.Add(playerId);
            else _players[playerId] = cached;
        }

        return cached;
    }

    /// <summary>The player's stats only when fetched during the current hockey day, so last night's games are counted.</summary>
    public PlayerCareer? GetFresh(long playerId) => Get(playerId) is { } career && IsFresh(career, _time.GetUtcNow()) ? career : null;

    private bool IsFresh(PlayerCareer career, DateTimeOffset now) =>
        NhlCalendar.GetHockeyDay(career.FetchedUtc, _time.LocalTimeZone) == NhlCalendar.GetHockeyDay(now, _time.LocalTimeZone);

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    /// <summary>Queues players whose stats are missing or from an earlier hockey day.</summary>
    public void Request(IEnumerable<long> playerIds)
    {
        var now = _time.GetUtcNow();
        var added = 0;
        foreach (var id in playerIds.Distinct())
        {
            var career = Get(id);
            lock (_gate)
            {
                if ((_failed.TryGetValue(id, out var failed) && now - failed < RetryAfterFailure)
                    || (career is not null && IsFresh(career, now))) continue;
                if (_queued.Add(id)) added++;
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
                long id;
                lock (_gate) id = _queued.FirstOrDefault();
                if (id == 0) continue;

                try
                {
                    await FetchAsync(id, ct).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _queued.Remove(id);
                }

                await Task.Delay(RequestGap, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Player stats worker error; continuing.", ex); // never let the loop die
            }
        }
    }

    internal async Task FetchAsync(long playerId, CancellationToken ct)
    {
        try
        {
            var career = await _api.GetPlayerCareerAsync(playerId, ct).ConfigureAwait(false) with { FetchedUtc = _time.GetUtcNow() };
            lock (_gate)
            {
                _players[playerId] = career;
                _failed.Remove(playerId);
            }

            Save(career);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Stats for player {playerId} unavailable: {ex.Message}");
            lock (_gate) _failed[playerId] = _time.GetUtcNow(); // retried after RetryAfterFailure
        }
    }

    private PlayerCareer? LoadCached(long playerId)
    {
        try
        {
            var path = Path.Combine(_directory, $"{playerId}.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<PlayerCareer>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Ignoring unreadable player cache {playerId}.", ex);
            return null;
        }
    }

    private void Save(PlayerCareer career)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{career.PlayerId}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(career, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not cache stats for player {career.PlayerId}.", ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _work.Dispose();
    }
}
