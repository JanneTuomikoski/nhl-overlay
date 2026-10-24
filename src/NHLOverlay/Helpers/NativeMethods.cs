using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NHLOverlay.Helpers;

internal static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExLayered = 0x00080000;
    public const long WsExNoActivate = 0x08000000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0200;

    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNoTopmost = new(-2);

    public const int WmHotkey = 0x0312;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const uint ModNoRepeat = 0x4000;

    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>Physical Ctrl key state; reliable even when this window is not active.</summary>
    public static bool IsControlKeyDown() => (GetAsyncKeyState(0x11) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>Asks DWM to draw a dark title bar (Windows 10 20H1+ / Windows 11). No-op elsewhere.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    /// <summary>Sets or clears extended window style bits.</summary>
    public static void SetExtendedStyle(IntPtr hwnd, long flags, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var updated = enabled ? style | flags : style & ~flags;
        if (updated != style)
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
    }

    /// <summary>Work area (excluding the taskbar) of the monitor that best contains <paramref name="rect"/>, in pixels.</summary>
    public static Rect GetWorkArea(Rect rect)
    {
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? info.Work : rect;
    }

    public static Rect GetPrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(new Point(), MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? info.Work : new Rect { Right = 1920, Bottom = 1080 };
    }

    /// <summary>True when the given rectangle overlaps a connected monitor.</summary>
    public static bool IsOnAnyMonitor(Rect rect) => MonitorFromRect(ref rect, MonitorDefaultToNull) != IntPtr.Zero;
}
