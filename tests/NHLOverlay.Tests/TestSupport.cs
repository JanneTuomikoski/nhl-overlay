using System.Net;
using System.Text;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

internal static class Fixtures
{
    /// <summary>Captured from the live NHL API on 2026-10-24: one FINAL, one LIVE (1st period) and ten FUT games.</summary>
    public const string LiveDay = "score_2026-10-24_live.json";

    /// <summary>Captured 2026-10-22: twelve OFF games, including two OT finals and one shootout final (CAR-EDM).</summary>
    public const string FinalDay = "score_2026-10-22_final.json";

    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static NhlApiService ApiReturning(string json) =>
        new(new HttpClient(new StubHandler(_ => Json(json))));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>In-memory NHL API: per-date game lists or failures, with a request log.</summary>
internal sealed class FakeNhlApi : INhlApiClient
{
    public Dictionary<DateOnly, IReadOnlyList<HockeyGame>> Games { get; } = [];

    public NhlApiException? Failure { get; set; }

    public List<DateOnly> Requests { get; } = [];

    public Task<IReadOnlyList<HockeyGame>> GetGamesForDateAsync(DateOnly nhlDate, CancellationToken cancellationToken)
    {
        Requests.Add(nhlDate);
        if (Failure is not null) throw Failure;
        return Task.FromResult(Games.TryGetValue(nhlDate, out var games) ? games : (IReadOnlyList<HockeyGame>)[]);
    }
}

internal static class TestGames
{
    public static HockeyGame Game(long id, GameStatus status, string rawState, DateTimeOffset start, int? away = null, int? home = null) => new()
    {
        Id = id,
        Status = status,
        RawGameState = rawState,
        StartTimeUtc = start,
        Away = new HockeyTeam { Abbrev = "AWY", Score = away },
        Home = new HockeyTeam { Abbrev = "HOM", Score = home },
    };
}

internal static class Zones
{
    public static TimeZoneInfo Helsinki { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    public static TimeZoneInfo Eastern { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
}
