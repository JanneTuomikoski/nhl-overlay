using System.Globalization;
using System.Windows.Media;
using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels;

/// <summary>A row in a division or wildcard table.</summary>
public sealed class StandingsRowViewModel(string rank, TeamStanding team, bool isFavorite, bool cutLineAfter) : ObservableObject
{
    private ImageSource? _logo;

    public string Rank { get; } = rank;

    public string Abbrev { get; } = team.Abbrev;

    public string Name { get; } = team.FullName;

    public string GamesPlayed { get; } = team.GamesPlayed.ToString(CultureInfo.InvariantCulture);

    public string Points { get; } = team.Points.ToString(CultureInfo.InvariantCulture);

    public string Record { get; } = team.Record;

    public string Differential { get; } = team.GoalDifferential > 0 ? $"+{team.GoalDifferential}" : team.GoalDifferential.ToString(CultureInfo.InvariantCulture);

    public string LastTen { get; } = team.LastTen;

    public bool IsFavorite { get; } = isFavorite;

    /// <summary>Draw the playoff cut line below this row.</summary>
    public bool CutLineAfter { get; } = cutLineAfter;

    public ImageSource? Logo { get => _logo; set => SetProperty(ref _logo, value); }
}

public sealed record StandingsTable(string Title, IReadOnlyList<StandingsRowViewModel> Rows);

/// <summary>A favorite team's summary: position, record, form, race and next games.</summary>
public sealed class TeamSummaryViewModel(TeamStanding team, RaceStatus race, IReadOnlyList<string> nextGames) : ObservableObject
{
    private ImageSource? _logo;

    public string Abbrev { get; } = team.Abbrev;

    public string Name { get; } = team.FullName;

    public string Position { get; } = race.Position;

    public bool InPlayoffSpot { get; } = race.InPlayoffSpot;

    public string Race { get; } = race.Detail;

    public string RecordLine { get; } = team.HasStats ? $"{team.Points} pts · {team.GamesPlayed} GP · {team.Record}" : "No games yet";

    public string FormLine { get; } = string.Join(" · ", new[]
    {
        team.LastTen.Length > 0 ? $"L10 {team.LastTen}" : null,
        team.Streak.Length > 0 ? $"Streak {team.Streak}" : null,
    }.Where(s => s is not null));

    public IReadOnlyList<string> NextGames { get; } = nextGames;

    public bool HasNextGames => NextGames.Count > 0;

    public ImageSource? Logo { get => _logo; set => SetProperty(ref _logo, value); }
}

/// <summary>
/// The standings view: favorite teams' race and next games, their divisions and conference wildcards (all four
/// divisions when no favorites are set), and the playoff bracket when it is in use.
/// </summary>
public sealed class StandingsViewModel : ObservableObject
{
    private const int NextGameCount = 5;
    private const int WildcardRowsShown = 6;

    private readonly TeamDirectoryService _teams;
    private readonly ScheduleService _schedules;
    private readonly LogoService _logos;
    private string _inputs = "";
    private IReadOnlyList<TeamSummaryViewModel> _favorites = [];
    private IReadOnlyList<StandingsTable> _divisions = [];
    private IReadOnlyList<StandingsTable> _wildcards = [];
    private IReadOnlyList<string> _series = [];
    private string? _message;

    public StandingsViewModel(TeamDirectoryService teams, ScheduleService schedules, LogoService logos)
    {
        _teams = teams;
        _schedules = schedules;
        _logos = logos;
    }

    public IReadOnlyList<TeamSummaryViewModel> Favorites { get => _favorites; private set => SetProperty(ref _favorites, value); }

    public IReadOnlyList<StandingsTable> Divisions { get => _divisions; private set => SetProperty(ref _divisions, value); }

    public IReadOnlyList<StandingsTable> Wildcards { get => _wildcards; private set => SetProperty(ref _wildcards, value); }

    /// <summary>Playoff series lines ("R1  BUF 4-2 BOS"), current round first.</summary>
    public IReadOnlyList<string> Series
    {
        get => _series;
        private set
        {
            if (SetProperty(ref _series, value)) OnPropertyChanged(nameof(HasSeries));
        }
    }

    public bool HasSeries => Series.Count > 0;

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message is not null;

