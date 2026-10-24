using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Session state that is not persisted: whether a game has focus, Do Not Disturb, and a temporary
/// interaction override. Combines with the settings into the effective overlay behavior. UI thread only.
/// </summary>
public sealed class RuntimeState
{
    private bool _isGaming;
    private string? _gameProcess;
    private bool _doNotDisturb;
    private bool? _clickThroughOverride;

    public event EventHandler? Changed;

    /// <summary>One of the configured games is in the foreground (and the gaming profile is enabled).</summary>
    public bool IsGaming => _isGaming;

    public string? GameProcess => _gameProcess;

    public bool DoNotDisturb
    {
        get => _doNotDisturb;
        set
        {
            if (_doNotDisturb == value) return;
            _doNotDisturb = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Set by the interaction hotkey while the gaming profile forces click-through, so the user can still
    /// interact mid-game. Cleared whenever gaming starts or stops.
    /// </summary>
    public bool? ClickThroughOverride
    {
        get => _clickThroughOverride;
        set
        {
            if (_clickThroughOverride == value) return;
            _clickThroughOverride = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetGaming(bool isGaming, string? process)
    {
        if (_isGaming == isGaming && _gameProcess == process) return;
        var modeChanged = _isGaming != isGaming;
        _isGaming = isGaming;
        _gameProcess = process;
        if (modeChanged) _clickThroughOverride = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool GamingForcesClickThrough(AppSettings s) => IsGaming && s.Gaming.Enabled && s.Gaming.ClickThrough;

    public bool EffectiveClickThrough(AppSettings s) =>
        ClickThroughOverride ?? (s.Window.ClickThrough || GamingForcesClickThrough(s));

    public DisplayMode EffectiveDisplayMode(AppSettings s) =>
        IsGaming && s.Gaming.Enabled && s.Gaming.DisplayMode == GamingDisplayMode.Compact ? DisplayMode.Compact : s.Appearance.DisplayMode;
}
