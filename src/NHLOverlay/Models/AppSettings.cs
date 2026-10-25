namespace NHLOverlay.Models;

/// <summary>
/// Persisted user configuration. New properties must have sensible initializers so that
/// settings files written by older versions load with defaults for anything missing.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public TeamSettings Teams { get; set; } = new();

    public HatTrickSettings HatTrick { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();

    public GamingSettings Gaming { get; set; } = new();

    public CountryWatchSettings CountryWatch { get; set; } = new();

    public SpoilerSettings Spoilers { get; set; } = new();

    public RecapSettings Recap { get; set; } = new();

    public FocusSettings Focus { get; set; } = new();

    public PlayerWatchSettings PlayerWatch { get; set; } = new();

    /// <summary>Clamps out-of-range values and replaces nulls with defaults.</summary>
    public AppSettings Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        General = (General ?? new()).Normalize();
        Appearance = (Appearance ?? new()).Normalize();
        Window = (Window ?? new()).Normalize();
        Hotkeys = (Hotkeys ?? new()).Normalize();
        Teams = (Teams ?? new()).Normalize();
        HatTrick = (HatTrick ?? new()).Normalize();
        Notifications = (Notifications ?? new()).Normalize();
        Gaming = (Gaming ?? new()).Normalize();
        CountryWatch = (CountryWatch ?? new()).Normalize();
        Spoilers = (Spoilers ?? new()).Normalize();
        Recap = (Recap ?? new()).Normalize();
        Focus = (Focus ?? new()).Normalize();
        PlayerWatch = (PlayerWatch ?? new()).Normalize();
        return this;
    }

    internal static T DefinedOr<T>(T value, T fallback) where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;

    /// <summary>Trimmed, upper-case, distinct, non-empty team keys.</summary>
    internal static List<string> NormalizeTeamList(List<string>? teams) =>
        (teams ?? []).Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim().ToUpperInvariant())
                     .Distinct()
                     .ToList();
}

public enum GameFilter
{
    All,
    Favorites,
    Live,
    LiveAndUpcoming,
    HatTrickWatch,
    Completed,
}

public enum GameSortOrder
{
    /// <summary>Live favorites, live, upcoming favorites, upcoming, finished.</summary>
    Default,
    StartTime,
}

public enum FavoriteHighlight
{
    BarAndTint,
    Bar,
    None,
}

public enum TeamScope
{
    AllTeams,
    FavoritesOnly,
}

public sealed class HatTrickSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>League-wide by default, even for games without favorite teams.</summary>
    public TeamScope Scope { get; set; } = TeamScope.AllTeams;

    /// <summary>Amber highlight on games with a hat-trick watch or a completed hat trick.</summary>
    public bool HighlightCards { get; set; } = true;

    public bool ShowPlayerName { get; set; } = true;

    internal HatTrickSettings Normalize()
    {
        Scope = AppSettings.DefinedOr(Scope, TeamScope.AllTeams);
        return this;
    }
}

public sealed class TeamSettings
{
    /// <summary>Favorite team abbreviations, e.g. "EDM".</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Applies to the today view; other days always show every game.</summary>
    public GameFilter Filter { get; set; } = GameFilter.All;

    public GameSortOrder SortOrder { get; set; } = GameSortOrder.Default;

    public FavoriteHighlight Highlight { get; set; } = FavoriteHighlight.BarAndTint;

    internal TeamSettings Normalize()
    {
        Favorites = AppSettings.NormalizeTeamList(Favorites);
        Filter = AppSettings.DefinedOr(Filter, GameFilter.All);
        SortOrder = AppSettings.DefinedOr(SortOrder, GameSortOrder.Default);
        Highlight = AppSettings.DefinedOr(Highlight, FavoriteHighlight.BarAndTint);
        return this;
    }
}

public sealed class GeneralSettings
{
    public static readonly int[] AllowedRefreshIntervals = [15, 30, 60, 120];

    public bool LaunchWithWindows { get; set; }

    /// <summary>When false, the app starts with only the tray icon visible.</summary>
    public bool ShowOverlayOnStartup { get; set; } = true;

    public int RefreshIntervalSeconds { get; set; } = 30;

    /// <summary>Also show Finnish Liiga games, in their own section below the NHL games.</summary>
    public bool ShowLiiga { get; set; }

    internal GeneralSettings Normalize()
    {
        // Snap to the closest supported interval.
        RefreshIntervalSeconds = AllowedRefreshIntervals.MinBy(i => Math.Abs(i - RefreshIntervalSeconds));
        return this;
    }
}

public enum DisplayMode
{
    /// <summary>One line per game.</summary>
    Compact,

