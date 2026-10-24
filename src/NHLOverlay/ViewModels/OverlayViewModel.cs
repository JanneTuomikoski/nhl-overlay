using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

/// <summary>One line of the watched-country panel: "A. Barkov  FLA  1+1".</summary>
public sealed record CountryPanelLine(string Name, string Team, string Stat);

public enum ConnectionState
{
    Connecting,
    Ok,
    Stale,
    Error,
}

public sealed class OverlayViewModel : ObservableObject
{
    public const int MaxDaysBack = 7;
    public const int MaxDaysForward = 1;

    /// <summary>Browsing other days returns to today automatically so live scores are never hidden for long.</summary>
    private static readonly TimeSpan ReturnToTodayAfter = TimeSpan.FromMinutes(2);

    private readonly SettingsService _settings;
    private readonly LogoService _logos;
    private readonly TeamDirectoryService _teams;
    private readonly RuntimeState _runtime;
    private readonly CountryWatchService _countryWatch;
    private readonly Action<DateOnly?> _requestDate;
    private readonly DispatcherTimer _returnToToday;
    private readonly Dictionary<long, GameCardViewModel> _cards = [];
    private ScoreboardSnapshot _snapshot = ScoreboardSnapshot.Empty;
    private int _dayOffset;
    private double _backgroundOpacity;
    private double _uiScale;
    private bool _isInteractive;
    private bool _isExpanded;
    private bool _isLocked;
    private bool _toastsAtBottom;
    private bool _isDoNotDisturb;
    private bool _isGaming;
    private ConnectionState _connection;
    private string _statusText = "";
    private string _statusToolTip = "";
    private string _dayLabel = "TODAY";
    private string? _message;
    private bool _showCountryPanel;
    private string _countryPanelTitle = "";
    private string _countryPanelFooter = "";
    private IReadOnlyList<CountryPanelLine> _countryPanelLines = [];

    public OverlayViewModel(
        GameStateService state,
        SettingsService settings,
        LogoService logos,
        TeamDirectoryService teams,
        RuntimeState runtime,
        ToastService toasts,
        CountryWatchService countryWatch,
        Action<DateOnly?> requestDate,
        Dispatcher dispatcher)
    {
        _settings = settings;
        _logos = logos;
        _teams = teams;
        _runtime = runtime;
        _countryWatch = countryWatch;
        Toasts = toasts.Toasts;
        teams.Changed += (_, _) => dispatcher.InvokeAsync(() => Apply(_snapshot));
        countryWatch.Changed += (_, _) => dispatcher.InvokeAsync(() => Apply(_snapshot));
        runtime.Changed += (_, _) =>
        {
            ApplySettings();
            Apply(_snapshot);
        };
        _requestDate = requestDate;
        _returnToToday = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = ReturnToTodayAfter };
        _returnToToday.Tick += (_, _) => SetDayOffset(0);

        PreviousDayCommand = new RelayCommand(() => SetDayOffset(_dayOffset - 1), () => _dayOffset > -MaxDaysBack);
        NextDayCommand = new RelayCommand(() => SetDayOffset(_dayOffset + 1), () => _dayOffset < MaxDaysForward);
        TodayCommand = new RelayCommand(() => SetDayOffset(0), () => _dayOffset != 0);
        ToggleDisplayModeCommand = new RelayCommand(() => settings.Update(s =>
            s.Appearance.DisplayMode = s.Appearance.DisplayMode == DisplayMode.Compact ? DisplayMode.Expanded : DisplayMode.Compact));

        state.Changed += (_, snapshot) => dispatcher.InvokeAsync(() => Apply(snapshot));
        settings.Changed += (_, _) => dispatcher.InvokeAsync(() =>
        {
            ApplySettings();
            Apply(_snapshot);
        });
        ApplySettings();
        Apply(state.Current);
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    /// <summary>Notification toasts shown inside the overlay (newest first).</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; }

    public bool ToastsAtBottom { get => _toastsAtBottom; private set => SetProperty(ref _toastsAtBottom, value); }

    /// <summary>Header badge: alerts are being held.</summary>
    public bool IsDoNotDisturb { get => _isDoNotDisturb; private set => SetProperty(ref _isDoNotDisturb, value); }

    /// <summary>Header badge: the gaming profile is active.</summary>
    public bool IsGaming { get => _isGaming; private set => SetProperty(ref _isGaming, value); }

    public ICommand PreviousDayCommand { get; }

    public ICommand NextDayCommand { get; }

