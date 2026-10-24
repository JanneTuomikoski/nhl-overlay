using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

/// <summary>One period's shots as two bars.</summary>
public sealed record PeriodBar(string Label, int Away, int Home, double AwayHeight, double HomeHeight);

/// <summary>A shot placed on the 200 x 85 rink canvas.</summary>
public sealed record ShotDot(double Left, double Top, double Size, bool IsAway, bool IsGoal, bool IsMiss, string Description);

/// <summary>
/// The focus dashboard for one game: score and clock from the scoreboard, strength, shots per period, a shot
/// map and the event feed from the play-by-play. Respects the spoiler shield.
/// </summary>
public sealed class FocusViewModel : ObservableObject, IDisposable
{
    private const double RinkWidth = 200;
    private const double RinkHeight = 85;
    private const double BarMaxHeight = 44;

    private readonly FocusGameService _focus;
    private readonly GameStateService _state;
    private readonly SettingsService _settings;
    private readonly LogoService _logos;
    private readonly TeamDirectoryService _teams;
    private readonly SpoilerService _spoilers;
    private readonly Dispatcher _dispatcher;
    private readonly EventHandler<PlayByPlay> _onPlayByPlay;
    private readonly EventHandler<ScoreboardSnapshot> _onSnapshot;
    private readonly EventHandler _onSpoilers;

    private string _title = "";
    private string _awayAbbrev = "";
    private string _homeAbbrev = "";
    private string _awayName = "";
    private string _homeName = "";
    private ImageSource? _awayLogo;
    private ImageSource? _homeLogo;
    private string _awayScore = "";
    private string _homeScore = "";
    private string _awayShots = "";
    private string _homeShots = "";
    private string _statusText = "";
    private GameStatus _status;
    private string _strength = "";
    private string _strengthDetail = "";
    private bool _showStrength;
    private IReadOnlyList<PeriodBar> _periods = [];
    private IReadOnlyList<ShotDot> _shots = [];
    private IReadOnlyList<FeedItem> _feed = [];
    private string? _message = "Loading play-by-play...";
    private bool _isShielded;
    private string? _awayLogoUrl;
    private string? _homeLogoUrl;

    public FocusViewModel(
        FocusGameService focus,
        GameStateService state,
        SettingsService settings,
        LogoService logos,
        TeamDirectoryService teams,
        SpoilerService spoilers,
        Dispatcher dispatcher)
    {
        _focus = focus;
        _state = state;
        _settings = settings;
        _logos = logos;
        _teams = teams;
        _spoilers = spoilers;
        _dispatcher = dispatcher;
        RevealCommand = new RelayCommand(() =>
        {
            if (_focus.GameId is { } id) _spoilers.Reveal(id);
        });

        _onPlayByPlay = (_, _) => _dispatcher.InvokeAsync(Refresh);
        _onSnapshot = (_, _) => _dispatcher.InvokeAsync(Refresh);
        _onSpoilers = (_, _) => Refresh();
        focus.Updated += _onPlayByPlay;
        state.Changed += _onSnapshot;
        spoilers.Changed += _onSpoilers;
    }

    public ICommand RevealCommand { get; }

    public string Title { get => _title; private set => SetProperty(ref _title, value); }

    public string AwayAbbrev { get => _awayAbbrev; private set => SetProperty(ref _awayAbbrev, value); }

    public string HomeAbbrev { get => _homeAbbrev; private set => SetProperty(ref _homeAbbrev, value); }

    public string AwayName { get => _awayName; private set => SetProperty(ref _awayName, value); }

    public string HomeName { get => _homeName; private set => SetProperty(ref _homeName, value); }

    public ImageSource? AwayLogo { get => _awayLogo; private set => SetProperty(ref _awayLogo, value); }

    public ImageSource? HomeLogo { get => _homeLogo; private set => SetProperty(ref _homeLogo, value); }

    public string AwayScore { get => _awayScore; private set => SetProperty(ref _awayScore, value); }

    public string HomeScore { get => _homeScore; private set => SetProperty(ref _homeScore, value); }

    public string AwayShots { get => _awayShots; private set => SetProperty(ref _awayShots, value); }

