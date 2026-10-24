using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>A country chip in the players tab.</summary>
public sealed class CountryChoiceViewModel(string code, string name) : ObservableObject
{
    private bool _isSelected;

    public string Code { get; } = code;

    public string Name { get; } = name;

    public string Label => $"{Code}  {Name}";

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>Country watch (e.g. Finnish players).</summary>
public sealed class PlayersSectionViewModel : ObservableObject, ISettingsSection
{
    private bool _countryWatchEnabled;
    private bool _showOnCards;
    private bool _showPanel;
    private bool _shutoutWatch;
    private bool _milestones;
    private bool _pointStreaks;
    private double _minStreak;
    private PlayerWatchScope _scope;

    public bool ShutoutWatch { get => _shutoutWatch; set => SetProperty(ref _shutoutWatch, value); }

    public bool Milestones { get => _milestones; set => SetProperty(ref _milestones, value); }

    public bool PointStreaks { get => _pointStreaks; set => SetProperty(ref _pointStreaks, value); }

    public double MinStreak { get => _minStreak; set => SetProperty(ref _minStreak, Math.Round(value)); }

    public PlayerWatchScope Scope { get => _scope; set => SetProperty(ref _scope, value); }

    public IReadOnlyList<ChoiceOption<PlayerWatchScope>> ScopeOptions { get; } =
    [
        new(PlayerWatchScope.FavoritesAndCountries, "Favorite teams and watched countries"),
        new(PlayerWatchScope.AllPlayers, "Every scorer"),
    ];

    public IReadOnlyList<CountryChoiceViewModel> Countries { get; } =
        HockeyCountries.All.Select(c => new CountryChoiceViewModel(c.Code, c.Name)).ToList();

    public bool CountryWatchEnabled { get => _countryWatchEnabled; set => SetProperty(ref _countryWatchEnabled, value); }

    public bool ShowOnCards { get => _showOnCards; set => SetProperty(ref _showOnCards, value); }

    public bool ShowPanel { get => _showPanel; set => SetProperty(ref _showPanel, value); }

    public void Load(AppSettings settings)
    {
        var watch = settings.CountryWatch;
        CountryWatchEnabled = watch.Enabled;
        ShowOnCards = watch.ShowOnCards;
        ShowPanel = watch.ShowPanel;
        foreach (var country in Countries) country.IsSelected = watch.Countries.Contains(country.Code);
        var players = settings.PlayerWatch;
        ShutoutWatch = players.ShutoutWatch;
        Milestones = players.Milestones;
        PointStreaks = players.PointStreaks;
        MinStreak = players.MinStreak;
        Scope = players.Scope;
    }

    public void Apply(AppSettings settings)
    {
        var watch = settings.CountryWatch;
        watch.Enabled = CountryWatchEnabled;
        watch.ShowOnCards = ShowOnCards;
        watch.ShowPanel = ShowPanel;
        // Keep codes that aren't offered as chips (hand-edited settings).
        var offered = Countries.Select(c => c.Code).ToHashSet();
        watch.Countries = watch.Countries.Where(c => !offered.Contains(c))
            .Concat(Countries.Where(c => c.IsSelected).Select(c => c.Code))
            .ToList();
        var players = settings.PlayerWatch;
        players.ShutoutWatch = ShutoutWatch;
        players.Milestones = Milestones;
        players.PointStreaks = PointStreaks;
        players.MinStreak = (int)MinStreak;
        players.Scope = Scope;
    }
}
