using System.Windows.Interop;
using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>
/// System-wide hotkeys via RegisterHotKey, delivered to a window's message loop. Registration fails when
/// another application already owns the combination; that is reported instead of silently ignored.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0x4E48; // arbitrary base ("NH")

    public GlobalHotkeys(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Window handle has no HwndSource.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns false if it is invalid or already taken by another application.</summary>
    public bool Register(HotkeyGesture gesture, Action action)
    {
        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, (uint)gesture.Modifiers | NativeMethods.ModNoRepeat, gesture.VirtualKey))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
