namespace NHLOverlay.Models;

/// <summary>Follow players from chosen countries (Finland by default) across all games.</summary>
public sealed class CountryWatchSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>ISO 3166-1 alpha-3 codes as used by the NHL, e.g. "FIN", "SWE".</summary>
    public List<string> Countries { get; set; } = ["FIN"];

    /// <summary>Show watched players' points on the game cards.</summary>
    public bool ShowOnCards { get; set; } = true;

    /// <summary>Show the "Finns tonight" summary above the games.</summary>
    public bool ShowPanel { get; set; } = true;

    internal CountryWatchSettings Normalize()
    {
        Countries = AppSettings.NormalizeTeamList(Countries).Where(c => c.Length == 3).ToList();
        return this;
    }
}

/// <summary>Display names for the hockey countries offered in settings.</summary>
public static class HockeyCountries
{
    public static IReadOnlyList<(string Code, string Name, string Adjective, string Plural)> All { get; } =
    [
        ("FIN", "Finland", "Finnish", "Finns"),
        ("SWE", "Sweden", "Swedish", "Swedes"),
        ("CZE", "Czechia", "Czech", "Czechs"),
        ("SVK", "Slovakia", "Slovak", "Slovaks"),
        ("CHE", "Switzerland", "Swiss", "Swiss"),
        ("DEU", "Germany", "German", "Germans"),
        ("DNK", "Denmark", "Danish", "Danes"),
        ("NOR", "Norway", "Norwegian", "Norwegians"),
        ("LVA", "Latvia", "Latvian", "Latvians"),
        ("AUT", "Austria", "Austrian", "Austrians"),
        ("RUS", "Russia", "Russian", "Russians"),
        ("USA", "United States", "American", "Americans"),
        ("CAN", "Canada", "Canadian", "Canadians"),
    ];

    public static string Adjective(string code) => All.FirstOrDefault(c => c.Code == code).Adjective ?? code;

    /// <summary>"FINNS" for one country, "WATCHED PLAYERS" for several.</summary>
    public static string PanelTitle(IReadOnlyList<string> codes) =>
        codes.Count == 1 && All.FirstOrDefault(c => c.Code == codes[0]).Plural is { } plural
            ? plural.ToUpperInvariant()
            : "WATCHED PLAYERS";
}
