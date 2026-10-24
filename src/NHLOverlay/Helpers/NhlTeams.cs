using NHLOverlay.Models;

namespace NHLOverlay.Helpers;

/// <summary>
/// Built-in list of the 32 NHL teams (2026-27), used until the live standings have been loaded so the
/// team picker works offline on first run. Live standings data always takes precedence.
/// </summary>
public static class NhlTeams
{
    public static IReadOnlyList<TeamStanding> BuiltIn { get; } =
    [
        Team("ANA", "Anaheim Ducks", "Ducks", "W", "P"),
        Team("BOS", "Boston Bruins", "Bruins", "E", "A"),
        Team("BUF", "Buffalo Sabres", "Sabres", "E", "A"),
        Team("CAR", "Carolina Hurricanes", "Hurricanes", "E", "M"),
        Team("CBJ", "Columbus Blue Jackets", "Blue Jackets", "E", "M"),
        Team("CGY", "Calgary Flames", "Flames", "W", "P"),
        Team("CHI", "Chicago Blackhawks", "Blackhawks", "W", "C"),
        Team("COL", "Colorado Avalanche", "Avalanche", "W", "C"),
        Team("DAL", "Dallas Stars", "Stars", "W", "C"),
        Team("DET", "Detroit Red Wings", "Red Wings", "E", "A"),
        Team("EDM", "Edmonton Oilers", "Oilers", "W", "P"),
        Team("FLA", "Florida Panthers", "Panthers", "E", "A"),
        Team("LAK", "Los Angeles Kings", "Kings", "W", "P"),
        Team("MIN", "Minnesota Wild", "Wild", "W", "C"),
        Team("MTL", "Montréal Canadiens", "Canadiens", "E", "A"),
        Team("NJD", "New Jersey Devils", "Devils", "E", "M"),
        Team("NSH", "Nashville Predators", "Predators", "W", "C"),
        Team("NYI", "New York Islanders", "Islanders", "E", "M"),
        Team("NYR", "New York Rangers", "Rangers", "E", "M"),
        Team("OTT", "Ottawa Senators", "Senators", "E", "A"),
        Team("PHI", "Philadelphia Flyers", "Flyers", "E", "M"),
        Team("PIT", "Pittsburgh Penguins", "Penguins", "E", "M"),
        Team("SEA", "Seattle Kraken", "Kraken", "W", "P"),
        Team("SJS", "San Jose Sharks", "Sharks", "W", "P"),
        Team("STL", "St. Louis Blues", "Blues", "W", "C"),
        Team("TBL", "Tampa Bay Lightning", "Lightning", "E", "A"),
        Team("TOR", "Toronto Maple Leafs", "Maple Leafs", "E", "A"),
        Team("UTA", "Utah Mammoth", "Mammoth", "W", "C"),
        Team("VAN", "Vancouver Canucks", "Canucks", "W", "P"),
        Team("VGK", "Vegas Golden Knights", "Golden Knights", "W", "P"),
        Team("WPG", "Winnipeg Jets", "Jets", "W", "C"),
        Team("WSH", "Washington Capitals", "Capitals", "E", "M"),
    ];

    public static string DivisionName(string code) => code switch
    {
        "A" => "Atlantic",
        "M" => "Metropolitan",
        "C" => "Central",
        "P" => "Pacific",
        _ => code,
    };

    public static string ConferenceName(string code) => code switch
    {
        "E" => "Eastern",
        "W" => "Western",
        _ => code,
    };

    private static TeamStanding Team(string abbrev, string fullName, string commonName, string conference, string division) => new()
    {
        Abbrev = abbrev,
        FullName = fullName,
        CommonName = commonName,
        LogoUrl = $"https://assets.nhle.com/logos/nhl/svg/{abbrev}_light.svg",
        Conference = conference,
        ConferenceName = ConferenceName(conference),
        Division = division,
        DivisionName = DivisionName(division),
    };
}
