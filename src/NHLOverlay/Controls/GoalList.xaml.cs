using System.Windows;
using System.Windows.Controls;

namespace NHLOverlay.Controls;

/// <summary>Aligned list of a game's goals: period, time, team, scorer, PP/SH/EN tags and running score.</summary>
public partial class GoalList : UserControl
{
    public static readonly DependencyProperty ShowAssistsProperty =
        DependencyProperty.Register(nameof(ShowAssists), typeof(bool), typeof(GoalList), new PropertyMetadata(false));

    public GoalList()
    {
        InitializeComponent();
    }

    /// <summary>Shows the assists on a second line under each scorer.</summary>
    public bool ShowAssists
    {
        get => (bool)GetValue(ShowAssistsProperty);
        set => SetValue(ShowAssistsProperty, value);
    }
}
