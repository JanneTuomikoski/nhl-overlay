using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Views;

/// <summary>
/// The focus dashboard: a normal resizable window (meant for a second or vertical monitor). Its position and
/// size are stored in physical pixels so it reopens on the same monitor.
/// </summary>
public partial class FocusWindow : Window
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _saveTimer;
    private IntPtr _hwnd;

    public FocusWindow(FocusViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePlacement();
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.UseDarkTitleBar(this);
            RestorePlacement();
        };
        LocationChanged += (_, _) => RestartSaveTimer();
        SizeChanged += (_, _) => RestartSaveTimer();
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            viewModel.Dispose();
        };
    }

    private void RestartSaveTimer()
    {
        if (_hwnd == IntPtr.Zero || WindowState != WindowState.Normal) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void RestorePlacement()
    {
        var focus = _settings.Current.Focus;
        if (focus is not { ScreenX: int x, ScreenY: int y, PixelWidth: int w, PixelHeight: int h }) return;
        var rect = new NativeMethods.Rect { Left = x, Top = y, Right = x + w, Bottom = y + h };
        if (!NativeMethods.IsOnAnyMonitor(rect)) return; // monitor gone: keep the centered default
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void SavePlacement()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var focus = _settings.Current.Focus;
        if (focus.ScreenX == rect.Left && focus.ScreenY == rect.Top && focus.PixelWidth == rect.Width && focus.PixelHeight == rect.Height) return;
        _settings.Update(s =>
        {
            s.Focus.ScreenX = rect.Left;
            s.Focus.ScreenY = rect.Top;
            s.Focus.PixelWidth = rect.Width;
            s.Focus.PixelHeight = rect.Height;
        });
    }
}