    public ICommand TodayCommand { get; }

    public ICommand ToggleDisplayModeCommand { get; }

    public double BackgroundOpacity { get => _backgroundOpacity; private set => SetProperty(ref _backgroundOpacity, value); }

    public double UiScale { get => _uiScale; private set => SetProperty(ref _uiScale, value); }

    /// <summary>False in click-through mode: buttons, arrows and the resize grip are hidden.</summary>
    public bool IsInteractive { get => _isInteractive; private set => SetProperty(ref _isInteractive, value); }

    public bool IsExpanded { get => _isExpanded; private set => SetProperty(ref _isExpanded, value); }

    public bool IsLocked { get => _isLocked; private set => SetProperty(ref _isLocked, value); }

    public bool IsViewingToday => _dayOffset == 0;

    /// <summary>"TODAY", "YESTERDAY", "TOMORROW" or e.g. "THU 22.10".</summary>
    public string DayLabel { get => _dayLabel; private set => SetProperty(ref _dayLabel, value); }

    public ConnectionState Connection { get => _connection; private set => SetProperty(ref _connection, value); }

    /// <summary>Header text: time of the last successful update.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string StatusToolTip { get => _statusToolTip; private set => SetProperty(ref _statusToolTip, value); }

    /// <summary>Loading / empty / error message shown instead of game cards. Null when cards are shown.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message is not null;

    /// <summary>Watched-country summary above the games ("FINNS TONIGHT").</summary>
    public bool ShowCountryPanel { get => _showCountryPanel; private set => SetProperty(ref _showCountryPanel, value); }

    public string CountryPanelTitle { get => _countryPanelTitle; private set => SetProperty(ref _countryPanelTitle, value); }

    public string CountryPanelFooter { get => _countryPanelFooter; private set => SetProperty(ref _countryPanelFooter, value); }

    public IReadOnlyList<CountryPanelLine> CountryPanelLines
    {
        get => _countryPanelLines;
        private set
        {
            if (_countryPanelLines.SequenceEqual(value)) return;
            _countryPanelLines = value;
            OnPropertyChanged();
        }
    }

    private void SetDayOffset(int offset)
    {
        offset = Math.Clamp(offset, -MaxDaysBack, MaxDaysForward);
        _returnToToday.Stop();
        if (offset != 0) _returnToToday.Start();
        if (offset == _dayOffset) return;

        _dayOffset = offset;
        OnPropertyChanged(nameof(IsViewingToday));
        var viewed = ViewedDate();
        // Yesterday and today are always loaded; other dates are fetched on demand.
        _requestDate(offset is 0 or -1 || viewed is null ? null : viewed);
        Apply(_snapshot);
        CommandManager.InvalidateRequerySuggested();
    }

    private DateOnly? ViewedDate() =>
        _snapshot.TodayNhlDate == default ? null : _snapshot.TodayNhlDate.AddDays(_dayOffset);

    private void ApplySettings()
    {
        var s = _settings.Current;
        BackgroundOpacity = s.Appearance.BackgroundOpacity;
        UiScale = s.Appearance.UiScale;
        IsExpanded = _runtime.EffectiveDisplayMode(s) == DisplayMode.Expanded;
        IsInteractive = !_runtime.EffectiveClickThrough(s);
        IsLocked = s.Window.LockPosition;
        ToastsAtBottom = s.Notifications.Placement == ToastPlacement.Bottom;
        IsDoNotDisturb = _runtime.DoNotDisturb;
        IsGaming = _runtime.IsGaming;
    }