    public string HomeShots { get => _homeShots; private set => SetProperty(ref _homeShots, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public GameStatus Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>"PP NJD · 5 on 4", "LAK goalie pulled · 6 on 5".</summary>
    public string Strength { get => _strength; private set => SetProperty(ref _strength, value); }

    /// <summary>"about 1:12 left" on a power play.</summary>
    public string StrengthDetail { get => _strengthDetail; private set => SetProperty(ref _strengthDetail, value); }

    public bool ShowStrength { get => _showStrength; private set => SetProperty(ref _showStrength, value); }

    public IReadOnlyList<PeriodBar> Periods { get => _periods; private set => SetProperty(ref _periods, value); }

    public IReadOnlyList<ShotDot> Shots { get => _shots; private set => SetProperty(ref _shots, value); }

    public IReadOnlyList<FeedItem> Feed { get => _feed; private set => SetProperty(ref _feed, value); }

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message is not null;

    public bool IsShielded
    {
        get => _isShielded;
        private set
        {
            if (SetProperty(ref _isShielded, value)) OnPropertyChanged(nameof(ShowDetails));
        }
    }

    public bool ShowDetails => !IsShielded;

    public bool AlwaysOnTop
    {
        get => _settings.Current.Focus.AlwaysOnTop;
        set => UpdateSetting(s => s.Focus.AlwaysOnTop = value);
    }

    public bool ShowShotsInFeed
    {
        get => _settings.Current.Focus.ShowShotsInFeed;
        set => UpdateSetting(s => s.Focus.ShowShotsInFeed = value);
    }

    public bool ShowHitsInFeed
    {
        get => _settings.Current.Focus.ShowHitsInFeed;
        set => UpdateSetting(s => s.Focus.ShowHitsInFeed = value);
    }

    public bool ShowMissedShots
    {
        get => _settings.Current.Focus.ShowMissedShotsOnMap;
        set => UpdateSetting(s => s.Focus.ShowMissedShotsOnMap = value);
    }

    /// <summary>Rebuilds everything from the latest scoreboard and play-by-play. UI thread.</summary>
    public void Refresh()
    {
        if (_focus.GameId is not { } id) return;
        var game = _state.Current.GamesByDate.Values.SelectMany(g => g).Concat(_state.Current.Games).FirstOrDefault(g => g.Id == id);
        var pbp = _focus.Latest;
        var culture = CultureInfo.CurrentCulture;

        if (game is not null) UpdateHeader(game, culture);
        IsShielded = game is not null && _spoilers.IsShielded(game);
        if (IsShielded)
        {
            AwayScore = HomeScore = AwayShots = HomeShots = "";
            StatusText = "Score hidden";
            ShowStrength = false;
            Periods = [];
            Shots = [];
            Feed = [];
            Message = "This game is hidden (watch later). Reveal it to see the dashboard.";
            return;
        }

        if (pbp is null)
        {
            Message = _focus.LastError is { } error ? $"Play-by-play unavailable: {error}" : "Loading play-by-play...";
            return;
        }

        Message = pbp.Plays.Count == 0 ? "No events yet. The feed starts at puck drop." : null;
        var focus = _settings.Current.Focus;
        UpdateStrength(pbp, game);

        var periods = FocusAnalysis.ShotsByPeriod(pbp);
        var max = Math.Max(1, periods.Select(p => Math.Max(p.Away, p.Home)).DefaultIfEmpty(1).Max());
        Periods = periods.Select(p => new PeriodBar(p.Label, p.Away, p.Home, BarMaxHeight * p.Away / max, BarMaxHeight * p.Home / max)).ToList();

        Shots = FocusAnalysis.ShotMarks(pbp, focus.ShowMissedShotsOnMap).Select(m =>
        {
            var size = m.IsGoal ? 6 : 3.5;
            return new ShotDot((RinkWidth / 2) + m.X - (size / 2), (RinkHeight / 2) - m.Y - (size / 2), size, m.IsAway, m.IsGoal, m.IsMiss, m.Description);
        }).ToList();

        Feed = FocusAnalysis.Feed(pbp, focus.ShowShotsInFeed, focus.ShowHitsInFeed);
    }

    private void UpdateHeader(HockeyGame game, CultureInfo culture)
    {
        AwayAbbrev = game.Away.Abbrev;
        HomeAbbrev = game.Home.Abbrev;
        AwayName = _teams.Find(game.Away.Abbrev)?.FullName ?? game.Away.Name;
        HomeName = _teams.Find(game.Home.Abbrev)?.FullName ?? game.Home.Name;
        Title = $"NHL Focus · {game.Away.Abbrev} @ {game.Home.Abbrev}";
        AwayScore = game.HasScore && !game.Status.IsUpcoming() ? game.Away.Score!.Value.ToString(culture) : "";
        HomeScore = game.HasScore && !game.Status.IsUpcoming() ? game.Home.Score!.Value.ToString(culture) : "";
        AwayShots = game.Away.ShotsOnGoal is { } a ? $"{a} SOG" : "";
        HomeShots = game.Home.ShotsOnGoal is { } h ? $"{h} SOG" : "";
        Status = game.Status;
        StatusText = GameStatusFormatter.FormatStatus(game, TimeZoneInfo.Local, DateTimeOffset.UtcNow, culture);

        var awayUrl = _teams.Find(game.Away.Abbrev)?.LogoUrl ?? game.Away.LogoUrl;
        var homeUrl = _teams.Find(game.Home.Abbrev)?.LogoUrl ?? game.Home.LogoUrl;
        if (awayUrl != _awayLogoUrl) _ = LoadLogoAsync(_awayLogoUrl = awayUrl, logo => AwayLogo = logo);
        if (homeUrl != _homeLogoUrl) _ = LoadLogoAsync(_homeLogoUrl = homeUrl, logo => HomeLogo = logo);
    }

    private void UpdateStrength(PlayByPlay pbp, HockeyGame? game)
    {
        var live = game?.Status is GameStatus.Live or GameStatus.Overtime;
        var last = pbp.Plays.LastOrDefault(p => p.SituationCode.Length == 4);
        var situation = Situation.Parse(last?.SituationCode);
        Strength = FocusAnalysis.StrengthText(situation, pbp.AwayAbbrev, pbp.HomeAbbrev);
        ShowStrength = live && Strength != "Even strength";
        var remaining = live && game?.Period is { } period && game.SecondsRemaining is { } seconds
            ? FocusAnalysis.PowerPlayRemaining(pbp, situation, period, seconds, game.MaxRegulationPeriods)
            : null;
        StrengthDetail = remaining is { } r ? $"about {(int)r.TotalMinutes}:{r.Seconds:00} left" : "";
    }

    private void UpdateSetting(Action<AppSettings> change)
    {
        _settings.Update(change);
        OnPropertyChanged(string.Empty);
        Refresh();
    }

    private async Task LoadLogoAsync(string? url, Action<ImageSource?> set)
    {
        try
        {
            set(await _logos.GetLogoAsync(url));
        }
        catch (Exception ex)
        {
            Log.Warn($"Focus logo failed for {url}.", ex);
        }
    }

    public void Dispose()
    {
        _focus.Updated -= _onPlayByPlay;
        _state.Changed -= _onSnapshot;
        _spoilers.Changed -= _onSpoilers;
    }
}
