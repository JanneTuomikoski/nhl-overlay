using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NHLOverlay.Helpers;

/// <summary>
/// Reports the process name of the foreground window whenever it changes, via a WinEvent hook
/// (no polling, no injection: an out-of-context hook delivered through this thread's message loop).
/// Create and dispose on the UI thread.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private readonly WinEventProc _callback; // kept alive for the lifetime of the hook
    private readonly IntPtr _hook;
    private readonly Dictionary<uint, string> _names = [];

    public ForegroundWatcher()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Report(hwnd);
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
    }

    /// <summary>Process name (no ".exe") of the new foreground window; null if it cannot be determined.</summary>
    public event EventHandler<string?>? ForegroundChanged;

    public string? CurrentProcess => ProcessName(GetForegroundWindow());

    private void Report(IntPtr hwnd) => ForegroundChanged?.Invoke(this, ProcessName(hwnd));

    private string? ProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        if (_names.TryGetValue(pid, out var cached)) return cached;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (_names.Count > 256) _names.Clear(); // pids get reused; keep the cache small
            _names[pid] = name;
            return name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }
}