    /// <summary>Logos, team names, shots and every goal; suited to a large or vertical monitor.</summary>
    Expanded,
}

public sealed class AppearanceSettings
{
    public const double MinUiScale = 0.75;
    public const double MaxUiScale = 3.0;

    public DisplayMode DisplayMode { get; set; } = DisplayMode.Compact;

    /// <summary>Opacity of the overlay background (text stays fully opaque). 0.0 - 1.0.</summary>
    public double BackgroundOpacity { get; set; } = 0.85;

    /// <summary>Overall UI scale factor on top of Windows display scaling.</summary>
    public double UiScale { get; set; } = 1.0;

    public bool ShowTeamLogos { get; set; } = true;

    public bool ShowGameClock { get; set; } = true;

    public bool ShowShotsOnGoal { get; set; }

    /// <summary>Expanded view: list the assists under each goal.</summary>
    public bool ShowAssists { get; set; } = true;

    public const int MinGoalFlashSeconds = 5;
    public const int MaxGoalFlashSeconds = 60;

    /// <summary>A game card pulses softly for <see cref="GoalFlashSeconds"/> after a goal.</summary>
    public bool FlashOnGoal { get; set; } = true;

    public int GoalFlashSeconds { get; set; } = 20;

    /// <summary>Sections ("NHL", "LIIGA", "NHL FINAL", "LIIGA FINAL") the user collapsed by clicking their title.</summary>
    public List<string> CollapsedSections { get; set; } = [];

    internal AppearanceSettings Normalize()
    {
        if (!Enum.IsDefined(DisplayMode)) DisplayMode = DisplayMode.Compact;
        CollapsedSections = (CollapsedSections ?? []).Where(s => s is not null).Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s is "NHL" or "LIIGA" or "NHL FINAL" or "LIIGA FINAL").Distinct().ToList();
        BackgroundOpacity = ClampOrDefault(BackgroundOpacity, 0.0, 1.0, 0.85);
        UiScale = ClampOrDefault(UiScale, MinUiScale, MaxUiScale, 1.0);
        GoalFlashSeconds = Math.Clamp(GoalFlashSeconds, MinGoalFlashSeconds, MaxGoalFlashSeconds);
        return this;
    }

    internal static double ClampOrDefault(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed class WindowSettings
{
    public const double MinWidth = 200;
    public const double MaxWidth = 1200;

    /// <summary>
    /// Top-left corner in physical screen pixels (virtual-desktop coordinates). Pixels rather than WPF units
    /// because monitors can have different DPI scaling. Null = default placement.
    /// </summary>
    public int? ScreenX { get; set; }

    public int? ScreenY { get; set; }

    /// <summary>Content width in WPF units before <see cref="AppearanceSettings.UiScale"/> is applied.</summary>
    public double Width { get; set; } = 300;

    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>When true, mouse input passes through the overlay to the game underneath.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Prevents dragging and resizing.</summary>
    public bool LockPosition { get; set; }

    public bool SnapToEdges { get; set; } = true;

    internal WindowSettings Normalize()
    {
        Width = AppearanceSettings.ClampOrDefault(Width, MinWidth, MaxWidth, 300);
        return this;
    }
}

public sealed class HotkeySettings
{
    public const string DefaultToggleOverlay = "Ctrl+Alt+N";
    public const string DefaultToggleInteraction = "Ctrl+Alt+I";
    public const string DefaultToggleDoNotDisturb = "Ctrl+Alt+D";

    /// <summary>Show/hide the overlay. Empty string disables the hotkey.</summary>
    public string ToggleOverlay { get; set; } = DefaultToggleOverlay;

    /// <summary>Switch between click-through and interaction mode. Empty string disables the hotkey.</summary>
    public string ToggleInteraction { get; set; } = DefaultToggleInteraction;

    /// <summary>Hold all alerts until switched off again (then a summary is shown).</summary>
    public string ToggleDoNotDisturb { get; set; } = DefaultToggleDoNotDisturb;

    internal HotkeySettings Normalize()
    {
        ToggleOverlay = NormalizeGesture(ToggleOverlay, DefaultToggleOverlay);
        ToggleInteraction = NormalizeGesture(ToggleInteraction, DefaultToggleInteraction);
        ToggleDoNotDisturb = NormalizeGesture(ToggleDoNotDisturb, DefaultToggleDoNotDisturb);
        return this;
    }

    private static string NormalizeGesture(string? value, string fallback)
    {
        if (value is null) return fallback;
        if (value.Trim().Length == 0) return "";
        return HotkeyGesture.TryParse(value, out var gesture) ? gesture.ToString() : fallback;
    }
}
