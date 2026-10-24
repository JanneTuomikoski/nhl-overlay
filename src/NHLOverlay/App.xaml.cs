using System.Windows;
using System.Windows.Threading;
using NHLOverlay.Helpers;
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
    private ScorePollingService? _polling;
    private GameStateService? _state;
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

        _state = new GameStateService();
        _api = new NhlApiService();
        _polling = new ScorePollingService(
            _api,
            _state,
            () => TimeSpan.FromSeconds(_settings.Current.General.RefreshIntervalSeconds),
            TimeProvider.System);

        var overlayViewModel = new OverlayViewModel(_state, _settings, Dispatcher);
        _overlay = new OverlayWindow(overlayViewModel, _settings);
        _overlay.SettingsRequested += (_, _) => ShowSettings();
        _overlay.IsVisibleChanged += (_, _) => _tray?.SetOverlayVisible(_overlay.IsVisible);

        _tray = new TrayIcon();
        _tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        _tray.RefreshRequested += (_, _) => _polling.RequestRefresh();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => Shutdown();

        if (_settings.Current.General.ShowOverlayOnStartup)
            _overlay.Show();
        _tray.SetOverlayVisible(_overlay.IsVisible);

        _polling.Start();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.Hide();
        else _overlay.Show();
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

        var viewModel = new SettingsViewModel(_settings!, _state!, () => _polling?.RequestRefresh(), Dispatcher);
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
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
        _api?.Dispose();
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
