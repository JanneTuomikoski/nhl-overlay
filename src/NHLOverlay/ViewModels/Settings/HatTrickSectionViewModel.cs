using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>Hat-Trick Watch: detection scope and how it is shown.</summary>
public sealed class HatTrickSectionViewModel : ObservableObject, ISettingsSection
{
    private bool _enabled;
    private TeamScope _scope;
    private bool _highlightCards;
    private bool _showPlayerName;

    public IReadOnlyList<ChoiceOption<TeamScope>> ScopeOptions { get; } =
    [
        new(TeamScope.AllTeams, "All games"),
        new(TeamScope.FavoritesOnly, "Favorite teams' games"),
    ];

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public TeamScope Scope { get => _scope; set => SetProperty(ref _scope, value); }

    public bool HighlightCards { get => _highlightCards; set => SetProperty(ref _highlightCards, value); }

    public bool ShowPlayerName { get => _showPlayerName; set => SetProperty(ref _showPlayerName, value); }

    public void Load(AppSettings settings)
    {
        Enabled = settings.HatTrick.Enabled;
        Scope = settings.HatTrick.Scope;
        HighlightCards = settings.HatTrick.HighlightCards;
        ShowPlayerName = settings.HatTrick.ShowPlayerName;
    }

    public void Apply(AppSettings settings)
    {
        settings.HatTrick.Enabled = Enabled;
        settings.HatTrick.Scope = Scope;
        settings.HatTrick.HighlightCards = HighlightCards;
        settings.HatTrick.ShowPlayerName = ShowPlayerName;
    }
}
