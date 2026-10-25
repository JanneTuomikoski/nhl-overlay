using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

/// <summary>One line of a game's goal list: "2ND 08:14  EDM  C. McDavid  PP  2-1".</summary>
public sealed record GoalLine(string Period, string Time, string Team, string Scorer, string Tags, string Score);

/// <summary>Everything a card needs besides the game itself; built once per refresh.</summary>
public sealed record CardContext(
    AppSettings Settings,
    IReadOnlySet<string> Favorites,
    Func<string, TeamStanding?> FindTeam,
    IReadOnlyDictionary<long, IReadOnlyList<PlayerGameGoals>> HatTricks,
    DateTimeOffset Now,
    TimeZoneInfo LocalZone,
    CultureInfo Culture)
{
    /// <summary>Watched-country players per game; empty when country watch is off.</summary>
    public Func<HockeyGame, IReadOnlyList<WatchedPlayer>> WatchedPlayers { get; init; } = _ => [];

    /// <summary>Spoiler shield: the game's score and details must stay hidden.</summary>
    public Func<HockeyGame, bool> IsShielded { get; init; } = _ => false;

    /// <summary>Shutout watch and milestone lines for a game.</summary>
    public Func<HockeyGame, IReadOnlyList<string>> WatchLines { get; init; } = _ => [];

    public AppearanceSettings Appearance => Settings.Appearance;

    /// <summary>Hat-Trick Watch players for a game, honoring the enable, scope and spoiler settings.</summary>
    public IReadOnlyList<PlayerGameGoals> HatTricksFor(HockeyGame game)
    {
        var settings = Settings.HatTrick;
        if (!settings.Enabled || IsShielded(game) || !HatTricks.TryGetValue(game.Id, out var players)) return [];
        if (settings.Scope == TeamScope.FavoritesOnly && !GameOrdering.IsFavorite(game, Favorites)) return [];
        // A two-goal "watch" only means something while the game can still produce a third goal;
        // completed hat tricks stay visible for the rest of the game day.
        return game.Status.IsInProgress() ? players : players.Where(p => p.IsHatTrick).ToList();
    }
}

public enum HatTrickState
{
    None,
    Watch,
    Completed,
}

/// <summary>One game card. Updated in place on every refresh so the UI doesn't rebuild or flicker.</summary>
public sealed class GameCardViewModel : ObservableObject
{
    private readonly LogoService _logos;
    private string _awayAbbrev = "";
    private string _homeAbbrev = "";
    private string _awayName = "";
    private string _homeName = "";
    private string _awayShortName = "";
    private string _homeShortName = "";
    private string _awayScore = "";
    private string _homeScore = "";
    private string _awayShots = "";
    private string _homeShots = "";
    private bool _hasScore;
    private bool _awayLeading;
    private bool _homeLeading;
    private string _statusText = "";
    private GameStatus _status;
    private string _shotsText = "";
    private bool _hasShots;
    private bool _showShots;
    private string _toolTip = "";
    private IReadOnlyList<GoalLine> _goals = [];
    private bool _isDetailsOpen;
    private string? _gameCenterUrl;
    private string? _awayLogoUrl;
    private string? _homeLogoUrl;
    private ImageSource? _awayLogo;
    private ImageSource? _homeLogo;
    private bool _showLogos;
    private bool _isFavorite;
    private bool _showFavoriteBar;
    private bool _showFavoriteTint;
    private HatTrickState _hatTrickState;
    private bool _highlightHatTrick;
    private string _hatTrickText = "";
    private string _hatTrickTextLong = "";
    private string _countryText = "";
    private string _countryTextLong = "";
    private string _watchText = "";
    private bool _isShielded;

