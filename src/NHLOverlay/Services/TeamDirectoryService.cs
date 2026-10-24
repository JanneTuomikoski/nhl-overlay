using System.Text.Json;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// All NHL teams with names, logos and standings. Starts from the built-in team list, then the disk cache,
/// then live <c>/standings/now</c> data, refreshed shortly after a game goes final (and every few hours as a fallback).
/// </summary>
public sealed class TeamDirectoryService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan AfterFinalDelay = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INhlApiClient _api;
    private readonly string _cachePath;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly HashSet<long> _knownFinals = [];
    private readonly Lock _gate = new();
    private IReadOnlyList<TeamStanding> _teams = NhlTeams.BuiltIn;
    private Dictionary<string, TeamStanding> _byAbbrev = NhlTeams.BuiltIn.ToDictionary(t => t.Abbrev, StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _updatedUtc;
    private bool _seenFirstSnapshot;
    private CancellationTokenSource? _cts;

    private sealed record CacheFile(DateTimeOffset SavedUtc, List<TeamStanding> Teams);

    public TeamDirectoryService(INhlApiClient api, string cacheDirectory, TimeProvider time)
    {
        _api = api;
        _cachePath = Path.Combine(cacheDirectory, "standings.json");
        _time = time;
    }

    /// <summary>Raised on a background thread whenever team data changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<TeamStanding> Teams
    {
        get
        {
            lock (_gate) return _teams;
        }
    }

    /// <summary>When standings were last fetched (possibly in an earlier session). Null = built-in list only.</summary>
    public DateTimeOffset? UpdatedUtc
    {
        get
        {
            lock (_gate) return _updatedUtc;
        }
    }

    public TeamStanding? Find(string? abbrev)
    {
        if (string.IsNullOrEmpty(abbrev)) return null;
        lock (_gate) return _byAbbrev.GetValueOrDefault(abbrev);
    }

    public void Start(GameStateService state)
    {
        if (_cts is not null) return;
        LoadCache();
        state.Changed += (_, snapshot) => OnScoreboard(snapshot);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    public void RequestRefresh()
    {
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var age = _time.GetUtcNow() - (UpdatedUtc ?? DateTimeOffset.MinValue);
                if (age >= TimeSpan.FromHours(1))
                    await RefreshAsync(ct).ConfigureAwait(false);

                await _wake.WaitAsync(RefreshInterval, ct).ConfigureAwait(false);
                // A wake-up means "refresh now" regardless of age.
                await RefreshAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Standings worker error; continuing.", ex); // never let the loop die
                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            }
        }
    }

    internal async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var standings = await _api.GetStandingsAsync(ct).ConfigureAwait(false);
            if (standings.Count == 0) return; // off-season: keep what we have
            Apply(standings, _time.GetUtcNow());
            SaveCache();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Standings refresh failed: {ex.Message}");
        }
    }

    private void Apply(IReadOnlyList<TeamStanding> standings, DateTimeOffset updatedUtc)
    {
        // Keep built-in entries for any team missing from the response (e.g. partial data).
        var merged = standings.Concat(NhlTeams.BuiltIn.Where(b => standings.All(s => !s.Abbrev.Equals(b.Abbrev, StringComparison.OrdinalIgnoreCase))))
                              .ToList();
        lock (_gate)
        {
            _teams = merged;
            _byAbbrev = merged.ToDictionary(t => t.Abbrev, StringComparer.OrdinalIgnoreCase);
            _updatedUtc = updatedUtc;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Standings change when games end, so refresh a few minutes after a new final appears.</summary>
    private void OnScoreboard(ScoreboardSnapshot snapshot)
    {
        if (!snapshot.HasData) return;
        var newFinal = false;
        lock (_gate)
        {
            // The first snapshot only records a baseline; finals that already existed are not "new".
            var baseline = !_seenFirstSnapshot;
            foreach (var game in snapshot.Games.Where(g => g.IsNhl && g.Status.IsFinal()))
                newFinal |= _knownFinals.Add(game.Id) && !baseline;
            _seenFirstSnapshot = true;
        }

        if (newFinal)
            _ = Task.Delay(AfterFinalDelay).ContinueWith(_ => RequestRefresh(), TaskScheduler.Default);
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath), JsonOptions);
            if (cache?.Teams is { Count: > 0 }) Apply(cache.Teams, cache.SavedUtc);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Ignoring unreadable standings cache.", ex);
        }
    }

    private void SaveCache()
    {
        try
        {
            CacheFile cache;
            lock (_gate) cache = new CacheFile(_updatedUtc ?? _time.GetUtcNow(), _teams.ToList());
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath + ".tmp", JsonSerializer.Serialize(cache, JsonOptions));
            File.Move(_cachePath + ".tmp", _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not write the standings cache.", ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _wake.Dispose();
    }
}
