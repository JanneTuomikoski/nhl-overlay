using System.Diagnostics;
using System.Windows.Input;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>Gaming profile: which games count, and how the overlay and alerts behave while one has focus.</summary>
public sealed class GamingSectionViewModel : ObservableObject, ISettingsSection
{
    private bool _enabled;
    private string _gameProcesses = "";
    private GamingDisplayMode _displayMode;
    private bool _clickThrough;
    private bool _desktopNotifications;
    private bool _sounds;
    private bool _speech;
    private IReadOnlyList<string> _runningApps = [];

    public GamingSectionViewModel()
    {
        RefreshAppsCommand = new RelayCommand(RefreshRunningApps);
        RefreshRunningApps();
    }

    public ICommand RefreshAppsCommand { get; }

    public IReadOnlyList<ChoiceOption<GamingDisplayMode>> DisplayModeOptions { get; } =
    [
        new(GamingDisplayMode.Compact, "Switch to compact"),
        new(GamingDisplayMode.Unchanged, "Keep current view"),
    ];

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    /// <summary>Comma-separated process names, e.g. "Wow, WowClassic".</summary>
    public string GameProcesses { get => _gameProcesses; set => SetProperty(ref _gameProcesses, value); }

    /// <summary>Applications with a visible window right now; clicking one adds it to the list.</summary>
    public IReadOnlyList<string> RunningApps { get => _runningApps; private set => SetProperty(ref _runningApps, value); }

    /// <summary>Selecting a running app appends it to <see cref="GameProcesses"/>.</summary>
    public string? SelectedRunningApp
    {
        get => null;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            var list = ParseList(GameProcesses);
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
            GameProcesses = string.Join(", ", list);
            OnPropertyChanged();
        }
    }

    public GamingDisplayMode DisplayMode { get => _displayMode; set => SetProperty(ref _displayMode, value); }

    public bool ClickThrough { get => _clickThrough; set => SetProperty(ref _clickThrough, value); }

    public bool DesktopNotifications { get => _desktopNotifications; set => SetProperty(ref _desktopNotifications, value); }

    public bool Sounds { get => _sounds; set => SetProperty(ref _sounds, value); }

    public bool Speech { get => _speech; set => SetProperty(ref _speech, value); }

    public void Load(AppSettings settings)
    {
        var g = settings.Gaming;
        Enabled = g.Enabled;
        GameProcesses = string.Join(", ", g.GameProcesses);
        DisplayMode = g.DisplayMode;
        ClickThrough = g.ClickThrough;
        DesktopNotifications = g.DesktopNotifications;
        Sounds = g.Sounds;
        Speech = g.Speech;
    }

    public void Apply(AppSettings settings)
    {
        var g = settings.Gaming;
        g.Enabled = Enabled;
        g.GameProcesses = ParseList(GameProcesses);
        g.DisplayMode = DisplayMode;
        g.ClickThrough = ClickThrough;
        g.DesktopNotifications = DesktopNotifications;
        g.Sounds = Sounds;
        g.Speech = Speech;
    }

    internal static List<string> ParseList(string text) =>
        text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void RefreshRunningApps()
    {
        var self = Environment.ProcessId;
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != self && process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        names.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    // The process exited while we were looking at it.
                }
            }
        }

        RunningApps = names
            .Where(n => !n.Equals("explorer", StringComparison.OrdinalIgnoreCase) && !n.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
