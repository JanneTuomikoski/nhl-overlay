using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// On-demand boxscores (goalie lines) for games that need them, e.g. a watched-country goalie or a shutout
/// watch. Official games are fetched once; live games at most every <see cref="LiveRefresh"/>. Requests are
/// processed one at a time in the background.
/// </summary>
public sealed class BoxscoreService : IDisposable
{
    internal static readonly TimeSpan LiveRefresh = TimeSpan.FromMinutes(2);

    private readonly INhlApiClient _api;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<long, (DateTimeOffset Fetched, GameBoxscore Box)> _cache = [];
    private readonly Dictionary<long, HockeyGame> _queued = [];
    private readonly SemaphoreSlim _work = new(0);
    private CancellationTokenSource? _cts;

    public BoxscoreService(INhlApiClient api, TimeProvider time)
    {
        _api = api;
        _time = time;
    }

    /// <summary>Raised on a background thread after a boxscore was loaded.</summary>
    public event EventHandler? Changed;

    public GameBoxscore? Get(long gameId)
    {
        lock (_gate) return _cache.TryGetValue(gameId, out var entry) ? entry.Box : null;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    /// <summary>Asks for a game's boxscore if it is missing or stale. Scheduled games are ignored.</summary>
    public void Request(HockeyGame game)
    {
        if (game.Status.IsUpcoming() || game.Status.IsInactive()) return;
        lock (_gate)
        {
            if (_cache.TryGetValue(game.Id, out var entry)
                && (entry.Box.IsOfficial || _time.GetUtcNow() - entry.Fetched < LiveRefresh))
                return;
            if (!_queued.TryAdd(game.Id, game)) return;
        }

        _work.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _work.WaitAsync(ct).ConfigureAwait(false);
                HockeyGame? game;
                lock (_gate) game = _queued.Values.FirstOrDefault();
                if (game is null) continue;

                try
                {
                    await FetchAsync(game, ct).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _queued.Remove(game.Id);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Boxscore worker error; continuing.", ex); // never let the loop die
            }
        }
    }

    internal async Task FetchAsync(HockeyGame game, CancellationToken ct)
    {
        try
        {
            var box = await _api.GetBoxscoreAsync(game.Id, game.Away.Abbrev, game.Home.Abbrev, ct).ConfigureAwait(false);
            lock (_gate) _cache[game.Id] = (_time.GetUtcNow(), box);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
        {
            Log.Warn($"Boxscore for game {game.Id} unavailable: {ex.Message}");
            // Remember the attempt so a failing game isn't retried in a tight loop.
            lock (_gate) _cache.TryAdd(game.Id, (_time.GetUtcNow(), new GameBoxscore(game.Id, [], false)));
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _work.Dispose();
    }
}
