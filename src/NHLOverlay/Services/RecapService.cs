using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Decides when the morning recap is shown and builds it: on the today view from the 08:00 rollover until the
/// configured hour (unless dismissed for that date), and above yesterday's games when browsing back. Requests
/// boxscores for shutouts so the goalie can be named. UI thread only.
/// </summary>
public sealed class RecapService
{
    private readonly SettingsService _settings;
    private readonly SpoilerService _spoilers;
    private readonly BoxscoreService _boxscores;
    private readonly CountryWatchService _countryWatch;
    private readonly AppStateService _state;
    private readonly TimeZoneInfo _localZone;

    public RecapService(
        SettingsService settings,
        SpoilerService spoilers,
        BoxscoreService boxscores,
        CountryWatchService countryWatch,
        AppStateService state,
        TimeZoneInfo? localZone = null)
    {
        _settings = settings;
        _spoilers = spoilers;
        _boxscores = boxscores;
        _countryWatch = countryWatch;
        _state = state;
        _localZone = localZone ?? TimeZoneInfo.Local;
    }

    /// <summary>The recap to show above the given day's games, or null.</summary>
    public Recap? For(ScoreboardSnapshot snapshot, int dayOffset, DateTimeOffset now)
    {
        var settings = _settings.Current;
        if (!settings.Recap.Enabled || snapshot.TodayNhlDate == default) return null;

        var lastNight = snapshot.TodayNhlDate.AddDays(-1);
        var show = dayOffset switch
        {
            0 => IsMorning(now, settings.Recap) && _state.State.RecapDismissedFor != lastNight,
            -1 => settings.Recap.ShowOnYesterday,
            _ => false,
        };
        if (!show || !snapshot.GamesByDate.TryGetValue(lastNight, out var games)) return null;

        foreach (var shutout in games.Where(g => g.Status.IsFinal() && (g.Away.Score == 0 || g.Home.Score == 0)))
            _boxscores.Request(shutout);

        var recap = RecapBuilder.Build(
            lastNight,
            games,
            settings.Teams.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _spoilers.IsShielded,
            _boxscores.Get,
            _countryWatch.PlayersFor,
            settings.CountryWatch.Enabled ? settings.CountryWatch.Countries : []);
        return recap.IsEmpty ? null : recap;
    }

    public void Dismiss(DateOnly date) => _state.Update(s => s.RecapDismissedFor = date);

    /// <summary>Between the 08:00 hockey-day rollover and the configured hour, local time.</summary>
    private bool IsMorning(DateTimeOffset now, RecapSettings settings)
    {
        var hour = TimeZoneInfo.ConvertTime(now, _localZone).Hour;
        return hour >= NhlCalendar.HockeyDayStartHour && hour < settings.ShowUntilHour;
    }
}
