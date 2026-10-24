using System.Text.Json;
using NHLOverlay.Helpers;

namespace NHLOverlay.Services;

/// <summary>Small session state that should survive restarts but isn't a setting.</summary>
public sealed class AppState
{
    /// <summary>Games the user marked "watch later" (score hidden), with when they were marked.</summary>
    public Dictionary<long, DateTimeOffset> ShieldedGames { get; set; } = [];

    /// <summary>Games whose score the user chose to reveal despite a shield rule.</summary>
    public Dictionary<long, DateTimeOffset> RevealedGames { get; set; } = [];

    /// <summary>The NHL date whose morning recap the user dismissed.</summary>
    public DateOnly? RecapDismissedFor { get; set; }
}

/// <summary>
/// Persists <see cref="AppState"/> as state.json next to the settings. Entries older than ten days are
/// dropped. UI thread only.
/// </summary>
public sealed class AppStateService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _time;

    public AppStateService(string directory, TimeProvider time)
    {
        _path = Path.Combine(directory, "state.json");
        _time = time;
        State = Load();
    }

    public AppState State { get; }

    public event EventHandler? Changed;

    /// <summary>Applies a change, prunes old entries, saves and notifies.</summary>
    public void Update(Action<AppState> change)
    {
        change(State);
        var cutoff = _time.GetUtcNow() - Retention;
        foreach (var map in new[] { State.ShieldedGames, State.RevealedGames })
            foreach (var id in map.Where(e => e.Value < cutoff).Select(e => e.Key).ToList())
                map.Remove(id);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppState Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(_path), JsonOptions) ?? new AppState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Ignoring unreadable state.json.", ex);
        }

        return new AppState();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(State, JsonOptions));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save state.json.", ex);
        }
    }
}
