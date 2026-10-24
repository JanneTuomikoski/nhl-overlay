using System.Windows.Controls;
using System.Windows.Input;
using NHLOverlay.Models;

namespace NHLOverlay.Controls;

/// <summary>
/// Records a hotkey: press the combination (e.g. Ctrl+Alt+N) while focused. Backspace or Delete clears
/// it (disables the hotkey). Combinations without Ctrl, Alt or Win are ignored.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return; // keep keyboard navigation working
        e.Handled = true;

        if (key is Key.Back or Key.Delete)
        {
            Text = "";
            return;
        }

        if (HotkeyGesture.IsModifierKey(key)) return;

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        if (HotkeyGesture.TryParse(new HotkeyGesture(modifiers, key).ToString(), out var gesture))
            Text = gesture.ToString();
    }
}
