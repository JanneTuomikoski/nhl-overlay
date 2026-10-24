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

/// <summary>One game card. Updated in place on every refresh so the UI doesn't rebuild or flicker.</summary>
public sealed class GameCardViewModel : ObservableObject
{
    private readonly LogoService _logos;
    private string _awayAbbrev = "";
    private string _homeAbbrev = "";
    private string _awayName = "";
    private string _homeName = "";
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

    public GameCardViewModel(long id, LogoService logos)
    {
        Id = id;
        _logos = logos;
        ToggleDetailsCommand = new RelayCommand(() => IsDetailsOpen = !IsDetailsOpen);
        OpenGameCenterCommand = new RelayCommand(OpenGameCenter, () => _gameCenterUrl is not null);
    }

    public long Id { get; }

    public ICommand ToggleDetailsCommand { get; }

    public ICommand OpenGameCenterCommand { get; }

    public string AwayAbbrev { get => _awayAbbrev; private set => SetProperty(ref _awayAbbrev, value); }

    public string HomeAbbrev { get => _homeAbbrev; private set => SetProperty(ref _homeAbbrev, value); }

    public string AwayName { get => _awayName; private set => SetProperty(ref _awayName, value); }

    public string HomeName { get => _homeName; private set => SetProperty(ref _homeName, value); }

    public ImageSource? AwayLogo { get => _awayLogo; private set => SetProperty(ref _awayLogo, value); }

    public ImageSource? HomeLogo { get => _homeLogo; private set => SetProperty(ref _homeLogo, value); }

    public bool ShowLogos { get => _showLogos; private set => SetProperty(ref _showLogos, value); }

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

    public void Update(HockeyGame game, AppearanceSettings appearance, DateTimeOffset now, TimeZoneInfo localZone, CultureInfo culture)
    {
        AwayAbbrev = game.Away.Abbrev;
        HomeAbbrev = game.Home.Abbrev;
        AwayName = string.IsNullOrWhiteSpace(game.Away.Name) ? game.Away.Abbrev : game.Away.Name;
        HomeName = string.IsNullOrWhiteSpace(game.Home.Name) ? game.Home.Abbrev : game.Home.Name;
        Status = game.Status;
        StatusText = GameStatusFormatter.FormatStatus(game, localZone, now, culture, appearance.ShowGameClock);
        _gameCenterUrl = game.GameCenterUrl;

        HasScore = game.HasScore && !game.Status.IsUpcoming() && !game.Status.IsInactive();
        AwayScore = HasScore ? game.Away.Score!.Value.ToString(culture) : "";
        HomeScore = HasScore ? game.Home.Score!.Value.ToString(culture) : "";
        AwayLeading = HasScore && game.Away.Score > game.Home.Score;
        HomeLeading = HasScore && game.Home.Score > game.Away.Score;

        HasShots = game.Away.ShotsOnGoal.HasValue && game.Home.ShotsOnGoal.HasValue && HasScore;
        ShowShots = appearance.ShowShotsOnGoal && HasShots;
        ShotsText = HasShots ? $"SOG {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : "";
        AwayShots = HasShots ? game.Away.ShotsOnGoal!.Value.ToString(culture) : "";
        HomeShots = HasShots ? game.Home.ShotsOnGoal!.Value.ToString(culture) : "";

        Goals = game.Goals.Select(g => ToGoalLine(g, game.MaxRegulationPeriods)).ToList();

        ShowLogos = appearance.ShowTeamLogos;
        if (ShowLogos)
        {
            if (game.Away.LogoUrl != _awayLogoUrl) _ = LoadLogoAsync(_awayLogoUrl = game.Away.LogoUrl, away: true);
            if (game.Home.LogoUrl != _homeLogoUrl) _ = LoadLogoAsync(_homeLogoUrl = game.Home.LogoUrl, away: false);
        }

        var localStart = TimeZoneInfo.ConvertTime(game.StartTimeUtc, localZone);
        ToolTip = string.Join(Environment.NewLine, new[]
        {
            $"{AwayName} at {HomeName}",
            $"Start: {localStart.ToString("f", culture)}",
            HasShots ? $"Shots on goal: {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : null,
            HasGoals ? "Click for goal details" : null,
        }.Where(line => line is not null));
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
