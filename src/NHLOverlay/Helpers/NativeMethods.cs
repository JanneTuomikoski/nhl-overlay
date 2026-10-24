using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NHLOverlay.Helpers;

internal static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Asks DWM to draw a dark title bar (Windows 10 20H1+ / Windows 11). No-op elsewhere.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
