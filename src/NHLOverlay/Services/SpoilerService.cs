using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Spoiler shield: decides which games hide their score. A game is shielded when it was marked "watch later"
/// or involves an always-shielded team, unless the user revealed it. UI thread only.
/// </summary>
public sealed class SpoilerService
{
    private readonly AppStateService _state;
    private readonly SettingsService _settings;

    public SpoilerService(AppStateService state, SettingsService settings)
    {
        _state = state;
        _settings = settings;
        state.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public bool IsShielded(HockeyGame game)
    {
        var spoilers = _settings.Current.Spoilers;
        if (!spoilers.Enabled || _state.State.RevealedGames.ContainsKey(game.Id)) return false;
        return _state.State.ShieldedGames.ContainsKey(game.Id)
               || spoilers.AlwaysShieldTeams.Contains(game.Away.Abbrev)
               || spoilers.AlwaysShieldTeams.Contains(game.Home.Abbrev);
    }

    /// <summary>Marks a game "watch later": its score stays hidden until revealed.</summary>
    public void Shield(long gameId) => _state.Update(s =>
    {
        s.RevealedGames.Remove(gameId);
        s.ShieldedGames[gameId] = DateTimeOffset.UtcNow;
    });

    public void Reveal(long gameId) => _state.Update(s =>
    {
        s.ShieldedGames.Remove(gameId);
        s.RevealedGames[gameId] = DateTimeOffset.UtcNow;
    });
}
