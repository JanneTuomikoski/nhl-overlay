using System.Windows.Input;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>Favorite teams, game filter, sort order and favorite highlighting.</summary>
public sealed class TeamsSectionViewModel : ObservableObject, ISettingsSection
{
    private static readonly string[] DivisionOrder = ["A", "M", "C", "P"];

    private GameFilter _filter;
    private GameSortOrder _sortOrder;
    private FavoriteHighlight _highlight;

    public TeamsSectionViewModel(TeamDirectoryService teams, LogoService logos)
    {
        TeamGroups = teams.Teams
            .GroupBy(t => t.Division)
            .OrderBy(g => Array.IndexOf(DivisionOrder, g.Key) is var i && i >= 0 ? i : 99)
            .Select(g => new TeamGroup(
                g.First().DivisionName,
                g.OrderBy(t => t.FullName).Select(t => new TeamChoiceViewModel(t.Abbrev, t.FullName)).ToList()))
            .ToList();
        AllTeams = TeamGroups.SelectMany(g => g.Teams).ToList();

        foreach (var team in AllTeams)
        {
            var url = teams.Find(team.Abbrev)?.LogoUrl;
            _ = LoadLogoAsync(team, url, logos);
        }

        ClearFavoritesCommand = new RelayCommand(() =>
        {
            foreach (var team in AllTeams) team.IsFavorite = false;
        });
    }

    public IReadOnlyList<TeamGroup> TeamGroups { get; }

    public IReadOnlyList<TeamChoiceViewModel> AllTeams { get; }

    public ICommand ClearFavoritesCommand { get; }

    public IReadOnlyList<ChoiceOption<GameFilter>> FilterOptions { get; } =
        Enum.GetValues<GameFilter>().Select(f => new ChoiceOption<GameFilter>(f, GameOrdering.Describe(f))).ToList();

    public IReadOnlyList<ChoiceOption<GameSortOrder>> SortOptions { get; } =
    [
        new(GameSortOrder.Default, "Live and favorites first"),
        new(GameSortOrder.StartTime, "Start time"),
    ];

    public IReadOnlyList<ChoiceOption<FavoriteHighlight>> HighlightOptions { get; } =
    [
        new(FavoriteHighlight.BarAndTint, "Bar and tint"),
        new(FavoriteHighlight.Bar, "Bar only"),
        new(FavoriteHighlight.None, "None"),
    ];

    public GameFilter Filter { get => _filter; set => SetProperty(ref _filter, value); }

    public GameSortOrder SortOrder { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }

    public FavoriteHighlight Highlight { get => _highlight; set => SetProperty(ref _highlight, value); }

    public void Load(AppSettings settings)
    {
        var favorites = settings.Teams.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var team in AllTeams) team.IsFavorite = favorites.Contains(team.Abbrev);
        Filter = settings.Teams.Filter;
        SortOrder = settings.Teams.SortOrder;
        Highlight = settings.Teams.Highlight;
    }

    public void Apply(AppSettings settings)
    {
        // Keep favorites that are not in the NHL picker (e.g. Liiga teams managed elsewhere).
        var nhl = AllTeams.Select(t => t.Abbrev).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.Teams.Favorites = settings.Teams.Favorites.Where(f => !nhl.Contains(f))
            .Concat(AllTeams.Where(t => t.IsFavorite).Select(t => t.Abbrev))
            .ToList();
        settings.Teams.Filter = Filter;
        settings.Teams.SortOrder = SortOrder;
        settings.Teams.Highlight = Highlight;
    }

    private static async Task LoadLogoAsync(TeamChoiceViewModel team, string? url, LogoService logos)
    {
        try
        {
            team.Logo = await logos.GetLogoAsync(url);
        }
        catch (Exception ex)
        {
            Log.Warn($"Logo for {team.Abbrev} failed.", ex);
        }
    }
}
