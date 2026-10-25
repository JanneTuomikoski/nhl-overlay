using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>A player found by the nationality search: "Oliver Kapanen · MTL · born SWE".</summary>
public sealed record PlayerSearchResult(RosterPlayer Player, ICommand AddCommand)
{
    public string Label => $"{Player.FullName} · {Player.Team} · born {Player.Country}";
}

/// <summary>A manual nationality in the list: "Oliver Kapanen · MTL → FIN".</summary>
public sealed record CountryOverrideItem(CountryOverride Override, ICommand RemoveCommand)
{
    public string Label => $"{Override.Name} · {Override.Team} → {Override.Country}";
}

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
public sealed class PlayersSectionViewModel : ObservableObject, ISettingsSection, IDisposable
{
    private const int MaxSearchResults = 6;

    private readonly RosterService _rosters;
    private readonly Dispatcher _dispatcher;
    private readonly EventHandler _rostersChanged;
    private bool _requestedAllRosters;
    private string _searchText = "";
    private string _overrideCountry = "FIN";
    private IReadOnlyList<PlayerSearchResult> _searchResults = [];
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

    public PlayersSectionViewModel(RosterService rosters, Dispatcher dispatcher)
    {
        _rosters = rosters;
        _dispatcher = dispatcher;
        // Rosters requested for the search arrive in the background; refresh the results as they do.
        _rostersChanged = (_, _) => _dispatcher.InvokeAsync(RunSearch);
        rosters.Changed += _rostersChanged;
    }

    /// <summary>Manual nationalities (the roster only knows where a player was born).</summary>
    public ObservableCollection<CountryOverrideItem> Overrides { get; } = [];

    public bool HasOverrides => Overrides.Count > 0;

    /// <summary>The country a player added from the search counts for.</summary>
    public IReadOnlyList<ChoiceOption<string>> OverrideCountryOptions { get; } =
        HockeyCountries.All.Select(c => new ChoiceOption<string>(c.Code, c.Code)).ToList();

    public string OverrideCountry { get => _overrideCountry; set => SetProperty(ref _overrideCountry, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            if (!_requestedAllRosters && value.Trim().Length >= 2)
            {
                _requestedAllRosters = true;
                _rosters.EnsureAll(); // one request per team whose roster isn't cached yet
            }

            RunSearch();
        }
    }

    public IReadOnlyList<PlayerSearchResult> SearchResults
    {
        get => _searchResults;
        private set
        {
            if (SetProperty(ref _searchResults, value)) OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    private void RunSearch() =>
        SearchResults = _rosters.Search(SearchText, MaxSearchResults)
            .Select(p => new PlayerSearchResult(p, new RelayCommand(() => AddOverride(p))))
            .ToList();

    private void AddOverride(RosterPlayer player)
    {
        var existing = Overrides.FirstOrDefault(o => o.Override.PlayerId == player.Id);
        if (existing is not null) Overrides.Remove(existing);
        AddItem(new CountryOverride(player.Id, player.FullName, player.Team, OverrideCountry));
        SearchText = "";
    }

    private void AddItem(CountryOverride entry)
    {
        CountryOverrideItem? item = null;
        item = new CountryOverrideItem(entry, new RelayCommand(() =>
        {
            Overrides.Remove(item!);
            OnPropertyChanged(nameof(HasOverrides));
        }));
        Overrides.Add(item);
        OnPropertyChanged(nameof(HasOverrides));
    }

    public void Load(AppSettings settings)
    {
        var watch = settings.CountryWatch;
        CountryWatchEnabled = watch.Enabled;
        ShowOnCards = watch.ShowOnCards;
        ShowPanel = watch.ShowPanel;
        foreach (var country in Countries) country.IsSelected = watch.Countries.Contains(country.Code);
        Overrides.Clear();
        foreach (var entry in watch.Overrides) AddItem(entry);
        OverrideCountry = watch.Countries.FirstOrDefault() ?? "FIN";
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
        watch.Overrides = Overrides.Select(o => o.Override).ToList();
        var players = settings.PlayerWatch;
        players.ShutoutWatch = ShutoutWatch;
        players.Milestones = Milestones;
        players.PointStreaks = PointStreaks;
        players.MinStreak = (int)MinStreak;
        players.Scope = Scope;
    }

    public void Dispose() => _rosters.Changed -= _rostersChanged;
}
