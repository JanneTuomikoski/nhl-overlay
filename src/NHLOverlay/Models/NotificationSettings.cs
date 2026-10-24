namespace NHLOverlay.Models;

/// <summary>How one kind of event is delivered.</summary>
public sealed class NotificationRule
{
    public bool Enabled { get; set; } = true;

    public TeamScope Scope { get; set; } = TeamScope.FavoritesOnly;

    /// <summary>Toast inside the overlay.</summary>
    public bool Overlay { get; set; } = true;

    /// <summary>Windows notification (from the tray icon).</summary>
    public bool Desktop { get; set; }

    public bool Sound { get; set; }

    /// <summary>Spoken with Windows text-to-speech.</summary>
    public bool Speech { get; set; }

    public NotificationRule Clone() => (NotificationRule)MemberwiseClone();
}

public enum ToastPlacement
{
    Top,
    Bottom,
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    public Dictionary<GameEventKind, NotificationRule> Rules { get; set; } = DefaultRules();

    public int ToastSeconds { get; set; } = 8;

    public ToastPlacement Placement { get; set; } = ToastPlacement.Top;

    /// <summary>Master switch for all notification sounds.</summary>
    public bool SoundsEnabled { get; set; } = true;

    public double SoundVolume { get; set; } = 0.6;

    /// <summary>Master switch for spoken alerts.</summary>
    public bool SpeechEnabled { get; set; } = true;

    /// <summary>Installed voice name; empty = the system default voice.</summary>
    public string Voice { get; set; } = "";

    /// <summary>-5 (slow) .. 5 (fast).</summary>
    public int SpeechRate { get; set; }

    /// <summary>Mutes sounds and voice (toasts still appear) between these local times.</summary>
    public bool QuietHoursEnabled { get; set; }

    public TimeOnly QuietFrom { get; set; } = new(0, 0);

    public TimeOnly QuietTo { get; set; } = new(8, 0);

    public static Dictionary<GameEventKind, NotificationRule> DefaultRules() => new()
    {
        [GameEventKind.GameStarted] = new() { Scope = TeamScope.FavoritesOnly },
        [GameEventKind.Goal] = new() { Scope = TeamScope.FavoritesOnly, Sound = true },
        [GameEventKind.HatTrickWatch] = new() { Scope = TeamScope.AllTeams },
        [GameEventKind.HatTrickCompleted] = new() { Scope = TeamScope.AllTeams, Sound = true },
        [GameEventKind.Overtime] = new() { Scope = TeamScope.FavoritesOnly },
        [GameEventKind.CloseGame] = new() { Enabled = false, Scope = TeamScope.FavoritesOnly },
        [GameEventKind.GameFinal] = new() { Scope = TeamScope.FavoritesOnly },
        [GameEventKind.WorthWatching] = new() { Scope = TeamScope.AllTeams },
        [GameEventKind.CountryPlayerPoint] = new() { Scope = TeamScope.AllTeams },
        [GameEventKind.Shutout] = new() { Scope = TeamScope.AllTeams },
        [GameEventKind.Milestone] = new() { Scope = TeamScope.AllTeams, Sound = true },
        [GameEventKind.PointStreak] = new() { Scope = TeamScope.AllTeams },
    };

    /// <summary>The rule for <paramref name="kind"/> (always present after normalization).</summary>
    public NotificationRule RuleFor(GameEventKind kind) => Rules.TryGetValue(kind, out var rule) ? rule : DefaultRules()[kind];

    internal NotificationSettings Normalize()
    {
        var defaults = DefaultRules();
        Rules ??= [];
        foreach (var kind in Rules.Keys.Where(k => !Enum.IsDefined(k)).ToList()) Rules.Remove(kind);
        foreach (var (kind, rule) in defaults)
        {
            if (!Rules.TryGetValue(kind, out var existing) || existing is null) Rules[kind] = rule;
            else existing.Scope = AppSettings.DefinedOr(existing.Scope, rule.Scope);
        }

        ToastSeconds = Math.Clamp(ToastSeconds, 3, 60);
        Placement = AppSettings.DefinedOr(Placement, ToastPlacement.Top);
        SoundVolume = AppearanceSettings.ClampOrDefault(SoundVolume, 0, 1, 0.6);
        SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        Voice ??= "";
        return this;
    }

    public bool IsQuietTime(TimeOnly now) =>
        QuietHoursEnabled && (QuietFrom <= QuietTo
            ? now >= QuietFrom && now < QuietTo
            : now >= QuietFrom || now < QuietTo); // range over midnight
}

public enum GamingDisplayMode
{
    Unchanged,
    Compact,
}

/// <summary>Behavior while one of the listed games has focus.</summary>
public sealed class GamingSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Process names without ".exe", e.g. "Wow".</summary>
    public List<string> GameProcesses { get; set; } = ["Wow", "WowClassic"];

    public GamingDisplayMode DisplayMode { get; set; } = GamingDisplayMode.Compact;

    /// <summary>Make the overlay click-through while gaming (the interaction hotkey still overrides it).</summary>
    public bool ClickThrough { get; set; } = true;

    /// <summary>Windows notifications can pop over the game; off by default while gaming.</summary>
    public bool DesktopNotifications { get; set; }

    public bool Sounds { get; set; } = true;

    public bool Speech { get; set; } = true;

    internal GamingSettings Normalize()
    {
        GameProcesses = (GameProcesses ?? [])
            .Select(p => p?.Trim() ?? "")
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        DisplayMode = AppSettings.DefinedOr(DisplayMode, GamingDisplayMode.Compact);
        return this;
    }
}
