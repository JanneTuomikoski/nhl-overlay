using System.Windows.Input;
using NHLOverlay.Models;

namespace NHLOverlay.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", "Ctrl+Alt+N")]
    [InlineData(" ctrl + alt + n ", "Ctrl+Alt+N")]
    [InlineData("Alt+Ctrl+I", "Ctrl+Alt+I")]
    [InlineData("Alt+F9", "Alt+F9")]
    [InlineData("Ctrl+Shift+1", "Ctrl+Shift+1")]
    [InlineData("Win+Alt+Home", "Alt+Win+Home")]
    [InlineData("Control+Alt+OemPlus", "Ctrl+Alt+OemPlus")]
    public void Parses_and_normalizes(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(expected, gesture.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("N")]
    [InlineData("Shift+N")] // would steal a normal key from games
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+N+M")]
    [InlineData("Ctrl+Alt+Banana")]
    [InlineData("Ctrl+65")]
    [InlineData("Ctrl+LeftShift")]
    public void Rejects_invalid_or_unsafe_combinations(string text) =>
        Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Maps_to_win32_values()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+N", out var gesture));
        Assert.Equal(Key.N, gesture.Key);
        Assert.Equal(0x4Eu, gesture.VirtualKey);
        Assert.Equal(3, (int)gesture.Modifiers); // MOD_ALT | MOD_CONTROL
    }
}
