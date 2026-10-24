using System.Windows.Input;

namespace NHLOverlay.Models;

/// <summary>Modifier flags; values match the Win32 RegisterHotKey MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global hotkey such as "Ctrl+Alt+N", stored in settings as text.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, Key Key)
{
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>
    /// Parses "Ctrl+Alt+N" style text. Ctrl, Alt or Win is required so a plain key (or Shift+key)
    /// can never be stolen from a game.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var parsed)) return false;
                    key = parsed;
                    break;
            }
        }

        if (key is not { } k || IsModifierKey(k)) return false;
        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0) return false;

        gesture = new HotkeyGesture(modifiers, k);
        return true;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        // Reject numeric strings: Enum.TryParse would accept them as raw enum values.
        if (text.All(char.IsAsciiDigit)) return false;
        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (Key - Key.D0))).ToString() : Key.ToString());
        return string.Join("+", parts);
    }
}
