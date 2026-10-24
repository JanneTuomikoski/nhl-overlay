namespace NHLOverlay.Helpers;

/// <summary>
/// Liiga teams (2026-27) with short codes for compact cards. The API identifies teams as "651304385:tps";
/// unknown teams fall back to the first letters of their slug.
/// </summary>
public static class LiigaTeams
{
    public static IReadOnlyList<(string Slug, string Abbrev, string Name)> All { get; } =
    [
        ("hifk", "HIFK", "HIFK"),
        ("hpk", "HPK", "HPK"),
        ("ilves", "ILV", "Ilves"),
        ("jokerit", "JOK", "Jokerit"),
        ("jukurit", "JUK", "Jukurit"),
        ("jyp", "JYP", "JYP"),
        ("kalpa", "KAL", "KalPa"),
        ("kookoo", "KOO", "KooKoo"),
        ("kärpät", "KÄR", "Kärpät"),
        ("lukko", "LUK", "Lukko"),
        ("pelicans", "PEL", "Pelicans"),
        ("saipa", "SAI", "SaiPa"),
        ("sport", "SPO", "Sport"),
        ("tappara", "TAP", "Tappara"),
        ("tps", "TPS", "TPS"),
        ("ässät", "ÄSS", "Ässät"),
    ];

    /// <summary>"651304385:tps" -> "TPS".</summary>
    public static string Abbrev(string? teamId, string? name)
    {
        var slug = teamId?.Split(':').LastOrDefault()?.Trim().ToLowerInvariant() ?? "";
        var known = All.FirstOrDefault(t => t.Slug == slug);
        if (known.Abbrev is not null) return known.Abbrev;
        var source = slug.Length > 0 ? slug : name ?? "?";
        return source[..Math.Min(3, source.Length)].ToUpperInvariant();
    }
}