    /// <summary>Requests what the view needs and rebuilds it when any input changed. UI thread.</summary>
    public void Update(AppSettings settings, ScoreboardSnapshot snapshot, DateTimeOffset now, TimeZoneInfo localZone, CultureInfo culture)
    {
        var favorites = settings.Teams.Favorites.Where(f => _teams.Find(f) is not null).ToList();
        var playoffYear = PlayoffYear(snapshot);
        _schedules.Ensure(favorites, playoffYear);

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, localZone).DateTime);
        var inputs = $"{_teams.UpdatedUtc}|{string.Join(",", favorites)}|{_schedules.Version}|{localDate}|{playoffYear}";
        if (inputs == _inputs) return;
        _inputs = inputs;

        var all = _teams.Teams;
        if (!all.Any(t => t.HasStats))
        {
            Message = "Standings are loading (or the season has not started).";
            Favorites = [];
            Divisions = [];
            Wildcards = [];
            Series = [];
            return;
        }

        Message = null;
        var favoriteSet = favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Favorites = favorites.Select(f => _teams.Find(f)!).Select(team =>
        {
            var next = _schedules.Upcoming(team.Abbrev, now, NextGameCount)
                .Select(g => $"{GameStatusFormatter.FormatStartTime(g.StartTimeUtc, localZone, now, culture)}  {(g.Home.Abbrev == team.Abbrev ? "vs " + g.Away.Abbrev : "@ " + g.Home.Abbrev)}")
                .ToList();
            var summary = new TeamSummaryViewModel(team, PlayoffRace.Describe(team, all), next);
            _ = LoadLogoAsync(team.LogoUrl, logo => summary.Logo = logo);
            return summary;
        }).ToList();

        var divisions = favorites.Count > 0
            ? favorites.Select(f => _teams.Find(f)!.Division).Distinct().ToList()
            : ["A", "M", "C", "P"];
        Divisions = divisions
            .Select(d => PlayoffRace.Division(all, d))
            .Where(rows => rows.Count > 0)
            .Select(rows => new StandingsTable(
                rows[0].DivisionName.ToUpperInvariant(),
                rows.Select(t => Row(t.DivisionRank.ToString(CultureInfo.InvariantCulture), t, favoriteSet, t.DivisionRank == 3)).ToList()))
            .ToList();

        var conferences = favorites.Count > 0
            ? favorites.Select(f => _teams.Find(f)!.Conference).Distinct().ToList()
            : ["E", "W"];
        Wildcards = conferences
            .Select(c => PlayoffRace.Wildcards(all, c))
            .Where(rows => rows.Count > 0)
            .Select(rows => new StandingsTable(
                $"{NhlTeams.ConferenceName(rows[0].Conference).ToUpperInvariant()} WILDCARD",
                // The first few rows around the cut line, plus favorites wherever they are.
                rows.Where((t, i) => i < WildcardRowsShown || favoriteSet.Contains(t.Abbrev))
                    .Select(t => Row($"WC{t.WildcardRank}", t, favoriteSet, t.WildcardRank == PlayoffRace.WildcardSpots))
                    .ToList()))
            .ToList();

        Series = _schedules.Bracket
            .Where(s => s.HasTeams)
            .OrderByDescending(s => s.Round)
            .ThenByDescending(s => s.Involves(favoriteSet))
            .Select(s => $"{s.Abbrev}  {s.TopSeed} {s.TopSeedWins}-{s.BottomSeedWins} {s.BottomSeed}{(s.Winner is { } w ? $"  · {w} wins" : "")}")
            .ToList();
    }

    /// <summary>Season end year when playoff games are on the schedule (April to June), else null.</summary>
    internal static int? PlayoffYear(ScoreboardSnapshot snapshot)
    {
        var date = snapshot.TodayNhlDate;
        if (date == default) return null;
        var playoffGames = snapshot.GamesByDate.Values.SelectMany(g => g).Any(g => g.GameType == 3);
        return playoffGames || date.Month is >= 4 and <= 6 ? date.Year : null;
    }

    private StandingsRowViewModel Row(string rank, TeamStanding team, IReadOnlySet<string> favorites, bool cutLineAfter)
    {
        var row = new StandingsRowViewModel(rank, team, favorites.Contains(team.Abbrev), cutLineAfter);
        _ = LoadLogoAsync(team.LogoUrl, logo => row.Logo = logo);
        return row;
    }

    private async Task LoadLogoAsync(string? url, Action<ImageSource?> set)
    {
        try
        {
            set(await _logos.GetLogoAsync(url));
        }
        catch (Exception ex)
        {
            Log.Warn($"Standings logo failed for {url}.", ex);
        }
    }
}
