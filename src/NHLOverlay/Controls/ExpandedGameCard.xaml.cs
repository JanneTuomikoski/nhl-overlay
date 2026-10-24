using System.Windows;
using System.Windows.Controls;

namespace NHLOverlay.Controls;

/// <summary>Large game card: logos, team names, shots, status and the full goal list.</summary>
public partial class ExpandedGameCard : UserControl
{
    /// <summary>Card width (in unscaled units) from which full team names fit next to the shots and score.</summary>
    private const double FullNameMinWidth = 380;

    public static readonly DependencyProperty UseFullNamesProperty =
        DependencyProperty.Register(nameof(UseFullNames), typeof(bool), typeof(ExpandedGameCard), new PropertyMetadata(false));

    public ExpandedGameCard()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UseFullNames = e.NewSize.Width >= FullNameMinWidth;
    }

    /// <summary>"Colorado Avalanche" when wide enough, otherwise "Avalanche".</summary>
    public bool UseFullNames
    {
        get => (bool)GetValue(UseFullNamesProperty);
        set => SetValue(UseFullNamesProperty, value);
    }
}
