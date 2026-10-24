using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Services;

/// <summary>
/// Delivers <see cref="GameEvent"/>s according to the notification rules: overlay toast, Windows notification,
/// sound and voice. Independent of the overlay window and the NHL API models' origin; other features (country
/// watch, milestones) feed events in through <see cref="Deliver"/>.
/// </summary>
public sealed class NotificationService
{
    internal static readonly TimeSpan WorthWatchingCooldown = TimeSpan.FromMinutes(10);
    internal const int MaxIndividualPerBatch = 3;

    private readonly SettingsService _settings;
    private readonly RuntimeState _runtime;
    private readonly ToastService _toasts;
    private readonly ISoundOutput _sounds;
    private readonly ISpeechOutput _speech;
    private readonly Action<string, string> _desktop;
    private readonly GameEventDetector _detector = new();
    private readonly List<GameEvent> _held = [];
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _lastWorthWatching = DateTimeOffset.MinValue;

    public NotificationService(
        GameStateService state,
        SettingsService settings,
        RuntimeState runtime,
        ToastService toasts,
        ISoundOutput sounds,
        ISpeechOutput speech,
        Action<string, string> desktop,
        Dispatcher dispatcher,
        Func<DateTimeOffset>? now = null,
        Func<long, RosterPlayer?>? findPlayer = null)
    {
        _settings = settings;
        _runtime = runtime;
        _toasts = toasts;
        _sounds = sounds;
        _speech = speech;
        _desktop = desktop;
        _now = now ?? (() => DateTimeOffset.Now);

        // Snapshots arrive sequentially from the polling loop, so the detector is only ever used by one thread.
        state.Changed += (_, snapshot) =>
        {
            var watch = _settings.Current.CountryWatch;
            var countries = watch.Enabled ? watch.Countries.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            var events = _detector.Detect(snapshot, findPlayer, countries);
            if (events.Count > 0) dispatcher.InvokeAsync(() => Deliver(events));
        };
        runtime.Changed += (_, _) =>
        {
            if (!_runtime.DoNotDisturb) ReleaseHeld();
        };
        settings.Changed += (_, s) => dispatcher.InvokeAsync(() => _speech.Configure(s.Notifications.Voice, s.Notifications.SpeechRate));
        _speech.Configure(settings.Current.Notifications.Voice, settings.Current.Notifications.SpeechRate);
    }

    /// <summary>Optional extra filter (e.g. the spoiler shield). Return true to drop the event.</summary>
    public Func<GameEvent, bool>? IsSuppressed { get; set; }

    /// <summary>Applies the rules and delivers. UI thread.</summary>
    public void Deliver(IReadOnlyList<GameEvent> events)
    {
        var s = _settings.Current;
        if (!s.Notifications.Enabled) return;

        var favorites = s.Teams.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = events.Where(e => IsAllowed(e, s, favorites)).ToList();

        // A watched player's own goal that is already announced as a goal needs no second alert.
        var announcedGoals = allowed.Where(e => e.Kind == GameEventKind.Goal).Select(e => e.GoalKey).ToHashSet();
        allowed.RemoveAll(e => e.Kind == GameEventKind.CountryPlayerPoint && e.GoalKey is not null && announcedGoals.Contains(e.GoalKey));
        if (allowed.Count == 0) return;

        if (_runtime.DoNotDisturb)
        {
            _held.AddRange(allowed);
            return;
        }

        foreach (var e in allowed.Take(MaxIndividualPerBatch)) Notify(e, s, s.Notifications.RuleFor(e.Kind));
        var rest = allowed.Skip(MaxIndividualPerBatch).ToList();
        if (rest.Count > 0)
            ShowSummary($"+{rest.Count} MORE UPDATE{(rest.Count == 1 ? "" : "S")}", rest, s, speak: false);
    }

    private bool IsAllowed(GameEvent e, AppSettings s, IReadOnlySet<string> favorites)
    {
        var rule = s.Notifications.RuleFor(e.Kind);
        if (!rule.Enabled) return false;
        if (rule.Scope == TeamScope.FavoritesOnly && !e.Teams.Any(favorites.Contains)) return false;
        if (IsSuppressed?.Invoke(e) == true) return false;
        if (e.Kind == GameEventKind.WorthWatching)
        {
            if (_now() - _lastWorthWatching < WorthWatchingCooldown) return false;
            _lastWorthWatching = _now();
        }

        return true;
    }

