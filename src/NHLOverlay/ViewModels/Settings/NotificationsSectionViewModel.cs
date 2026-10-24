using System.Globalization;
using System.Windows.Input;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>One row of the notification matrix: an event kind and its channels.</summary>
public sealed class NotificationRuleRow(GameEventKind kind, string label, string description, Action<GameEventKind> test) : ObservableObject
{
    private bool _enabled;
    private bool _favoritesOnly;
    private bool _overlay;
    private bool _desktop;
    private bool _sound;
    private bool _speech;

    public GameEventKind Kind { get; } = kind;

    public string Label { get; } = label;

    public string Description { get; } = description;

    public ICommand TestCommand { get; } = new RelayCommand(() => test(kind));

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public bool FavoritesOnly { get => _favoritesOnly; set => SetProperty(ref _favoritesOnly, value); }

    public bool Overlay { get => _overlay; set => SetProperty(ref _overlay, value); }

    public bool Desktop { get => _desktop; set => SetProperty(ref _desktop, value); }

    public bool Sound { get => _sound; set => SetProperty(ref _sound, value); }

    public bool Speech { get => _speech; set => SetProperty(ref _speech, value); }

    public void Load(NotificationRule rule)
    {
        Enabled = rule.Enabled;
        FavoritesOnly = rule.Scope == TeamScope.FavoritesOnly;
        Overlay = rule.Overlay;
        Desktop = rule.Desktop;
        Sound = rule.Sound;
        Speech = rule.Speech;
    }

    public NotificationRule ToRule() => new()
    {
        Enabled = Enabled,
        Scope = FavoritesOnly ? TeamScope.FavoritesOnly : TeamScope.AllTeams,
        Overlay = Overlay,
        Desktop = Desktop,
        Sound = Sound,
        Speech = Speech,
    };
}

/// <summary>Which events notify, through which channels, plus sound/voice/quiet-hours options.</summary>
public sealed class NotificationsSectionViewModel : ObservableObject, ISettingsSection
{
    private const string DefaultVoiceLabel = "System default";

    private readonly NotificationService _notifications;
    private bool _enabled;
    private double _toastSeconds;
    private ToastPlacement _placement;
    private bool _soundsEnabled;
    private double _soundVolumePercent;
    private bool _speechEnabled;
    private string _voice = DefaultVoiceLabel;
    private int _speechRate;
    private bool _quietHoursEnabled;
    private string _quietFrom = "";
    private string _quietTo = "";

    public NotificationsSectionViewModel(NotificationService notifications, SpeechService speech)
    {
        _notifications = notifications;
        Rules =
        [
            Row(GameEventKind.GameStarted, "Puck drop", "A game starts."),
            Row(GameEventKind.Goal, "Goals", "Scorer, strength and the new score."),
            Row(GameEventKind.HatTrickWatch, "Hat-trick watch", "A player reaches two goals in a game."),
            Row(GameEventKind.HatTrickCompleted, "Hat trick", "A player scores a third goal."),
            Row(GameEventKind.Overtime, "Overtime / shootout", "The game goes past regulation."),
            Row(GameEventKind.CloseGame, "Close game", "Within one goal with ten minutes or less left in the third."),
            Row(GameEventKind.GameFinal, "Final score", "The game ends."),
            Row(GameEventKind.WorthWatching, "Worth switching to", "The night's most exciting moment: late tie, comeback, hat-trick chance (at most one every 10 minutes)."),
        ];
        Voices = [DefaultVoiceLabel, .. speech.Voices];
        SpeechAvailable = speech.IsAvailable && speech.Voices.Count > 0;
    }

    public IReadOnlyList<NotificationRuleRow> Rules { get; }

    public IReadOnlyList<string> Voices { get; }

    public bool SpeechAvailable { get; }

    public IReadOnlyList<ChoiceOption<ToastPlacement>> PlacementOptions { get; } =
    [
        new(ToastPlacement.Top, "Above the games"),
        new(ToastPlacement.Bottom, "Below the games"),
    ];

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public double ToastSeconds { get => _toastSeconds; set => SetProperty(ref _toastSeconds, Math.Round(value)); }

    public ToastPlacement Placement { get => _placement; set => SetProperty(ref _placement, value); }

    public bool SoundsEnabled { get => _soundsEnabled; set => SetProperty(ref _soundsEnabled, value); }

    public double SoundVolumePercent { get => _soundVolumePercent; set => SetProperty(ref _soundVolumePercent, Math.Round(value)); }

    public bool SpeechEnabled { get => _speechEnabled; set => SetProperty(ref _speechEnabled, value); }

    public string Voice { get => _voice; set => SetProperty(ref _voice, value); }

    public int SpeechRate { get => _speechRate; set => SetProperty(ref _speechRate, value); }

    public bool QuietHoursEnabled { get => _quietHoursEnabled; set => SetProperty(ref _quietHoursEnabled, value); }

    /// <summary>"HH:mm"; invalid input keeps the previous value on save.</summary>
    public string QuietFrom { get => _quietFrom; set => SetProperty(ref _quietFrom, value); }

    public string QuietTo { get => _quietTo; set => SetProperty(ref _quietTo, value); }

    public void Load(AppSettings settings)
    {
        var n = settings.Notifications;
        Enabled = n.Enabled;
        foreach (var row in Rules) row.Load(n.RuleFor(row.Kind));
        ToastSeconds = n.ToastSeconds;
        Placement = n.Placement;
        SoundsEnabled = n.SoundsEnabled;
        SoundVolumePercent = n.SoundVolume * 100;
        SpeechEnabled = n.SpeechEnabled;
        Voice = string.IsNullOrEmpty(n.Voice) || !Voices.Contains(n.Voice) ? DefaultVoiceLabel : n.Voice;
        SpeechRate = n.SpeechRate;
        QuietHoursEnabled = n.QuietHoursEnabled;
        QuietFrom = n.QuietFrom.ToString("HH:mm", CultureInfo.InvariantCulture);
        QuietTo = n.QuietTo.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public void Apply(AppSettings settings)
    {
        var n = settings.Notifications;
        n.Enabled = Enabled;
        foreach (var row in Rules) n.Rules[row.Kind] = row.ToRule();
        n.ToastSeconds = (int)ToastSeconds;
        n.Placement = Placement;
        n.SoundsEnabled = SoundsEnabled;
        n.SoundVolume = SoundVolumePercent / 100;
        n.SpeechEnabled = SpeechEnabled;
        n.Voice = Voice == DefaultVoiceLabel ? "" : Voice;
        n.SpeechRate = SpeechRate;
        n.QuietHoursEnabled = QuietHoursEnabled;
        if (TryParseTime(QuietFrom, out var from)) n.QuietFrom = from;
        if (TryParseTime(QuietTo, out var to)) n.QuietTo = to;
    }

    internal static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim().Replace('.', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private NotificationRuleRow Row(GameEventKind kind, string label, string description) =>
        new(kind, label, description, k => _notifications.Test(k));
}