    /// <param name="id">NHL game id.</param>
    /// <param name="logos">Logo source.</param>
    /// <param name="setShielded">Spoiler shield action: (gameId, hide) where hide=false reveals the score.</param>
    /// <param name="openFocus">Opens the focus dashboard for a game.</param>
    /// <param name="toggleSection">Collapses or expands a section by its key ("NHL", "LIIGA FINAL", ...).</param>
    public GameCardViewModel(long id, LogoService logos, Action<long, bool>? setShielded = null, Action<long>? openFocus = null,
        Action<string>? toggleSection = null)
    {
        Id = id;
        _logos = logos;
        ToggleDetailsCommand = new RelayCommand(() => IsDetailsOpen = !IsDetailsOpen);
        ToggleSectionCommand = new RelayCommand(() =>
        {
            if (SectionKey is { } key) toggleSection?.Invoke(key);
        }, () => toggleSection is not null);
        OpenGameCenterCommand = new RelayCommand(OpenGameCenter, () => _gameCenterUrl is not null);
        ToggleShieldCommand = new RelayCommand(() => setShielded?.Invoke(Id, !IsShielded), () => setShielded is not null);
        FocusCommand = new RelayCommand(() => openFocus?.Invoke(Id), () => openFocus is not null && _isNhl);
    }

    private bool _isNhl = true;
    private string? _sectionTitle;

    /// <summary>The section this card belongs to ("NHL", "NHL FINAL", "LIIGA", "LIIGA FINAL").</summary>
    public string? SectionKey { get; set; }

    /// <summary>"NHL" / "LIIGA" / "FINAL" above the first card of a titled section.</summary>
    public string? SectionTitle
    {
        get => _sectionTitle;
        set
        {
            if (SetProperty(ref _sectionTitle, value)) OnPropertyChanged(nameof(HasSectionTitle));
        }
    }

    public bool HasSectionTitle => SectionTitle is not null;

    private bool _isSectionCollapsed;
    private string _sectionSummary = "";

    /// <summary>The user collapsed this card's section (or its league); only the section header (on the first card) stays visible.</summary>
    public bool IsSectionCollapsed
    {
        get => _isSectionCollapsed;
        set
        {
            if (SetProperty(ref _isSectionCollapsed, value)) OnPropertyChanged(nameof(IsBodyVisible));
        }
    }

    public bool IsBodyVisible => !IsSectionCollapsed;

    /// <summary>"3 games · 1 live" next to a collapsed section's title.</summary>
    public string SectionSummary { get => _sectionSummary; set => SetProperty(ref _sectionSummary, value); }

    public ICommand ToggleSectionCommand { get; }

    /// <summary>Opens this game in the focus dashboard window.</summary>
    public ICommand FocusCommand { get; }

    public long Id { get; }

    public ICommand ToggleDetailsCommand { get; }

    public ICommand OpenGameCenterCommand { get; }

    /// <summary>Hides the score ("watch later") or reveals it.</summary>
    public ICommand ToggleShieldCommand { get; }

    /// <summary>Spoiler shield is hiding this game's score.</summary>
    public bool IsShielded
    {
        get => _isShielded;
        private set
        {
            if (SetProperty(ref _isShielded, value)) OnPropertyChanged(nameof(ShieldActionLabel));
        }
    }

    public string ShieldActionLabel => IsShielded ? "Reveal score" : "Hide score (watch later)";

    public string AwayAbbrev { get => _awayAbbrev; private set => SetProperty(ref _awayAbbrev, value); }

    public string HomeAbbrev { get => _homeAbbrev; private set => SetProperty(ref _homeAbbrev, value); }

    public string AwayName { get => _awayName; private set => SetProperty(ref _awayName, value); }

    public string HomeName { get => _homeName; private set => SetProperty(ref _homeName, value); }

    /// <summary>Common name ("Avalanche") for narrow layouts.</summary>
    public string AwayShortName { get => _awayShortName; private set => SetProperty(ref _awayShortName, value); }

    public string HomeShortName { get => _homeShortName; private set => SetProperty(ref _homeShortName, value); }

    public ImageSource? AwayLogo { get => _awayLogo; private set => SetProperty(ref _awayLogo, value); }

    public ImageSource? HomeLogo { get => _homeLogo; private set => SetProperty(ref _homeLogo, value); }

    public bool ShowLogos { get => _showLogos; private set => SetProperty(ref _showLogos, value); }

    public bool IsFavorite { get => _isFavorite; private set => SetProperty(ref _isFavorite, value); }