    private void Notify(GameEvent e, AppSettings s, NotificationRule rule)
    {
        var n = s.Notifications;
        var gaming = _runtime.IsGaming && s.Gaming.Enabled;
        var quiet = n.IsQuietTime(TimeOnly.FromDateTime(_now().DateTime));

        if (rule.Overlay) _toasts.Show(e.Kind, e.Title, e.Body, TimeSpan.FromSeconds(n.ToastSeconds));
        if (rule.Desktop && (!gaming || s.Gaming.DesktopNotifications)) _desktop(e.Title, e.Body);
        if (rule.Sound && n.SoundsEnabled && !quiet && (!gaming || s.Gaming.Sounds)) _sounds.Play(SoundFor(e.Kind), n.SoundVolume);
        if (rule.Speech && n.SpeechEnabled && !quiet && (!gaming || s.Gaming.Speech)) _speech.Speak(e.Speech);
        Log.Info($"Notification {e.Kind}: {e.Title} | {e.Body}");
    }

    private void ReleaseHeld()
    {
        if (_held.Count == 0) return;
        var held = _held.ToList();
        _held.Clear();
        ShowSummary($"WHILE DO NOT DISTURB: {held.Count} UPDATE{(held.Count == 1 ? "" : "S")}", held, _settings.Current, speak: true);
    }

    private void ShowSummary(string title, IReadOnlyList<GameEvent> events, AppSettings s, bool speak)
    {
        var lines = events.Take(4).Select(e => e.Title).ToList();
        if (events.Count > 4) lines.Add("...");
        _toasts.Show(null, title, string.Join(Environment.NewLine, lines), TimeSpan.FromSeconds(Math.Max(s.Notifications.ToastSeconds, 12)));
        if (speak && s.Notifications.SpeechEnabled && events.Any(e => s.Notifications.RuleFor(e.Kind).Speech))
            _speech.Speak($"While you were away: {events.Count} updates.");
    }

    /// <summary>Plays a sample of <paramref name="kind"/> through its configured channels (settings "Test" button).</summary>
    public void Test(GameEventKind kind)
    {
        var s = _settings.Current;
        var rule = s.Notifications.RuleFor(kind).Clone();
        rule.Overlay = true;
        Notify(SampleEvent(kind), s, rule);
    }

    internal static AlertSound SoundFor(GameEventKind kind) => kind switch
    {
        GameEventKind.Goal => AlertSound.Horn,
        GameEventKind.HatTrickCompleted => AlertSound.Fanfare,
        _ => AlertSound.Chime,
    };

    private static GameEvent SampleEvent(GameEventKind kind)
    {
        var game = new HockeyGame
        {
            Id = 1,
            Status = GameStatus.Live,
            Away = new HockeyTeam { Abbrev = "EDM", Name = "Oilers", Score = 3 },
            Home = new HockeyTeam { Abbrev = "TOR", Name = "Maple Leafs", Score = 2 },
        };
        var (title, body, speech) = kind switch
        {
            GameEventKind.GameStarted => ("PUCK DROP  EDM @ TOR", "Oilers at Maple Leafs", "Puck drop: Oilers at Maple Leafs."),
            GameEventKind.Goal => ("EDM GOAL  EDM 3-2 TOR", "C. McDavid (2) · PP · 3RD 12:41", "Goal, Oilers! Connor McDavid, his second of the game. Oilers lead 3 to 2."),
            GameEventKind.HatTrickWatch => ("HAT-TRICK WATCH", "Connor McDavid (EDM) has 2 goals · EDM 3-2 TOR", "Hat-trick watch: Connor McDavid has two goals."),
            GameEventKind.HatTrickCompleted => ("HAT TRICK!", "Connor McDavid (EDM) · 3 goals · EDM 4-2 TOR", "Hat trick! Connor McDavid has 3 goals."),
            GameEventKind.Overtime => ("OVERTIME  EDM 3-3 TOR", "Oilers and Maple Leafs go to overtime", "Oilers and Maple Leafs go to overtime, tied 3 to 3."),
            GameEventKind.CloseGame => ("CLOSE GAME  EDM 3-2 TOR", "3RD 08:41 · one-goal game", "Close game. Oilers lead 3 to 2. About 8 minutes left."),
            GameEventKind.WorthWatching => ("WORTH WATCHING  EDM 3-3 TOR", "tied 3-3 · 3RD 04:12 · comeback from 2 down", "Worth watching: Oilers versus Maple Leafs. Tied 3-3, comeback from 2 down."),
            GameEventKind.CountryPlayerPoint => ("FIN GOAL  K. Kapanen · EDM 3-2 TOR", "Kasperi Kapanen (EDM) · 1+1 tonight", "Finnish goal! Kasperi Kapanen scores for the Oilers."),
            _ => ("FINAL  EDM 4-2 TOR", "Oilers win 4-2", "Final: Oilers beat the Maple Leafs 4 to 2."),
        };
        return new GameEvent(kind, $"test:{kind}", game, title, body, speech);
    }
}
