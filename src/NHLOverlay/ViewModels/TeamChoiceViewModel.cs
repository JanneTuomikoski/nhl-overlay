using System.Windows.Media;
using NHLOverlay.Helpers;

namespace NHLOverlay.ViewModels;

/// <summary>A selectable value with a display label (for chip lists bound to enum settings).</summary>
public sealed record ChoiceOption<T>(T Value, string Label);

/// <summary>A team in a settings team picker (favorites, always-hidden teams).</summary>
public sealed class TeamChoiceViewModel(string abbrev, string fullName) : ObservableObject
{
    private bool _isSelected;
    private ImageSource? _logo;

    public string Abbrev { get; } = abbrev;

    public string FullName { get; } = fullName;

    public ImageSource? Logo { get => _logo; set => SetProperty(ref _logo, value); }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>Teams of one division, as shown in the picker.</summary>
public sealed record TeamGroup(string Name, IReadOnlyList<TeamChoiceViewModel> Teams);
