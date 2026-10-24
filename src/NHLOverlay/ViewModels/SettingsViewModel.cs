using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels.Settings;

namespace NHLOverlay.ViewModels;

/// <summary>
/// Editable copy of the settings. Nothing is applied until <see cref="SaveCommand"/>; saving only writes
/// the fields this window owns, so it never overwrites state changed elsewhere (e.g. overlay position).
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly GameStateService _state;
    private readonly HotkeyService _hotkeys;
    private readonly Dispatcher _dispatcher;
    private readonly EventHandler<ScoreboardSnapshot> _stateHandler;
    private readonly EventHandler _hotkeyProblemsHandler;
    private readonly IReadOnlyList<ISettingsSection> _sections;

    private bool _launchWithWindows;
    private bool _showOverlayOnStartup;
    private int _refreshIntervalSeconds;
    private DisplayMode _displayMode;
    private double _backgroundOpacityPercent;
    private double _uiScalePercent;
    private bool _showTeamLogos;
    private bool _showGameClock;
    private bool _showShotsOnGoal;
    private bool _alwaysOnTop;
    private bool _clickThrough;
    private bool _lockPosition;
    private bool _snapToEdges;
    private string _toggleOverlayHotkey = "";
    private string _toggleInteractionHotkey = "";
    private string _toggleDoNotDisturbHotkey = "";
    private string _hotkeyProblems = "";
    private string _apiStatus = "";
    private string _lastSuccessText = "";
    private string _lastErrorText = "";

    public SettingsViewModel(
        SettingsService settings,
        GameStateService state,
        HotkeyService hotkeys,
        TeamDirectoryService teams,
        LogoService logos,
        NotificationService notifications,
        SpeechService speech,
        Action refreshNow,
        Dispatcher dispatcher)
    {
        _settings = settings;
        _state = state;
        _hotkeys = hotkeys;
        _dispatcher = dispatcher;
        Teams = new TeamsSectionViewModel(teams, logos);
        HatTrick = new HatTrickSectionViewModel();
        Notifications = new NotificationsSectionViewModel(notifications, speech);
        Gaming = new GamingSectionViewModel();
        Players = new PlayersSectionViewModel();
        Recap = new RecapSectionViewModel(teams, logos);
        _sections = [Teams, HatTrick, Notifications, Gaming, Players, Recap];

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        RefreshNowCommand = new RelayCommand(refreshNow);
        ResetCommand = new RelayCommand(Reset);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);

        LoadFrom(settings.Current);
        _stateHandler = (_, snapshot) => _dispatcher.InvokeAsync(() => UpdateStatus(snapshot));
        _state.Changed += _stateHandler;
        UpdateStatus(state.Current);
        _hotkeyProblemsHandler = (_, _) => HotkeyProblems = string.Join(Environment.NewLine, _hotkeys.Problems);
        _hotkeys.ProblemsChanged += _hotkeyProblemsHandler;
        _hotkeyProblemsHandler(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshNowCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }

    public TeamsSectionViewModel Teams { get; }

    public HatTrickSectionViewModel HatTrick { get; }

    public NotificationsSectionViewModel Notifications { get; }

    public GamingSectionViewModel Gaming { get; }

    public PlayersSectionViewModel Players { get; }

    public RecapSectionViewModel Recap { get; }

    // General
    public IReadOnlyList<int> RefreshIntervals { get; } = GeneralSettings.AllowedRefreshIntervals;

    public bool LaunchWithWindows { get => _launchWithWindows; set => SetProperty(ref _launchWithWindows, value); }

    public bool ShowOverlayOnStartup { get => _showOverlayOnStartup; set => SetProperty(ref _showOverlayOnStartup, value); }

    public int RefreshIntervalSeconds { get => _refreshIntervalSeconds; set => SetProperty(ref _refreshIntervalSeconds, value); }

    // Appearance
    public IReadOnlyList<DisplayMode> DisplayModes { get; } = Enum.GetValues<DisplayMode>();

    public DisplayMode DisplayMode { get => _displayMode; set => SetProperty(ref _displayMode, value); }

    public double BackgroundOpacityPercent { get => _backgroundOpacityPercent; set => SetProperty(ref _backgroundOpacityPercent, Math.Round(value)); }

    public double UiScalePercent { get => _uiScalePercent; set => SetProperty(ref _uiScalePercent, Math.Round(value / 5) * 5); }

    public double MinUiScalePercent => AppearanceSettings.MinUiScale * 100;

    public double MaxUiScalePercent => AppearanceSettings.MaxUiScale * 100;

    public bool ShowTeamLogos { get => _showTeamLogos; set => SetProperty(ref _showTeamLogos, value); }

    public bool ShowGameClock { get => _showGameClock; set => SetProperty(ref _showGameClock, value); }

    public bool ShowShotsOnGoal { get => _showShotsOnGoal; set => SetProperty(ref _showShotsOnGoal, value); }

    // Overlay window
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

    public bool ClickThrough { get => _clickThrough; set => SetProperty(ref _clickThrough, value); }

    public bool LockPosition { get => _lockPosition; set => SetProperty(ref _lockPosition, value); }

    public bool SnapToEdges { get => _snapToEdges; set => SetProperty(ref _snapToEdges, value); }

    public string ToggleOverlayHotkey { get => _toggleOverlayHotkey; set => SetProperty(ref _toggleOverlayHotkey, value); }

    public string ToggleInteractionHotkey { get => _toggleInteractionHotkey; set => SetProperty(ref _toggleInteractionHotkey, value); }

    public string ToggleDoNotDisturbHotkey { get => _toggleDoNotDisturbHotkey; set => SetProperty(ref _toggleDoNotDisturbHotkey, value); }

    /// <summary>Registration problems from the last time the hotkeys were applied (empty when all is well).</summary>
    public string HotkeyProblems
    {
        get => _hotkeyProblems;
        private set
        {
            if (SetProperty(ref _hotkeyProblems, value)) OnPropertyChanged(nameof(HasHotkeyProblems));
        }
    }

    public bool HasHotkeyProblems => HotkeyProblems.Length > 0;

    // Advanced
    public string SettingsPath => _settings.SettingsPath;

    public string StorageMode => _settings.IsPortable ? "Portable (stored next to the executable)" : "User profile (%AppData%)";

    public string? LoadWarning => _settings.LoadWarning;

    public bool HasLoadWarning => LoadWarning is not null;

    public string ApiEndpoint => NhlApiService.BaseAddress.ToString();

    public string ApiStatus { get => _apiStatus; private set => SetProperty(ref _apiStatus, value); }

    public string LastSuccessText { get => _lastSuccessText; private set => SetProperty(ref _lastSuccessText, value); }

    public string LastErrorText { get => _lastErrorText; private set => SetProperty(ref _lastErrorText, value); }

    private void LoadFrom(AppSettings s)
    {
        LaunchWithWindows = s.General.LaunchWithWindows;
        ShowOverlayOnStartup = s.General.ShowOverlayOnStartup;
        RefreshIntervalSeconds = s.General.RefreshIntervalSeconds;
        DisplayMode = s.Appearance.DisplayMode;
        BackgroundOpacityPercent = s.Appearance.BackgroundOpacity * 100;
        UiScalePercent = s.Appearance.UiScale * 100;
        ShowTeamLogos = s.Appearance.ShowTeamLogos;
        ShowGameClock = s.Appearance.ShowGameClock;
        ShowShotsOnGoal = s.Appearance.ShowShotsOnGoal;
        AlwaysOnTop = s.Window.AlwaysOnTop;
        ClickThrough = s.Window.ClickThrough;
        LockPosition = s.Window.LockPosition;
        SnapToEdges = s.Window.SnapToEdges;
        ToggleOverlayHotkey = s.Hotkeys.ToggleOverlay;
        ToggleInteractionHotkey = s.Hotkeys.ToggleInteraction;
        ToggleDoNotDisturbHotkey = s.Hotkeys.ToggleDoNotDisturb;
        foreach (var section in _sections) section.Load(s);
    }

    private void Save()
    {
        var startupChanged = _settings.Current.General.LaunchWithWindows != LaunchWithWindows;
        _settings.Update(s =>
        {
            s.General.LaunchWithWindows = LaunchWithWindows;
            s.General.ShowOverlayOnStartup = ShowOverlayOnStartup;
            s.General.RefreshIntervalSeconds = RefreshIntervalSeconds;
            s.Appearance.DisplayMode = DisplayMode;
            s.Appearance.BackgroundOpacity = BackgroundOpacityPercent / 100;
            s.Appearance.UiScale = UiScalePercent / 100;
            s.Appearance.ShowTeamLogos = ShowTeamLogos;
            s.Appearance.ShowGameClock = ShowGameClock;
            s.Appearance.ShowShotsOnGoal = ShowShotsOnGoal;
            s.Window.AlwaysOnTop = AlwaysOnTop;
            s.Window.ClickThrough = ClickThrough;
            s.Window.LockPosition = LockPosition;
            s.Window.SnapToEdges = SnapToEdges;
            s.Hotkeys.ToggleOverlay = ToggleOverlayHotkey;
            s.Hotkeys.ToggleInteraction = ToggleInteractionHotkey;
            s.Hotkeys.ToggleDoNotDisturb = ToggleDoNotDisturbHotkey;
            foreach (var section in _sections) section.Apply(s);
        });
        if (startupChanged) StartupRegistration.Apply(LaunchWithWindows);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Reset()
    {
        var answer = MessageBox.Show(
            "Reset all settings to their defaults? The overlay will also move back to its default position.",
            "NHL Live Overlay", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        _settings.ResetToDefaults();
        StartupRegistration.Apply(_settings.Current.General.LaunchWithWindows);
        LoadFrom(_settings.Current);
    }

    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.SettingsDirectory);
            Process.Start(new ProcessStartInfo(_settings.SettingsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the settings folder.", ex);
        }
    }

    private void UpdateStatus(ScoreboardSnapshot snapshot)
    {
        var culture = CultureInfo.CurrentCulture;
        ApiStatus = (snapshot.HasData, snapshot.IsStale, snapshot.LastError) switch
        {
            (false, _, null) => "Connecting...",
            (false, _, _) => "Unavailable (no data yet)",
            (true, true, _) => "Degraded (showing cached scores)",
            _ => "Connected",
        };
        LastSuccessText = snapshot.LastSuccessUtc is { } ok ? ok.ToLocalTime().ToString("G", culture) : "Never";
        LastErrorText = snapshot.LastError is { } error && snapshot.LastErrorUtc is { } at
            ? $"{at.ToLocalTime().ToString("G", culture)}: {error}"
            : "None";
    }

    public void Dispose()
    {
        _state.Changed -= _stateHandler;
        _hotkeys.ProblemsChanged -= _hotkeyProblemsHandler;
    }
}
