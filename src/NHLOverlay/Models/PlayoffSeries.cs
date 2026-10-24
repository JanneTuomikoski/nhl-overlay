namespace NHLOverlay.Models;

/// <summary>One playoff series from the bracket ("R1 BUF 4-2 BOS").</summary>
public sealed record PlayoffSeries(
    int Round,
    string Abbrev,
    string Title,
    string? TopSeed,
    int TopSeedWins,
    string? BottomSeed,
    int BottomSeedWins,
    string? Winner)
{
    public bool IsDecided => Winner is not null;

    public bool HasTeams => TopSeed is not null && BottomSeed is not null;

    public bool Involves(IReadOnlySet<string> teams) =>
        (TopSeed is not null && teams.Contains(TopSeed)) || (BottomSeed is not null && teams.Contains(BottomSeed));
}
