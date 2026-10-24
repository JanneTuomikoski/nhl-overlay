using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using NHLOverlay.Models;
using NHLOverlay.Services.Api;

namespace NHLOverlay.Services;

/// <summary>Finnish Liiga games for one (Finnish) calendar date.</summary>
public interface ILiigaClient
{
    Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP client for https://liiga.fi/api/v2/ (public, no key, not an officially documented API). Uses
/// <c>games?date=yyyy-MM-dd</c>: one small response per date with live score, clock and goal events.
/// </summary>
public sealed class LiigaApiService : ILiigaClient, IDisposable
{
    public static readonly Uri BaseAddress = new("https://liiga.fi/api/v2/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public LiigaApiService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _http = httpClient ?? CreateDefaultClient();
        _http.BaseAddress ??= BaseAddress;
    }

    public async Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var path = "games?date=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new NhlApiException($"Liiga API returned {(int)response.StatusCode} {response.ReasonPhrase} for /{path}.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync<LiigaDayDto>(stream, JsonOptions, timeout.Token).ConfigureAwait(false);
            return LiigaMapper.MapDay(dto);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NhlApiException($"Liiga API request timed out (/{path}).");
        }
        catch (HttpRequestException ex)
        {
            throw new NhlApiException($"Network error contacting the Liiga API: {ex.Message}", inner: ex);
        }
        catch (IOException ex)
        {
            throw new NhlApiException($"Network error reading the Liiga API response: {ex.Message}", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new NhlApiException($"Unexpected response format from Liiga /{path}: {ex.Message}", inner: ex);
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
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
