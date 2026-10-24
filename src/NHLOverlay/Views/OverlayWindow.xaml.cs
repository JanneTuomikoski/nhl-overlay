using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Views;

/// <summary>
/// Borderless, always-on-top, transparent overlay. Closing it only hides it (the tray owns the app lifetime).
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _savePositionTimer;
    private bool _applyingSettings;

    public OverlayWindow(OverlayViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;

        // Debounce position saves while dragging.
        _savePositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer.Stop();
            SavePosition();
        };
        LocationChanged += (_, _) =>
        {
            if (_applyingSettings) return;
            _savePositionTimer.Stop();
            _savePositionTimer.Start();
        };

        ApplyWindowSettings(settings.Current);
        settings.Changed += (_, s) => Dispatcher.InvokeAsync(() => ApplyWindowSettings(s));
    }

    public event EventHandler? SettingsRequested;

    /// <summary>Set by the application on exit so the window can really close.</summary>
    public bool AllowClose { get; set; }

    private void ApplyWindowSettings(AppSettings s)
    {
        _applyingSettings = true;
        try
        {
            Root.Width = s.Window.Width;
            var scaledWidth = s.Window.Width * s.Appearance.UiScale;
            if (s.Window.Left is { } left && s.Window.Top is { } top && IsOnVirtualScreen(left, top, scaledWidth))
            {
                Left = left;
                Top = top;
            }
            else
            {
                // First run, reset, or the saved monitor is gone: top-right of the primary work area.
                var work = SystemParameters.WorkArea;
                Left = work.Right - scaledWidth - ScreenMargin;
                Top = work.Top + ScreenMargin;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    /// <summary>Coarse check that the header is reachable on the current desktop.</summary>
    private static bool IsOnVirtualScreen(double left, double top, double width)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.IntersectsWith(new Rect(left, top, Math.Max(width, 40), 24));
    }

    private void SavePosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        var current = _settings.Current.Window;
        if (current.Left == Left && current.Top == Top) return;
        _settings.Update(s =>
        {
            s.Window.Left = Left;
            s.Window.Top = Top;
        });
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _savePositionTimer.Stop();
            SavePosition();
        }

        base.OnClosing(e);
    }
}