    /// <summary>Accent bar on the left edge of a favorite team's card.</summary>
    public bool ShowFavoriteBar { get => _showFavoriteBar; private set => SetProperty(ref _showFavoriteBar, value); }

    /// <summary>Tinted background for a favorite team's card.</summary>
    public bool ShowFavoriteTint { get => _showFavoriteTint; private set => SetProperty(ref _showFavoriteTint, value); }

    public HatTrickState HatTrickState
    {
        get => _hatTrickState;
        private set
        {
            if (SetProperty(ref _hatTrickState, value)) OnPropertyChanged(nameof(HasHatTrick));
        }
    }

    public bool HasHatTrick => HatTrickState != HatTrickState.None;

    /// <summary>Amber card highlight (setting), shown for a watch or a completed hat trick.</summary>
    public bool HighlightHatTrick { get => _highlightHatTrick; private set => SetProperty(ref _highlightHatTrick, value); }

    /// <summary>Compact text, e.g. "HAT-TRICK WATCH  C. McDavid (2)"; one line per player.</summary>
    public string HatTrickText { get => _hatTrickText; private set => SetProperty(ref _hatTrickText, value); }

    /// <summary>Expanded text, e.g. "HAT-TRICK WATCH  Connor McDavid · 2 goals".</summary>
    public string HatTrickTextLong { get => _hatTrickTextLong; private set => SetProperty(ref _hatTrickTextLong, value); }

    /// <summary>Compact: watched players with points tonight, e.g. "FIN  Barkov 1+1 · Saros 24/26".</summary>
    public string CountryText
    {
        get => _countryText;
        private set
        {
            if (SetProperty(ref _countryText, value)) OnPropertyChanged(nameof(HasCountryText));
        }
    }

    public bool HasCountryText => CountryText.Length > 0;

    /// <summary>Expanded: every watched player in the game, with stats where there are any.</summary>
    public string CountryTextLong
    {
        get => _countryTextLong;
        private set
        {
            if (SetProperty(ref _countryTextLong, value)) OnPropertyChanged(nameof(HasCountryTextLong));
        }
    }

    public bool HasCountryTextLong => CountryTextLong.Length > 0;

    /// <summary>"SHUTOUT WATCH  J. Saros (NSH)", "C. McDavid 1 goal from 500"; one per line.</summary>
    public string WatchText
    {
        get => _watchText;
        private set
        {
            if (SetProperty(ref _watchText, value)) OnPropertyChanged(nameof(HasWatchText));
        }
    }

    public bool HasWatchText => WatchText.Length > 0;

    public string AwayScore { get => _awayScore; private set => SetProperty(ref _awayScore, value); }

    public string HomeScore { get => _homeScore; private set => SetProperty(ref _homeScore, value); }

    /// <summary>False before puck drop: the card shows "AWY @ HOM" instead of a score.</summary>
    public bool HasScore { get => _hasScore; private set => SetProperty(ref _hasScore, value); }

    public bool AwayLeading { get => _awayLeading; private set => SetProperty(ref _awayLeading, value); }

    public bool HomeLeading { get => _homeLeading; private set => SetProperty(ref _homeLeading, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public GameStatus Status { get => _status; private set => SetProperty(ref _status, value); }

    public string ShotsText { get => _shotsText; private set => SetProperty(ref _shotsText, value); }

    public string AwayShots { get => _awayShots; private set => SetProperty(ref _awayShots, value); }

    public string HomeShots { get => _homeShots; private set => SetProperty(ref _homeShots, value); }

    public bool HasShots { get => _hasShots; private set => SetProperty(ref _hasShots, value); }

    /// <summary>Shots line in compact mode (opt-in setting). Expanded mode always shows shots.</summary>
    public bool ShowShots { get => _showShots; private set => SetProperty(ref _showShots, value); }

    public string ToolTip { get => _toolTip; private set => SetProperty(ref _toolTip, value); }

    /// <summary>Goals in scoring order (shootout goal last, if any).</summary>
    public IReadOnlyList<GoalLine> Goals
    {
        get => _goals;
        private set
        {
            if (_goals.SequenceEqual(value)) return;
            _goals = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGoals));
        }
    }

