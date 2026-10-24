using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

/// <summary>
/// Editable copy of the settings. Nothing is applied until <see cref="SaveCommand"/>; saving only writes
/// the fields this window owns, so it never overwrites state changed elsewhere (e.g. overlay position).
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly GameStateService _state;
    private readonly Dispatcher _dispatcher;
    private readonly EventHandler<ScoreboardSnapshot> _stateHandler;

    private bool _launchWithWindows;
    private bool _showOverlayOnStartup;
    private int _refreshIntervalSeconds;
    private double _backgroundOpacityPercent;
    private double _uiScalePercent;
    private bool _showGameClock;
    private bool _showShotsOnGoal;
    private string _apiStatus = "";
    private string _lastSuccessText = "";
    private string _lastErrorText = "";

    public SettingsViewModel(SettingsService settings, GameStateService state, Action refreshNow, Dispatcher dispatcher)
    {
        _settings = settings;
        _state = state;
        _dispatcher = dispatcher;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        RefreshNowCommand = new RelayCommand(refreshNow);
        ResetCommand = new RelayCommand(Reset);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);

        LoadFrom(settings.Current);
        _stateHandler = (_, snapshot) => _dispatcher.InvokeAsync(() => UpdateStatus(snapshot));
        _state.Changed += _stateHandler;
        UpdateStatus(state.Current);
    }

    public event EventHandler? CloseRequested;

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshNowCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }

    // General
    public IReadOnlyList<int> RefreshIntervals { get; } = GeneralSettings.AllowedRefreshIntervals;

    public bool LaunchWithWindows { get => _launchWithWindows; set => SetProperty(ref _launchWithWindows, value); }

    public bool ShowOverlayOnStartup { get => _showOverlayOnStartup; set => SetProperty(ref _showOverlayOnStartup, value); }

    public int RefreshIntervalSeconds { get => _refreshIntervalSeconds; set => SetProperty(ref _refreshIntervalSeconds, value); }

    // Appearance
    public double BackgroundOpacityPercent { get => _backgroundOpacityPercent; set => SetProperty(ref _backgroundOpacityPercent, Math.Round(value)); }

    public double UiScalePercent { get => _uiScalePercent; set => SetProperty(ref _uiScalePercent, Math.Round(value / 5) * 5); }

    public bool ShowGameClock { get => _showGameClock; set => SetProperty(ref _showGameClock, value); }

    public bool ShowShotsOnGoal { get => _showShotsOnGoal; set => SetProperty(ref _showShotsOnGoal, value); }

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
        BackgroundOpacityPercent = s.Appearance.BackgroundOpacity * 100;
        UiScalePercent = s.Appearance.UiScale * 100;
        ShowGameClock = s.Appearance.ShowGameClock;
        ShowShotsOnGoal = s.Appearance.ShowShotsOnGoal;
    }

    private void Save()
    {
        var startupChanged = _settings.Current.General.LaunchWithWindows != LaunchWithWindows;
        _settings.Update(s =>
        {
            s.General.LaunchWithWindows = LaunchWithWindows;
            s.General.ShowOverlayOnStartup = ShowOverlayOnStartup;
            s.General.RefreshIntervalSeconds = RefreshIntervalSeconds;
            s.Appearance.BackgroundOpacity = BackgroundOpacityPercent / 100;
            s.Appearance.UiScale = UiScalePercent / 100;
            s.Appearance.ShowGameClock = ShowGameClock;
            s.Appearance.ShowShotsOnGoal = ShowShotsOnGoal;
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

    public void Dispose() => _state.Changed -= _stateHandler;
}
