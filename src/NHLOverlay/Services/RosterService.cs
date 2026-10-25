using System.Globalization;
using System.Text.Json;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Current team rosters (with each player's birth country) for the teams in the loaded games. Each roster is
/// cached on disk for a day and fetched one team at a time in the background, so a normal night costs at most
/// one request per team per day.
/// </summary>
public sealed class RosterService : IDisposable
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(20);
    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(400);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INhlApiClient _api;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedRoster> _rosters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, RosterPlayer> _players = [];
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _work = new(0);
    private IReadOnlyDictionary<long, string> _countryOverrides = new Dictionary<long, string>();
    private CancellationTokenSource? _cts;

    private sealed record CachedRoster(DateTimeOffset SavedUtc, List<RosterPlayer> Players);

    public RosterService(INhlApiClient api, string cacheDirectory, TimeProvider time)
    {
        _api = api;
        _directory = Path.Combine(cacheDirectory, "rosters");
        _time = time;
    }

    /// <summary>Raised on a background thread after a roster was loaded or refreshed.</summary>
    public event EventHandler? Changed;

    /// <summary>A player with their country (a manual override when there is one, else the birth country).</summary>
    public RosterPlayer? Find(long playerId)
    {
        lock (_gate) return _players.TryGetValue(playerId, out var player) ? WithCountry(player) : null;
    }

    public IReadOnlyList<RosterPlayer> Team(string abbrev)
    {
        lock (_gate)
        {
            if (!_rosters.TryGetValue(abbrev, out var roster)) return [];
            return _countryOverrides.Count == 0 ? roster.Players : roster.Players.Select(WithCountry).ToList();
        }
    }

    /// <summary>Players whose country differs from their birth country (from settings).</summary>
    public void SetCountryOverrides(IEnumerable<CountryOverride> overrides)
    {
        var map = overrides.ToDictionary(o => o.PlayerId, o => o.Country);
        lock (_gate)
        {
            if (map.Count == _countryOverrides.Count && map.All(kv => _countryOverrides.GetValueOrDefault(kv.Key) == kv.Value)) return;
            _countryOverrides = map;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loaded players whose name contains <paramref name="text"/> (ignoring case and accents), with their birth country.</summary>
    public IReadOnlyList<RosterPlayer> Search(string text, int max)
    {
        text = text.Trim();
        if (text.Length < 2) return [];
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        lock (_gate)
        {
            return _players.Values
                .Where(p => compare.IndexOf(p.FullName, text, options) >= 0)
                .OrderBy(p => compare.IsPrefix(p.LastName, text, options) ? 0 : 1)
                .ThenBy(p => p.LastName, StringComparer.CurrentCulture)
                .Take(max)
                .ToList();
        }
    }

    /// <summary>Queues the rosters of every NHL team (e.g. so a player search in settings finds anyone).</summary>
    public void EnsureAll() => Ensure(NhlTeams.BuiltIn.Select(t => t.Abbrev));

    /// <summary>Called under the lock.</summary>
    private RosterPlayer WithCountry(RosterPlayer player) =>
        _countryOverrides.TryGetValue(player.Id, out var country) && country != player.Country ? player with { Country = country } : player;

    public void Start(GameStateService state)
    {
        if (_cts is not null) return;
        LoadCache();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
        state.Changed += (_, snapshot) => Ensure(snapshot.GamesByDate.Values.SelectMany(g => g).Concat(snapshot.Games)
            .Where(g => g.IsNhl)
            .SelectMany(g => new[] { g.Away.Abbrev, g.Home.Abbrev }));
        Ensure(state.Current.Games.Where(g => g.IsNhl).SelectMany(g => new[] { g.Away.Abbrev, g.Home.Abbrev }));
    }

    /// <summary>Queues every team whose roster is missing or older than <see cref="MaxAge"/>.</summary>
    public void Ensure(IEnumerable<string> teams)
    {
        var now = _time.GetUtcNow();
        var added = 0;
        lock (_gate)
        {
            foreach (var team in teams.Where(t => t.Length is >= 2 and <= 4).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fresh = _rosters.TryGetValue(team, out var roster) && now - roster.SavedUtc < MaxAge;
                if (!fresh && _queued.Add(team)) added++;
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
                lock (_gate) team = _queued.FirstOrDefault();
                if (team is null) continue;

                try
                {
                    await FetchAsync(team, ct).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _queued.Remove(team);
                }

                await Task.Delay(RequestGap, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Roster worker error; continuing.", ex); // never let the loop die
            }
        }
    }

    internal async Task FetchAsync(string team, CancellationToken ct)
    {
        try
        {
            var players = await _api.GetRosterAsync(team, ct).ConfigureAwait(false);
            if (players.Count == 0) return;
            var roster = new CachedRoster(_time.GetUtcNow(), players.ToList());
            Store(team, roster);
            Save(team, roster);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Roster for {team} unavailable: {ex.Message}");
        }
    }

    private void Store(string team, CachedRoster roster)
    {
        lock (_gate)
        {
            _rosters[team] = roster;
            foreach (var player in roster.Players) _players[player.Id] = player;
        }
    }

    private void LoadCache()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var roster = JsonSerializer.Deserialize<CachedRoster>(File.ReadAllText(file), JsonOptions);
                if (roster?.Players is { Count: > 0 }) Store(Path.GetFileNameWithoutExtension(file), roster);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Ignoring unreadable roster cache {file}.", ex);
            }
        }
    }

    private void Save(string team, CachedRoster roster)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{team.ToUpperInvariant()}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(roster, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not cache the {team} roster.", ex);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _work.Dispose();
    }
}