    public bool HasGoals => _goals.Count > 0;

    /// <summary>Compact mode: the goal list opens below the card when it is clicked.</summary>
    public bool IsDetailsOpen { get => _isDetailsOpen; set => SetProperty(ref _isDetailsOpen, value); }

    public void Update(HockeyGame game, CardContext context)
    {
        var appearance = context.Appearance;
        var culture = context.Culture;
        _isNhl = game.IsNhl; // the focus dashboard uses NHL play-by-play
        AwayAbbrev = game.Away.Abbrev;
        HomeAbbrev = game.Home.Abbrev;
        AwayName = TeamName(game.Away, context);
        HomeName = TeamName(game.Home, context);
        AwayShortName = ShortTeamName(game.Away, context);
        HomeShortName = ShortTeamName(game.Home, context);
        IsShielded = context.IsShielded(game);
        // A shielded game only reveals whether it is still going: no score, period, overtime or goals.
        var revealDetails = !IsShielded || game.Status.IsUpcoming() || game.Status.IsInactive();
        Status = revealDetails ? game.Status : game.Status.IsInProgress() ? GameStatus.Live : GameStatus.Final;
        StatusText = revealDetails
            ? GameStatusFormatter.FormatStatus(game, context.LocalZone, context.Now, culture, appearance.ShowGameClock)
            : game.Status.IsInProgress() ? "LIVE · HIDDEN" : "HIDDEN";

        IsFavorite = GameOrdering.IsFavorite(game, context.Favorites);
        ShowFavoriteBar = IsFavorite && context.Settings.Teams.Highlight is FavoriteHighlight.Bar or FavoriteHighlight.BarAndTint;
        ShowFavoriteTint = IsFavorite && context.Settings.Teams.Highlight == FavoriteHighlight.BarAndTint;
        UpdateHatTrick(context.HatTricksFor(game), context.Settings.HatTrick);
        UpdateCountry(context.Settings.CountryWatch.ShowOnCards && !IsShielded ? context.WatchedPlayers(game) : []);
        WatchText = IsShielded ? "" : string.Join(Environment.NewLine, context.WatchLines(game));
        _gameCenterUrl = game.GameCenterUrl;

        HasScore = game.HasScore && !game.Status.IsUpcoming() && !game.Status.IsInactive() && !IsShielded;
        AwayScore = HasScore ? game.Away.Score!.Value.ToString(culture) : "";
        HomeScore = HasScore ? game.Home.Score!.Value.ToString(culture) : "";
        AwayLeading = HasScore && game.Away.Score > game.Home.Score;
        HomeLeading = HasScore && game.Home.Score > game.Away.Score;

        HasShots = game.Away.ShotsOnGoal.HasValue && game.Home.ShotsOnGoal.HasValue && HasScore;
        ShowShots = appearance.ShowShotsOnGoal && HasShots;
        ShotsText = HasShots ? $"SOG {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : "";
        AwayShots = HasShots ? game.Away.ShotsOnGoal!.Value.ToString(culture) : "";
        HomeShots = HasShots ? game.Home.ShotsOnGoal!.Value.ToString(culture) : "";

        Goals = IsShielded ? [] : game.Goals.Select(g => ToGoalLine(g, game.MaxRegulationPeriods)).ToList();

        ShowLogos = appearance.ShowTeamLogos;
        if (ShowLogos)
        {
            // A missing logo is asked for again; the service retries failed downloads after a while.
            if (game.Away.LogoUrl != _awayLogoUrl || AwayLogo is null) _ = LoadLogoAsync(_awayLogoUrl = game.Away.LogoUrl, away: true);
            if (game.Home.LogoUrl != _homeLogoUrl || HomeLogo is null) _ = LoadLogoAsync(_homeLogoUrl = game.Home.LogoUrl, away: false);
        }

        var localStart = TimeZoneInfo.ConvertTime(game.StartTimeUtc, context.LocalZone);
        ToolTip = string.Join(Environment.NewLine, new[]
        {
            $"{AwayName} at {HomeName}",
            $"Start: {localStart.ToString("f", culture)}",
            HasShots ? $"Shots on goal: {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : null,
            IsShielded ? "Score hidden (watch later). Click, then Reveal score." : "Click for details",
        }.Where(line => line is not null));
    }

