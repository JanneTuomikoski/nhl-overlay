using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using NHLOverlay.Models;
using NHLOverlay.Services.Api;

namespace NHLOverlay.Services;

/// <summary>Abstraction over the NHL web API so polling logic can be tested without the network.</summary>
public interface INhlApiClient
{
    /// <summary>All games on one NHL schedule date, including clock, shots and goals.</summary>
    Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly nhlDate, CancellationToken cancellationToken);

    /// <summary>Current standings for all teams (empty in the off-season).</summary>
    Task<IReadOnlyList<TeamStanding>> GetStandingsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>A team's current roster with each player's birth country.</summary>
    Task<IReadOnlyList<RosterPlayer>> GetRosterAsync(string teamAbbrev, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>Goalie lines for one game.</summary>
    Task<GameBoxscore> GetBoxscoreAsync(long gameId, string awayAbbrev, string homeAbbrev, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>A team's full current-season schedule (past games with results, future games with start times).</summary>
    Task<IReadOnlyList<HockeyGame>> GetTeamScheduleAsync(string teamAbbrev, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>Playoff series for the season ending in <paramref name="year"/>.</summary>
    Task<IReadOnlyList<PlayoffSeries>> GetPlayoffBracketAsync(int year, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>Every recorded event of one game (shots with coordinates, penalties, goals, ...).</summary>
    Task<PlayByPlay> GetPlayByPlayAsync(long gameId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>
    /// Career totals and recent games. The season game log is fetched as well only when the last five games all
    /// had points, because only then can the streak be longer than five.
    /// </summary>
    Task<PlayerCareer> GetPlayerCareerAsync(long playerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Failure talking to the NHL API. <see cref="RetryAfter"/> is set when the server asked us to back off.</summary>
public sealed class NhlApiException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// HTTP client for https://api-web.nhle.com/v1/ (public, no API key, not an officially supported API).
/// Uses <c>/score/{date}</c> because, unlike <c>/scoreboard/now</c>, it includes the game clock,
/// shots on goal and the per-game goal list.
/// </summary>
public sealed class NhlApiService : INhlApiClient, IDisposable
{
    public static readonly Uri BaseAddress = new("https://api-web.nhle.com/v1/");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public NhlApiService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _http = httpClient ?? CreateDefaultClient();
        _http.BaseAddress ??= BaseAddress;
    }

    public async Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly nhlDate, CancellationToken cancellationToken)
    {
        var path = "score/" + nhlDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dto = await GetJsonAsync<ScoreResponseDto>(path, cancellationToken).ConfigureAwait(false);
        return NhlMapper.MapGames(dto?.Games);
    }

    public async Task<IReadOnlyList<TeamStanding>> GetStandingsAsync(CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<StandingsResponseDto>("standings/now", cancellationToken).ConfigureAwait(false);
        return NhlMapper.MapStandings(dto?.Standings);
    }

    public async Task<IReadOnlyList<RosterPlayer>> GetRosterAsync(string teamAbbrev, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<RosterDto>($"roster/{Uri.EscapeDataString(teamAbbrev)}/current", cancellationToken).ConfigureAwait(false);
        return NhlMapper.MapRoster(dto, teamAbbrev);
    }

    public async Task<GameBoxscore> GetBoxscoreAsync(long gameId, string awayAbbrev, string homeAbbrev, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<BoxscoreDto>($"gamecenter/{gameId}/boxscore", cancellationToken).ConfigureAwait(false)
                  ?? throw new NhlApiException($"Empty boxscore for game {gameId}.");
        return NhlMapper.MapBoxscore(dto, awayAbbrev, homeAbbrev);
    }

    public async Task<IReadOnlyList<HockeyGame>> GetTeamScheduleAsync(string teamAbbrev, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<ClubScheduleDto>($"club-schedule-season/{Uri.EscapeDataString(teamAbbrev)}/now", cancellationToken).ConfigureAwait(false);
        return NhlMapper.MapGames(dto?.Games);
    }

    public async Task<IReadOnlyList<PlayoffSeries>> GetPlayoffBracketAsync(int year, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<BracketDto>($"playoff-bracket/{year.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        return NhlMapper.MapBracket(dto);
    }

    public async Task<PlayByPlay> GetPlayByPlayAsync(long gameId, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<PlayByPlayDto>($"gamecenter/{gameId}/play-by-play", cancellationToken).ConfigureAwait(false)
                  ?? throw new NhlApiException($"Empty play-by-play for game {gameId}.");
        return NhlMapper.MapPlayByPlay(dto);
    }

    public async Task<PlayerCareer> GetPlayerCareerAsync(long playerId, CancellationToken cancellationToken)
    {
        var id = playerId.ToString(CultureInfo.InvariantCulture);
        var landing = await GetJsonAsync<PlayerLandingDto>($"player/{id}/landing", cancellationToken).ConfigureAwait(false)
                      ?? throw new NhlApiException($"Empty player profile for {playerId}.");
        IReadOnlyList<GameLogDto>? gameLog = null;
        if (landing.Last5Games is { Count: 5 } last5 && last5.All(g => (g.Goals ?? 0) + (g.Assists ?? 0) > 0))
            gameLog = (await GetJsonAsync<GameLogResponseDto>($"player/{id}/game-log/now", cancellationToken).ConfigureAwait(false))?.GameLog;
        return NhlMapper.MapPlayer(landing, gameLog, DateTimeOffset.UtcNow);
    }

    internal async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new NhlApiException(
                    $"NHL API returned {(int)response.StatusCode} {response.ReasonPhrase} for /{path}.",
                    GetRetryAfter(response));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NhlApiException($"NHL API request timed out after {RequestTimeout.TotalSeconds:0} s (/{path}).");
        }
        catch (HttpRequestException ex)
        {
            throw new NhlApiException($"Network error contacting the NHL API: {ex.Message}", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new NhlApiException($"Unexpected response format from /{path}: {ex.Message}", inner: ex);
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retry) return null;
        if (retry.Delta is { } delta) return delta;
        if (retry.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10), // pick up DNS changes
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; // per-request timeout above
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NHLOverlay/{version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
