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

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
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
