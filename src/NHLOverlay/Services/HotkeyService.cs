using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Keeps the global hotkeys registered according to the settings and reports combinations that another
/// application already owns. All members must be used on the UI thread.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly GlobalHotkeys _hotkeys;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleInteraction;
    private HotkeySettings _applied;
    private bool _suspended;

    public HotkeyService(IntPtr hwnd, SettingsService settings, Action toggleOverlay, Action toggleInteraction)
    {
        _hotkeys = new GlobalHotkeys(hwnd);
        _toggleOverlay = toggleOverlay;
        _toggleInteraction = toggleInteraction;
        _applied = settings.Current.Hotkeys;
        settings.Changed += (_, s) =>
        {
            if (s.Hotkeys.ToggleOverlay == _applied.ToggleOverlay && s.Hotkeys.ToggleInteraction == _applied.ToggleInteraction)
                return;
            _applied = s.Hotkeys;
            Apply();
        };
        Apply();
    }

    /// <summary>Human-readable registration problems, e.g. a combination used by another application.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    public event EventHandler? ProblemsChanged;

    /// <summary>Temporarily releases the hotkeys (e.g. while the user records new ones in the settings window).</summary>
    public void Suspend(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        Apply();
    }

    private void Apply()
    {
        _hotkeys.UnregisterAll();
        var problems = new List<string>();
        TryRegister("Show/hide overlay", _applied.ToggleOverlay, _toggleOverlay, problems);
        TryRegister("Toggle interaction mode", _applied.ToggleInteraction, _toggleInteraction, problems);
        // While suspended the registration above is only a probe, so conflicts are still reported.
        if (_suspended) _hotkeys.UnregisterAll();

        Problems = problems;
        foreach (var problem in problems) Log.Warn(problem);
        ProblemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryRegister(string name, string text, Action action, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!HotkeyGesture.TryParse(text, out var gesture))
            problems.Add($"{name}: \"{text}\" is not a valid hotkey.");
        else if (!_hotkeys.Register(gesture, action))
            problems.Add($"{name}: {gesture} is already in use by another application. Choose a different combination.");
    }

    public void Dispose() => _hotkeys.Dispose();
}