    private void UpdateHatTrick(IReadOnlyList<PlayerGameGoals> players, HatTrickSettings settings)
    {
        HatTrickState = players.Any(p => p.IsHatTrick) ? HatTrickState.Completed
            : players.Any(p => p.IsWatch) ? HatTrickState.Watch
            : HatTrickState.None;
        HighlightHatTrick = HasHatTrick && settings.HighlightCards;

        if (!settings.ShowPlayerName)
        {
            HatTrickText = HatTrickTextLong = HatTrickState switch
            {
                HatTrickState.Completed => "HAT TRICK!",
                HatTrickState.Watch => "HAT-TRICK WATCH",
                _ => "",
            };
            return;
        }

        static string Label(PlayerGameGoals p) => p.IsHatTrick ? "HAT TRICK!" : "HAT-TRICK WATCH";
        HatTrickText = string.Join(Environment.NewLine, players.Select(p => $"{Label(p)}  {p.ShortName} ({p.Goals})"));
        HatTrickTextLong = string.Join(Environment.NewLine, players.Select(p => $"{Label(p)}  {p.Name} · {p.Goals} goals"));
    }

    private void UpdateCountry(IReadOnlyList<WatchedPlayer> players)
    {
        static string Join(IEnumerable<WatchedPlayer> group, Func<WatchedPlayer, string> format) =>
            string.Join("   ", group.GroupBy(p => p.Country).Select(c => $"{c.Key}  {string.Join(" · ", c.Select(format))}"));

        CountryText = Join(players.Where(p => p.HasStat), p => $"{p.LastName} {p.Stat}");
        CountryTextLong = Join(players, p => p.HasStat ? $"{p.ShortName} {p.Stat}" : p.ShortName);
    }

    /// <summary>Full name from the team directory ("Los Angeles Kings"), else what the scoreboard sent.</summary>
    private static string TeamName(HockeyTeam team, CardContext context)
    {
        var known = context.FindTeam(team.Abbrev)?.FullName;
        if (!string.IsNullOrWhiteSpace(known)) return known;
        return string.IsNullOrWhiteSpace(team.Name) ? team.Abbrev : team.Name;
    }

    private static string ShortTeamName(HockeyTeam team, CardContext context)
    {
        var known = context.FindTeam(team.Abbrev)?.CommonName;
        if (!string.IsNullOrWhiteSpace(known)) return known;
        return string.IsNullOrWhiteSpace(team.Name) ? team.Abbrev : team.Name;
    }

    internal static GoalLine ToGoalLine(GameGoal goal, int maxRegulationPeriods)
    {
        var tags = new List<string>(2);
        if (string.Equals(goal.Strength, "pp", StringComparison.OrdinalIgnoreCase)) tags.Add("PP");
        else if (string.Equals(goal.Strength, "sh", StringComparison.OrdinalIgnoreCase)) tags.Add("SH");
        if (goal.IsEmptyNet) tags.Add("EN");

        return new GoalLine(
            GameStatusFormatter.PeriodLabel(goal.Period, goal.PeriodType, maxRegulationPeriods),
            goal.IsShootout ? "" : goal.TimeInPeriod,
            goal.TeamAbbrev,
            goal.ScorerShortName,
            string.Join(" ", tags),
            $"{goal.AwayScore}-{goal.HomeScore}");
    }

    private async Task LoadLogoAsync(string? url, bool away)
    {
        ImageSource? image;
        try
        {
            image = await _logos.GetLogoAsync(url);
        }
        catch (Exception ex)
        {
            Log.Warn($"Logo load failed for {url}.", ex);
            image = null;
        }

        // Ignore a result that arrived after the team's logo URL changed.
        if (away && url == _awayLogoUrl) AwayLogo = image;
        else if (!away && url == _homeLogoUrl) HomeLogo = image;
    }

    private void OpenGameCenter()
    {
        if (_gameCenterUrl is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_gameCenterUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the NHL game page.", ex);
        }
    }
}
