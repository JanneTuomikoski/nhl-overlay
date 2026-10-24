using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

public enum ConnectionState
{
    Connecting,
    Ok,
    Stale,
    Error,
}

public sealed class OverlayViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly Dictionary<long, GameCardViewModel> _cards = [];
    private ScoreboardSnapshot _snapshot = ScoreboardSnapshot.Empty;
    private double _backgroundOpacity;
    private double _uiScale;
    private ConnectionState _connection;
    private string _statusText = "";
    private string _statusToolTip = "";
    private string? _message;

    public OverlayViewModel(GameStateService state, SettingsService settings, Dispatcher dispatcher)
    {
        _settings = settings;
        state.Changed += (_, snapshot) => dispatcher.InvokeAsync(() => Apply(snapshot));
        settings.Changed += (_, _) => dispatcher.InvokeAsync(() =>
        {
            ApplyAppearance();
            Apply(_snapshot);
        });
        ApplyAppearance();
        Apply(state.Current);
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public double BackgroundOpacity { get => _backgroundOpacity; private set => SetProperty(ref _backgroundOpacity, value); }

    public double UiScale { get => _uiScale; private set => SetProperty(ref _uiScale, value); }

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

    private void ApplyAppearance()
    {
        var appearance = _settings.Current.Appearance;
        BackgroundOpacity = appearance.BackgroundOpacity;
        UiScale = appearance.UiScale;
    }

    private void Apply(ScoreboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        var now = DateTimeOffset.UtcNow;
        var culture = CultureInfo.CurrentCulture;
        var appearance = _settings.Current.Appearance;
        var ordered = GameOrdering.Sort(snapshot.Games);

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
                card = new GameCardViewModel(game.Id);
                _cards[game.Id] = card;
            }

            card.Update(game, appearance, now, TimeZoneInfo.Local, culture);
            var index = Games.IndexOf(card);
            if (index < 0) Games.Insert(i, card);
            else if (index != i) Games.Move(index, i);
        }

        Connection = (snapshot.HasData, snapshot.IsStale, snapshot.LastError) switch
        {
            (false, _, null) => ConnectionState.Connecting,
            (false, _, _) => ConnectionState.Error,
            (true, true, _) => ConnectionState.Stale,
            _ => ConnectionState.Ok,
        };

        string Time(DateTimeOffset value, string format = "t") => value.ToLocalTime().ToString(format, culture);

        Message = Connection switch
        {
            ConnectionState.Connecting => "Loading NHL scores...",
            ConnectionState.Error => snapshot.NextRefreshUtc is { } next
                ? $"NHL scores unavailable. Retrying at {Time(next)}."
                : "NHL scores unavailable.",
            _ when Games.Count == 0 => "No NHL games right now.",
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
}