    private void Apply(ScoreboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        var culture = CultureInfo.CurrentCulture;
        var settings = _settings.Current;
        var favorites = settings.Teams.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var watched = new Dictionary<long, IReadOnlyList<WatchedPlayer>>();
        IReadOnlyList<WatchedPlayer> Watched(HockeyGame g) =>
            watched.TryGetValue(g.Id, out var players) ? players : watched[g.Id] = _countryWatch.PlayersFor(g);
        var context = new CardContext(settings, favorites, _teams.Find, snapshot.HatTricks, DateTimeOffset.UtcNow, TimeZoneInfo.Local, culture)
        {
            WatchedPlayers = Watched,
        };

        var viewed = ViewedDate();
        DayLabel = viewed is { } date ? GameStatusFormatter.FormatDayLabel(date, snapshot.TodayNhlDate, culture) : "TODAY";

        IReadOnlyList<HockeyGame>? source = _dayOffset == 0
            ? snapshot.HasData ? snapshot.Games : null
            : viewed is { } d && snapshot.GamesByDate.TryGetValue(d, out var dayGames) ? dayGames : null;

        // The filter applies to the today view only; browsing other days always shows every game.
        var filter = _dayOffset == 0 ? settings.Teams.Filter : GameFilter.All;
        var visible = source is null
            ? []
            : GameOrdering.Sort(GameOrdering.Filter(source, filter, favorites, g => context.HatTricksFor(g).Count > 0), favorites, settings.Teams.SortOrder);
        SyncCards(visible, context);
        UpdateCountryPanel(source ?? [], Watched, settings.CountryWatch);

        Connection = (snapshot.HasData, snapshot.IsStale, snapshot.LastError) switch
        {
            (false, _, null) => ConnectionState.Connecting,
            (false, _, _) => ConnectionState.Error,
            (true, true, _) => ConnectionState.Stale,
            _ => ConnectionState.Ok,
        };

        string Time(DateTimeOffset value, string format = "t") => value.ToLocalTime().ToString(format, culture);

        Message = (Connection, source) switch
        {
            (ConnectionState.Connecting, _) => "Loading NHL scores...",
            (ConnectionState.Error, _) => snapshot.NextRefreshUtc is { } next
                ? $"NHL scores unavailable. Retrying at {Time(next)}."
                : "NHL scores unavailable.",
            (_, null) => "Loading...",
            _ when Games.Count == 0 && source.Count > 0 => $"No games match the filter: {GameOrdering.Describe(filter).ToLowerInvariant()}.",
            _ when Games.Count == 0 => _dayOffset == 0 ? "No NHL games today." : $"No NHL games on {DayLabel.ToLowerInvariant()}.",
            _ => null,
        };

        StatusText = snapshot.LastSuccessUtc is { } success ? Time(success) : "--:--";

        var tip = new List<string>
        {
            snapshot.LastSuccessUtc is { } ok ? $"Last updated {Time(ok, "T")}" : "Not updated yet",
        };
        if (snapshot.NextRefreshUtc is { } nextRefresh) tip.Add($"Next refresh {Time(nextRefresh, "T")}");
        if (snapshot.IsStale && snapshot.LastError is { } error) tip.Add($"Showing cached scores. Last error: {error}");
        StatusToolTip = string.Join(Environment.NewLine, tip);
    }

    /// <summary>Watched players with points (and goalies who played) across the shown day's games.</summary>
    private void UpdateCountryPanel(IReadOnlyList<HockeyGame> games, Func<HockeyGame, IReadOnlyList<WatchedPlayer>> watched, CountryWatchSettings settings)
    {
        var players = settings.Enabled ? games.SelectMany(watched).DistinctBy(p => (p.PlayerId, p.GameId)).ToList() : [];
        ShowCountryPanel = settings.ShowPanel && players.Count > 0;
        if (!ShowCountryPanel) return;

        CountryPanelTitle = $"{HockeyCountries.PanelTitle(settings.Countries)} {(_dayOffset == 0 ? "TONIGHT" : "· " + DayLabel)}";
        CountryPanelLines = players
            .Where(p => p.HasStat)
            .OrderBy(p => p.IsGoalie)
            .ThenByDescending(p => p.Points)
            .ThenByDescending(p => p.Goals)
            .Select(p => new CountryPanelLine(p.ShortName, p.Team, p.Stat))
            .ToList();
        var gameCount = players.Select(p => p.GameId).Distinct().Count();
        CountryPanelFooter = $"{players.Select(p => p.PlayerId).Distinct().Count()} playing in {gameCount} game{(gameCount == 1 ? "" : "s")}"
                             + (CountryPanelLines.Count == 0 ? " · no points yet" : "");
    }

    private void SyncCards(IReadOnlyList<HockeyGame> ordered, CardContext context)
    {
        var ids = ordered.Select(g => g.Id).ToHashSet();
        for (var i = Games.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(Games[i].Id)) continue;
            _cards.Remove(Games[i].Id);
            Games.RemoveAt(i);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var game = ordered[i];
            if (!_cards.TryGetValue(game.Id, out var card))
            {
                card = new GameCardViewModel(game.Id, _logos);
                _cards[game.Id] = card;
            }

            card.Update(game, context);
            var index = Games.IndexOf(card);
            if (index < 0) Games.Insert(i, card);
            else if (index != i) Games.Move(index, i);
        }
    }
}
