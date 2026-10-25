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

/// <summary>The morning recap as shown above the games.</summary>
public sealed record RecapCard(
    string Title,
    IReadOnlyList<string> Favorites,
    IReadOnlyList<string> Highlights,
    string CountryTitle,
    IReadOnlyList<string> CountryLines,
    string Footer,
    bool CanDismiss)
{
    public bool HasFavorites => Favorites.Count > 0;

    public bool HasHighlights => Highlights.Count > 0;

    public bool HasCountryLines => CountryLines.Count > 0;
}

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
    private readonly SpoilerService _spoilers;
    private readonly RecapService _recaps;
    private readonly Action<DateOnly?> _requestDate;
    private readonly Action<long> _openFocus;
    private readonly WatchService _watches;
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
    private RecapCard? _recap;
    private DateOnly _recapDate;
    private bool _isStandingsView;

    public OverlayViewModel(
        GameStateService state,
        SettingsService settings,
        LogoService logos,
        TeamDirectoryService teams,
        RuntimeState runtime,
        ToastService toasts,
        CountryWatchService countryWatch,
        SpoilerService spoilers,
        RecapService recaps,
        StandingsViewModel standings,
        ScheduleService schedules,
        WatchService watches,
        Action<DateOnly?> requestDate,
        Action<long> openFocus,
        Dispatcher dispatcher)
    {
        _openFocus = openFocus;
        _watches = watches;
        watches.Changed += (_, _) => dispatcher.InvokeAsync(() => Apply(_snapshot));
        Standings = standings;
        schedules.Changed += (_, _) => dispatcher.InvokeAsync(() => Apply(_snapshot));
        ToggleStandingsCommand = new RelayCommand(() =>
        {
            IsStandingsView = !IsStandingsView;
            Apply(_snapshot);
        });
        _settings = settings;
        _logos = logos;
        _teams = teams;
        _runtime = runtime;
        _countryWatch = countryWatch;
        _spoilers = spoilers;
        _recaps = recaps;
        spoilers.Changed += (_, _) => Apply(_snapshot);
        DismissRecapCommand = new RelayCommand(() =>
        {
            _recaps.Dismiss(_recapDate);
            Apply(_snapshot);
        });
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

    public ICommand DismissRecapCommand { get; }

    public ICommand ToggleStandingsCommand { get; }

    public StandingsViewModel Standings { get; }

    /// <summary>Shows standings, playoff race and next games instead of the game list.</summary>
    public bool IsStandingsView
    {
        get => _isStandingsView;
        private set
        {
            if (SetProperty(ref _isStandingsView, value)) OnPropertyChanged(nameof(IsGamesView));
        }
    }

    public bool IsGamesView => !IsStandingsView;

    /// <summary>Morning recap ("LAST NIGHT"), or null when not shown.</summary>
    public RecapCard? Recap
    {
        get => _recap;
        private set
        {
            if (SetProperty(ref _recap, value)) OnPropertyChanged(nameof(ShowRecap));
        }
    }

    public bool ShowRecap => Recap is not null;

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
            watched.TryGetValue(g.Id, out var players) ? players
            : watched[g.Id] = _spoilers.IsShielded(g) ? [] : _countryWatch.PlayersFor(g);
        var context = new CardContext(settings, favorites, _teams.Find, snapshot.HatTricks, DateTimeOffset.UtcNow, TimeZoneInfo.Local, culture)
        {
            WatchedPlayers = Watched,
            IsShielded = _spoilers.IsShielded,
            WatchLines = _watches.CardLines,
        };

        var viewed = ViewedDate();
        DayLabel = IsStandingsView ? "STANDINGS"
            : viewed is { } date ? GameStatusFormatter.FormatDayLabel(date, snapshot.TodayNhlDate, culture) : "TODAY";
        if (IsStandingsView) Standings.Update(settings, snapshot, DateTimeOffset.UtcNow, TimeZoneInfo.Local, culture);

        IReadOnlyList<HockeyGame>? source = _dayOffset == 0
            ? snapshot.HasData ? snapshot.Games : null
            : viewed is { } d && snapshot.GamesByDate.TryGetValue(d, out var dayGames) ? dayGames : null;

        // The filter applies to the today view only; browsing other days always shows every game.
        var filter = _dayOffset == 0 ? settings.Teams.Filter : GameFilter.All;
        // NHL first, then Liiga; each league sorted on its own.
        var visible = source is null
            ? []
            : GameOrdering.Filter(source, filter, favorites, g => context.HatTricksFor(g).Count > 0)
                .GroupBy(g => g.League)
                .OrderBy(g => g.Key)
                .SelectMany(g => GameOrdering.Sort(g, favorites, settings.Teams.SortOrder))
                .ToList();
        SyncCards(visible, context);
        UpdateCountryPanel(source ?? [], Watched, settings.CountryWatch);
        UpdateRecap(snapshot, culture);

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
            _ when Games.Count == 0 =>
                $"No {(settings.General.ShowLiiga ? "games" : "NHL games")} {(_dayOffset == 0 ? "today" : "on " + DayLabel.ToLowerInvariant())}.",
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

    private void UpdateRecap(ScoreboardSnapshot snapshot, CultureInfo culture)
    {
        var recap = _recaps.For(snapshot, _dayOffset, DateTimeOffset.UtcNow);
        if (recap is null)
        {
            Recap = null;
            return;
        }

        _recapDate = recap.Date;
        var footer = $"{recap.FinishedGames} game{(recap.FinishedGames == 1 ? "" : "s")}"
                     + (recap.HiddenGames > 0 ? $" · {recap.HiddenGames} hidden (watch later)" : "");
        var card = new RecapCard(
            $"LAST NIGHT · {GameStatusFormatter.FormatShortDate(recap.Date, culture)}",
            recap.FavoriteResults,
            recap.Highlights,
            recap.CountryTitle,
            // Above yesterday's games the watched-players panel already lists the same points.
            _dayOffset == 0 ? recap.CountryLines : [],
            footer,
            CanDismiss: _dayOffset == 0);
        // Rebuilt every refresh; only notify when the content actually changed.
        if (Recap is null || Recap.Title != card.Title || Recap.Footer != card.Footer || Recap.CanDismiss != card.CanDismiss
            || !Recap.Favorites.SequenceEqual(card.Favorites) || !Recap.Highlights.SequenceEqual(card.Highlights)
            || !Recap.CountryLines.SequenceEqual(card.CountryLines))
            Recap = card;
    }

    /// <summary>Watched players with points (and goalies who played) across the shown day's games.</summary>
    private void UpdateCountryPanel(IReadOnlyList<HockeyGame> games, Func<HockeyGame, IReadOnlyList<WatchedPlayer>> watched, CountryWatchSettings settings)
    {
        // Shielded games return no players (see Watched), so the panel never spoils them.
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

        var placed = PlaceInSections(ordered);
        var collapsed = context.Settings.Appearance.CollapsedSections;
        for (var i = 0; i < placed.Count; i++)
        {
            var (game, key, title, parent) = placed[i];
            if (!_cards.TryGetValue(game.Id, out var card))
            {
                card = new GameCardViewModel(game.Id, _logos, (id, hide) =>
                {
                    if (hide) _spoilers.Shield(id);
                    else _spoilers.Reveal(id);
                }, _openFocus, ToggleSection);
                _cards[game.Id] = card;
            }

            card.Update(game, context);
            // Only titled sections can be collapsed; a collapsed league also hides its FINAL section.
            var parentCollapsed = parent is not null && placed.Any(p => p.Key == parent && p.Title is not null) && collapsed.Contains(parent);
            card.SectionKey = key;
            card.SectionTitle = (i == 0 || placed[i - 1].Key != key) && !parentCollapsed ? title : null;
            card.IsSectionCollapsed = parentCollapsed || (title is not null && collapsed.Contains(key));
            card.SectionSummary = card.IsSectionCollapsed && card.HasSectionTitle
                ? SectionSummary(placed.Where(p => p.Key == key || p.Parent == key).Select(p => p.Game))
                : "";
            var index = Games.IndexOf(card);
            if (index < 0) Games.Insert(i, card);
            else if (index != i) Games.Move(index, i);
        }
    }

    /// <summary>Where a card goes: its section's key ("NHL", "NHL FINAL", ...), the title shown above the section's first card (null = untitled), and the enclosing league section.</summary>
    internal sealed record CardPlacement(HockeyGame Game, string Key, string? Title, string? Parent);

    /// <summary>
    /// League sections (titled only when both leagues are on screen), each with its ended games last in a
    /// "FINAL" section when there are also games still to come or in progress.
    /// </summary>
    internal static IReadOnlyList<CardPlacement> PlaceInSections(IReadOnlyList<HockeyGame> ordered)
    {
        var bothLeagues = ordered.Any(g => g.League != ordered[0].League);
        var placed = new List<CardPlacement>();
        foreach (var league in ordered.GroupBy(g => g.League))
        {
            var name = league.Key == League.Nhl ? "NHL" : "LIIGA";
            var active = league.Where(g => !g.Status.IsFinal()).ToList();
            var ended = league.Where(g => g.Status.IsFinal()).ToList();
            var split = active.Count > 0 && ended.Count > 0;
            placed.AddRange((split ? active : league.ToList()).Select(g => new CardPlacement(g, name, bothLeagues ? name : null, null)));
            if (split) placed.AddRange(ended.Select(g => new CardPlacement(g, $"{name} FINAL", "FINAL", name)));
        }

        return placed;
    }

    private void ToggleSection(string key) => _settings.Update(s =>
    {
        if (!s.Appearance.CollapsedSections.Remove(key)) s.Appearance.CollapsedSections.Add(key);
    });

    /// <summary>"3 games · 1 live" (no scores, so a collapsed section never spoils anything).</summary>
    internal static string SectionSummary(IEnumerable<HockeyGame> games)
    {
        var list = games.ToList();
        var live = list.Count(g => g.Status.IsInProgress());
        return $"{list.Count} game{(list.Count == 1 ? "" : "s")}" + (live > 0 ? $" · {live} live" : "");
    }
}
