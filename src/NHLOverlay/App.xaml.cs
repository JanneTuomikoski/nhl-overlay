using System.Windows;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;
using NHLOverlay.Views;

namespace NHLOverlay;

/// <summary>
/// Composition root. Services are wired by hand (constructor injection without a container) to keep
/// the dependency footprint small. The tray icon owns the application lifetime.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private SettingsService? _settings;
    private NhlApiService? _api;
    private LiigaApiService? _liiga;
    private LogoService? _logos;
    private TeamDirectoryService? _teams;
    private RosterService? _rosters;
    private BoxscoreService? _boxscores;
    private ScheduleService? _schedules;
    private FocusGameService? _focus;
    private PlayerStatsService? _playerStats;
    private SpoilerService? _spoilers;
    private FocusWindow? _focusWindow;
    private ScorePollingService? _polling;
    private GameStateService? _state;
    private RuntimeState? _runtime;
    private ToastService? _toasts;
    private SoundService? _sounds;
    private SpeechService? _speech;
    private NotificationService? _notifications;
    private ForegroundWatcher? _foreground;
    private HotkeyService? _hotkeys;
    private OverlayWindow? _overlay;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\NHLOverlay.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _settings = SettingsService.CreateDefault();
        Log.Initialize(Path.Combine(_settings.SettingsDirectory, "logs"));
        Log.Info($"Starting NHL Live Overlay. Settings: {_settings.SettingsPath}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        _settings.Load();

        // Data
        _state = new GameStateService();
        _api = new NhlApiService();
        _logos = new LogoService(Path.Combine(_settings.CacheDirectory, "logos"));
        _liiga = new LiigaApiService();
        _polling = new ScorePollingService(
            _api,
            _state,
            () => TimeSpan.FromSeconds(_settings.Current.General.RefreshIntervalSeconds),
            TimeProvider.System,
            liiga: _liiga,
            liigaEnabled: () => _settings.Current.General.ShowLiiga);
        _teams = new TeamDirectoryService(_api, _settings.CacheDirectory, TimeProvider.System);
        _teams.Start(_state);
        _rosters = new RosterService(_api, _settings.CacheDirectory, TimeProvider.System);
        _rosters.SetCountryOverrides(_settings.Current.CountryWatch.Overrides);
        _rosters.Start(_state);
        _boxscores = new BoxscoreService(_api, TimeProvider.System);
        _boxscores.Start();
        _schedules = new ScheduleService(_api, _settings.CacheDirectory, TimeProvider.System);
        _schedules.Start();
        var countryWatch = new CountryWatchService(_state, _settings, _rosters, _boxscores);

        // Session state and notifications
        _runtime = new RuntimeState();
        _toasts = new ToastService(Dispatcher);
        _sounds = new SoundService();
        _speech = new SpeechService();
        _tray = new TrayIcon();
        var appState = new AppStateService(_settings.SettingsDirectory, TimeProvider.System);
        var spoilers = new SpoilerService(appState, _settings);
        var recaps = new RecapService(_settings, spoilers, _boxscores, countryWatch, appState);
        _notifications = new NotificationService(
            _state, _settings, _runtime, _toasts, _sounds, _speech, _tray.ShowNotification, Dispatcher, findPlayer: countryWatch.FindPlayer)
        {
            IsSuppressed = e => spoilers.IsShielded(e.Game),
        };

        // Overlay
        var standings = new StandingsViewModel(_teams, _schedules, _logos);
        _focus = new FocusGameService(_api, () => TimeSpan.FromSeconds(_settings.Current.General.RefreshIntervalSeconds));
        _spoilers = spoilers;
        _playerStats = new PlayerStatsService(_api, _settings.CacheDirectory, TimeProvider.System);
        _playerStats.Start();
        var watches = new WatchService(_state, _settings, _playerStats, _boxscores, _rosters, _notifications.Deliver, Dispatcher);
        var overlayViewModel = new OverlayViewModel(
            _state, _settings, _logos, _teams, _runtime, _toasts, countryWatch, spoilers, recaps, standings, _schedules, watches,
            _polling.SetViewedDate, ShowFocus, Dispatcher);
        _overlay = new OverlayWindow(overlayViewModel, _settings, _runtime);
        _overlay.SettingsRequested += (_, _) => ShowSettings();
        _overlay.IsVisibleChanged += (_, _) => _tray?.SetOverlayVisible(_overlay.IsVisible);

        // Tray
        _tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        _tray.ToggleInteractionRequested += (_, _) => ToggleInteraction();
        _tray.ToggleDisplayModeRequested += (_, _) => _settings.Update(s =>
            s.Appearance.DisplayMode = s.Appearance.DisplayMode == DisplayMode.Compact ? DisplayMode.Expanded : DisplayMode.Compact);
        _tray.ToggleDoNotDisturbRequested += (_, _) => ToggleDoNotDisturb();
        _tray.RefreshRequested += (_, _) => _polling.RequestRefresh();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => Shutdown();
        _settings.Changed += (_, s) => Dispatcher.InvokeAsync(() =>
        {
            _polling.Nudge(); // e.g. Liiga switched on: fetch it now rather than at the next due time
            _rosters?.SetCountryOverrides(s.CountryWatch.Overrides);
            SyncTray();
            UpdateGaming(_foreground?.CurrentProcess);
        });
        _runtime.Changed += (_, _) => SyncTray();
        SyncTray();

        // Game detection (foreground window changes)
        _foreground = new ForegroundWatcher();
        _foreground.ForegroundChanged += (_, process) => UpdateGaming(process);
        UpdateGaming(_foreground.CurrentProcess);

        // The hotkeys need a window handle even while the overlay starts hidden.
        var hwnd = _overlay.EnsureHandle();
        _hotkeys = new HotkeyService(hwnd, _settings, ToggleOverlay, ToggleInteraction, ToggleDoNotDisturb);

        if (_settings.Current.General.ShowOverlayOnStartup)
            _overlay.Show();
        _tray.SetOverlayVisible(_overlay.IsVisible);

        _polling.Start();
    }

    private void SyncTray()
    {
        if (_tray is null || _settings is null || _runtime is null) return;
        var s = _settings.Current;
        _tray.SetInteractionMode(!_runtime.EffectiveClickThrough(s));
        _tray.SetExpanded(s.Appearance.DisplayMode == DisplayMode.Expanded);
        _tray.SetDoNotDisturb(_runtime.DoNotDisturb);
        _tray.SetFilterChoices(Enum.GetValues<GameFilter>().Select(filter => (
            GameOrdering.Describe(filter),
            filter == s.Teams.Filter,
            (Action)(() => _settings.Update(x => x.Teams.Filter = filter)))));
    }

    /// <summary>Activates the gaming profile while one of the configured games is in the foreground.</summary>
    private void UpdateGaming(string? process)
    {
        if (_settings is null || _runtime is null) return;
        var gaming = _settings.Current.Gaming;
        var isGame = gaming.Enabled && process is not null
                     && gaming.GameProcesses.Contains(process, StringComparer.OrdinalIgnoreCase);
        if (isGame != _runtime.IsGaming) Log.Info(isGame ? $"Gaming profile on ({process})." : "Gaming profile off.");
        _runtime.SetGaming(isGame, isGame ? process : null);
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.Hide();
        else _overlay.Show();
    }

    /// <summary>
    /// Switches between click-through and interaction mode. While the gaming profile forces click-through,
    /// this only overrides it for the current session; otherwise it changes the setting. Entering interaction
    /// mode also shows the overlay.
    /// </summary>
    private void ToggleInteraction()
    {
        if (_settings is null || _overlay is null || _runtime is null) return;
        var s = _settings.Current;
        var clickThrough = _runtime.EffectiveClickThrough(s);
        if (_runtime.GamingForcesClickThrough(s))
        {
            _runtime.ClickThroughOverride = !clickThrough;
        }
        else
        {
            _runtime.ClickThroughOverride = null;
            _settings.Update(x => x.Window.ClickThrough = !clickThrough);
        }

        if (clickThrough && !_overlay.IsVisible) _overlay.Show();
    }

    /// <summary>Opens (or retargets) the focus dashboard window for a game.</summary>
    private void ShowFocus(long gameId)
    {
        if (_focus is null) return;
        _focus.Focus(gameId);
        if (_focusWindow is null)
        {
            var viewModel = new FocusViewModel(_focus, _state!, _settings!, _logos!, _teams!, _spoilers!, Dispatcher);
            _focusWindow = new FocusWindow(viewModel, _settings!);
            _focusWindow.Closed += (_, _) =>
            {
                _focusWindow = null;
                _focus?.Stop();
            };
            _focusWindow.Show();
        }

        ((FocusViewModel)_focusWindow.DataContext).Refresh();
        if (_focusWindow.WindowState == WindowState.Minimized) _focusWindow.WindowState = WindowState.Normal;
        _focusWindow.Activate();
    }

    private void ToggleDoNotDisturb()
    {
        if (_runtime is null) return;
        _runtime.DoNotDisturb = !_runtime.DoNotDisturb;
        Log.Info(_runtime.DoNotDisturb ? "Do not disturb on." : "Do not disturb off.");
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(
            _settings!, _state!, _hotkeys!, _teams!, _logos!, _notifications!, _speech!, _rosters!, () => _polling?.RequestRefresh(), Dispatcher);
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _hotkeys?.Suspend(false);
        };
        // Release the global hotkeys so new combinations can be recorded in the settings window.
        _hotkeys?.Suspend(true);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Never show crash dialogs over a game; log and keep running.
        Log.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Exiting.");
        _polling?.Dispose();
        _teams?.Dispose();
        _rosters?.Dispose();
        _boxscores?.Dispose();
        _schedules?.Dispose();
        _focus?.Dispose();
        _playerStats?.Dispose();
        _api?.Dispose();
        _liiga?.Dispose();
        _logos?.Dispose();
        _hotkeys?.Dispose();
        _foreground?.Dispose();
        _speech?.Dispose();
        _sounds?.Dispose();
        if (_overlay is not null)
        {
            _overlay.AllowClose = true;
            _overlay.Close();
        }

        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }
}
