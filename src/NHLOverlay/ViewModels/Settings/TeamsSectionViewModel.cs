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
        TeamGroups = BuildTeamGroups(teams, logos);
        AllTeams = TeamGroups.SelectMany(g => g.Teams).ToList();
        ClearFavoritesCommand = new RelayCommand(() =>
        {
            foreach (var team in AllTeams) team.IsSelected = false;
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
        SelectTeams(AllTeams, settings.Teams.Favorites);
        Filter = settings.Teams.Filter;
        SortOrder = settings.Teams.SortOrder;
        Highlight = settings.Teams.Highlight;
    }

    public void Apply(AppSettings settings)
    {
        settings.Teams.Favorites = SelectedTeams(AllTeams, settings.Teams.Favorites);
        settings.Teams.Filter = Filter;
        settings.Teams.SortOrder = SortOrder;
        settings.Teams.Highlight = Highlight;
    }

    /// <summary>All NHL teams grouped by division (Atlantic, Metropolitan, Central, Pacific), with logos.</summary>
    internal static IReadOnlyList<TeamGroup> BuildTeamGroups(TeamDirectoryService teams, LogoService logos)
    {
        var groups = teams.Teams
            .GroupBy(t => t.Division)
            .OrderBy(g => Array.IndexOf(DivisionOrder, g.Key) is var i && i >= 0 ? i : 99)
            .Select(g => new TeamGroup(
                g.First().DivisionName,
                g.OrderBy(t => t.FullName).Select(t => new TeamChoiceViewModel(t.Abbrev, t.FullName)).ToList()))
            .ToList();
        foreach (var team in groups.SelectMany(g => g.Teams))
            _ = LoadLogoAsync(team, teams.Find(team.Abbrev)?.LogoUrl, logos);

        // Liiga teams (used when Liiga is switched on in General); their logos come with the game data.
        groups.Add(new TeamGroup("Liiga", LiigaTeams.All.Select(t => new TeamChoiceViewModel(t.Abbrev, t.Name)).ToList()));
        return groups;
    }

    internal static void SelectTeams(IEnumerable<TeamChoiceViewModel> teams, IEnumerable<string> selected)
    {
        var set = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams) team.IsSelected = set.Contains(team.Abbrev);
    }

    /// <summary>Selected teams, keeping entries that are not in the picker (e.g. hand-edited or other leagues).</summary>
    internal static List<string> SelectedTeams(IReadOnlyList<TeamChoiceViewModel> teams, IEnumerable<string> previous)
    {
        var offered = teams.Select(t => t.Abbrev).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return previous.Where(t => !offered.Contains(t))
            .Concat(teams.Where(t => t.IsSelected).Select(t => t.Abbrev))
            .ToList();
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
