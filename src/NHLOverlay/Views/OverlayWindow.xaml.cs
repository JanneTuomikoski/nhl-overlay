using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Views;

/// <summary>
/// Borderless, always-on-top, transparent overlay. It never takes focus from the game (WS_EX_NOACTIVATE)
/// and, in click-through mode, lets all mouse input pass to the window underneath (WS_EX_TRANSPARENT).
/// The position is stored in physical pixels so it survives monitors with different DPI scaling, and is
/// validated against the connected monitors whenever the display layout changes.
/// Closing the window only hides it; the tray owns the application lifetime.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ScreenMargin = 16;
    private const double SnapDistance = 16;
    private const double ScaleStep = 0.1;

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _savePositionTimer;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private bool _placing;
    private bool _resizing;
    private bool _rendered; // before the first render the window can still have the default (huge) size

    public OverlayWindow(OverlayViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        Root.Width = settings.Current.Window.Width;

        // Debounce position saves while the window moves.
        _savePositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer.Stop();
            SavePosition();
        };
        LocationChanged += (_, _) =>
        {
            if (_placing) return;
            _savePositionTimer.Stop();
            _savePositionTimer.Start();
        };

        // Borderless games sometimes push themselves above other topmost windows; reclaim the spot.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _topmostTimer.Start();
            else _topmostTimer.Stop();
        };

        SizeChanged += (_, _) =>
        {
            if (!_resizing)
            {
                // Default placement is anchored to the top-right corner, so follow width changes;
                // a saved position is nudged back on screen when scaling makes the overlay grow past an edge.
                if (_settings.Current.Window.ScreenX is null) PlaceAtDefault();
                else KeepInsideWorkArea();
            }

            UpdateMaxHeight();
        };
        ContentRendered += (_, _) =>
        {
            _rendered = true;
            if (_settings.Current.Window.ScreenX is null) PlaceAtDefault();
            else KeepInsideWorkArea();
        };
        DpiChanged += (_, _) => UpdateMaxHeight();
        PreviewMouseWheel += OnPreviewMouseWheel;
        SourceInitialized += OnSourceInitialized;
        settings.Changed += (_, s) => Dispatcher.InvokeAsync(() => ApplyWindowSettings(s));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? SettingsRequested;

    /// <summary>Set by the application on exit so the window can really close.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Creates the native window without showing it (needed for global hotkeys).</summary>
    public IntPtr EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Never activate (keeps keyboard focus in the game) and stay out of Alt+Tab.
        NativeMethods.SetExtendedStyle(_hwnd, NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow, true);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyWindowSettings(_settings.Current);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private void ApplyWindowSettings(AppSettings s)
    {
        if (!_resizing) Root.Width = s.Window.Width;
        Topmost = s.Window.AlwaysOnTop;
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetExtendedStyle(_hwnd, NativeMethods.WsExTransparent, s.Window.ClickThrough);
        ApplyPosition(s.Window);
    }

    private void ApplyPosition(WindowSettings w)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (w.ScreenX is not int x || w.ScreenY is not int y)
        {
            PlaceAtDefault();
            return;
        }

        NativeMethods.GetWindowRect(_hwnd, out var current);
        if (current.Left == x && current.Top == y) return;

        var target = new NativeMethods.Rect { Left = x, Top = y, Right = x + Math.Max(current.Width, 100), Bottom = y + Math.Max(current.Height, 40) };
        if (NativeMethods.IsOnAnyMonitor(target)) MoveTo(x, y);
        else PlaceAtDefault(); // The saved monitor is gone (unplugged, layout changed).
    }

    /// <summary>Top-right corner of the primary monitor's work area.</summary>
    private void PlaceAtDefault()
    {
        if (_hwnd == IntPtr.Zero) return;
        var work = NativeMethods.GetPrimaryWorkArea();
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var margin = (int)Math.Round(ScreenMargin * VisualTreeHelper.GetDpi(this).DpiScaleX);
        MoveTo(work.Right - rect.Width - margin, work.Top + margin);
    }

    private void MoveTo(int x, int y)
    {
        _placing = true;
        try
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }
        finally
        {
            _placing = false;
        }
    }

    private void SavePosition()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var current = _settings.Current.Window;
        if (current.ScreenX == rect.Left && current.ScreenY == rect.Top) return;
        _settings.Update(s =>
        {
            s.Window.ScreenX = rect.Left;
            s.Window.ScreenY = rect.Top;
        });
    }

    /// <summary>Keeps the overlay no taller than its monitor's work area; the game list scrolls beyond that.</summary>
    private void UpdateMaxHeight()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var work = NativeMethods.GetWorkArea(rect);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var maxHeight = Math.Max(120, (work.Height / dpi) - (2 * ScreenMargin));
        if (Math.Abs(MaxHeight - maxHeight) > 0.5) MaxHeight = maxHeight;
    }

    /// <summary>Moves the overlay so it fits inside its monitor's work area, then remembers the new position.</summary>
    private void KeepInsideWorkArea()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || !_rendered) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var work = NativeMethods.GetWorkArea(rect);
        var x = Math.Max(work.Left, Math.Min(rect.Left, work.Right - rect.Width));
        var y = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - rect.Height));
        if (x == rect.Left && y == rect.Top) return;
        MoveTo(x, y);
        SavePosition();
    }

    private void SnapToWorkAreaEdges()
    {
        if (!_settings.Current.Window.SnapToEdges || _hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var work = NativeMethods.GetWorkArea(rect);
        var snap = (int)Math.Round(SnapDistance * VisualTreeHelper.GetDpi(this).DpiScaleX);

        var x = rect.Left;
        var y = rect.Top;
        if (Math.Abs(rect.Left - work.Left) <= snap) x = work.Left;
        else if (Math.Abs(work.Right - rect.Right) <= snap) x = work.Right - rect.Width;
        if (Math.Abs(rect.Top - work.Top) <= snap) y = work.Top;
        else if (Math.Abs(work.Bottom - rect.Bottom) <= snap) y = work.Bottom - rect.Height;

        if (x != rect.Left || y != rect.Top) MoveTo(x, y);
    }

    private void ReassertTopmost()
    {
        if (!Topmost || !IsVisible || _hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        ApplyPosition(_settings.Current.Window);
        UpdateMaxHeight();
    });

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Current.Window.LockPosition || e.ButtonState != MouseButtonState.Pressed) return;
        NativeMethods.GetWindowRect(_hwnd, out var before);
        DragMove();
        NativeMethods.GetWindowRect(_hwnd, out var after);
        if (before.Left == after.Left && before.Top == after.Top) return; // a click, not a drag
        SnapToWorkAreaEdges();
        _savePositionTimer.Stop();
        SavePosition();
    }

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        // Pin the current position so a right-anchored default placement doesn't move under the mouse.
        _resizing = true;
        SavePosition();
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // The grip lives inside the scaled content, so the delta is already in unscaled units.
        Root.Width = Math.Clamp(Root.Width + e.HorizontalChange, WindowSettings.MinWidth, WindowSettings.MaxWidth);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizing = false;
        var width = Math.Round(Root.Width);
        _settings.Update(s => s.Window.Width = width);
        KeepInsideWorkArea();
    }

    /// <summary>Ctrl + mouse wheel scales the whole overlay.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!NativeMethods.IsControlKeyDown()) return;
        e.Handled = true;
        var current = _settings.Current.Appearance.UiScale;
        var scale = Math.Round(Math.Clamp(current + (e.Delta > 0 ? ScaleStep : -ScaleStep), AppearanceSettings.MinUiScale, AppearanceSettings.MaxUiScale), 2);
        if (Math.Abs(scale - current) > 0.001)
            _settings.Update(s => s.Appearance.UiScale = scale);
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
            _topmostTimer.Stop();
            SavePosition();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

        base.OnClosing(e);
    }
}
