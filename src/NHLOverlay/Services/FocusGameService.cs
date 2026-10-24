using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Polls the play-by-play of the one game shown in the focus dashboard, at the user's refresh interval and only
/// while a game is focused. A finished game is fetched once more and then left alone.
/// </summary>
public sealed class FocusGameService : IDisposable
{
    private readonly INhlApiClient _api;
    private readonly Func<TimeSpan> _interval;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Lock _gate = new();
    private long _gameId;
    private PlayByPlay? _latest;
    private CancellationTokenSource? _cts;

    public FocusGameService(INhlApiClient api, Func<TimeSpan> interval)
    {
        _api = api;
        _interval = interval;
    }

    /// <summary>Raised on a background thread with each new play-by-play for the focused game.</summary>
    public event EventHandler<PlayByPlay>? Updated;

    public long? GameId
    {
        get
        {
            lock (_gate) return _gameId == 0 ? null : _gameId;
        }
    }

    public PlayByPlay? Latest
    {
        get
        {
            lock (_gate) return _latest?.GameId == _gameId ? _latest : null;
        }
    }

    public string? LastError { get; private set; }

    public void Focus(long gameId)
    {
        lock (_gate)
        {
            if (_gameId == gameId) return;
            _gameId = gameId;
        }

        if (_cts is null)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(token));
        }
        else
        {
            Wake();
        }
    }

    public void Stop()
    {
        lock (_gate) _gameId = 0;
        Wake();
    }

    private void Wake()
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
            var gameId = GameId;
            var finished = false;
            if (gameId is { } id)
            {
                try
                {
                    var pbp = await _api.GetPlayByPlayAsync(id, ct).ConfigureAwait(false);
                    lock (_gate) _latest = pbp;
                    LastError = null;
                    finished = pbp.Plays.Any(p => p.Type == PlayType.GameEnd);
                    if (GameId == id) Updated?.Invoke(this, pbp);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is NhlApiException or NotSupportedException)
                {
                    LastError = ex.Message;
                    Log.Warn($"Play-by-play for game {id} failed: {ex.Message}");
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Log.Error($"Play-by-play worker error for game {id}; continuing.", ex); // never let the loop die
                }
            }

            try
            {
                // Idle (no game) or finished: wait until another game is focused.
                if (gameId is null || finished) await _wake.WaitAsync(ct).ConfigureAwait(false);
                else await _wake.WaitAsync(_interval(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _wake.Dispose();
    }
}
