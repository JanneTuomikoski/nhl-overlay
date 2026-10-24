using System.Globalization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.ViewModels;

/// <summary>One game card. Updated in place on every refresh so the UI doesn't rebuild or flicker.</summary>
public sealed class GameCardViewModel(long id) : ObservableObject
{
    private string _awayAbbrev = "";
    private string _homeAbbrev = "";
    private string _awayScore = "";
    private string _homeScore = "";
    private bool _hasScore;
    private bool _awayLeading;
    private bool _homeLeading;
    private string _statusText = "";
    private GameStatus _status;
    private string _shotsText = "";
    private bool _showShots;
    private string _toolTip = "";

    public long Id { get; } = id;

    public string AwayAbbrev { get => _awayAbbrev; private set => SetProperty(ref _awayAbbrev, value); }

    public string HomeAbbrev { get => _homeAbbrev; private set => SetProperty(ref _homeAbbrev, value); }

    public string AwayScore { get => _awayScore; private set => SetProperty(ref _awayScore, value); }

    public string HomeScore { get => _homeScore; private set => SetProperty(ref _homeScore, value); }

    /// <summary>False before puck drop: the card shows "AWY @ HOM" instead of a score.</summary>
    public bool HasScore { get => _hasScore; private set => SetProperty(ref _hasScore, value); }

    public bool AwayLeading { get => _awayLeading; private set => SetProperty(ref _awayLeading, value); }

    public bool HomeLeading { get => _homeLeading; private set => SetProperty(ref _homeLeading, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public GameStatus Status { get => _status; private set => SetProperty(ref _status, value); }

    public string ShotsText { get => _shotsText; private set => SetProperty(ref _shotsText, value); }

    public bool ShowShots { get => _showShots; private set => SetProperty(ref _showShots, value); }

    public string ToolTip { get => _toolTip; private set => SetProperty(ref _toolTip, value); }

    public void Update(HockeyGame game, AppearanceSettings appearance, DateTimeOffset now, TimeZoneInfo localZone, CultureInfo culture)
    {
        AwayAbbrev = game.Away.Abbrev;
        HomeAbbrev = game.Home.Abbrev;
        Status = game.Status;
        StatusText = GameStatusFormatter.FormatStatus(game, localZone, now, culture, appearance.ShowGameClock);

        HasScore = game.HasScore && !game.Status.IsUpcoming() && !game.Status.IsInactive();
        AwayScore = HasScore ? game.Away.Score!.Value.ToString(culture) : "";
        HomeScore = HasScore ? game.Home.Score!.Value.ToString(culture) : "";
        AwayLeading = HasScore && game.Away.Score > game.Home.Score;
        HomeLeading = HasScore && game.Home.Score > game.Away.Score;

        var hasShots = game.Away.ShotsOnGoal.HasValue && game.Home.ShotsOnGoal.HasValue && HasScore;
        ShowShots = appearance.ShowShotsOnGoal && hasShots;
        ShotsText = hasShots ? $"SOG {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : "";

        var localStart = TimeZoneInfo.ConvertTime(game.StartTimeUtc, localZone);
        ToolTip = string.Join(Environment.NewLine, new[]
        {
            $"{game.Away.Name} at {game.Home.Name}",
            $"Start: {localStart.ToString("f", culture)}",
            hasShots ? $"Shots on goal: {game.Away.ShotsOnGoal} - {game.Home.ShotsOnGoal}" : null,
        }.Where(line => line is not null));
    }
}
