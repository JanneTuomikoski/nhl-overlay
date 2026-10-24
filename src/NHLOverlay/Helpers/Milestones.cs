namespace NHLOverlay.Helpers;

/// <summary>Career milestones: first goal/point, 50 goals, and every 100 goals, assists or points.</summary>
public static class Milestones
{
    public static int NextGoals(int goals) => goals < 1 ? 1 : goals < 50 ? 50 : NextHundred(goals);

    public static int NextAssists(int assists) => NextHundred(assists);

    public static int NextPoints(int points) => points < 1 ? 1 : NextHundred(points);

    private static int NextHundred(int value) => ((value / 100) + 1) * 100;

    /// <summary>Milestone values passed when a total goes from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static IEnumerable<int> Crossed(int before, int after, Func<int, int> next)
    {
        for (var milestone = next(before); milestone <= after; milestone = next(milestone))
            yield return milestone;
    }

    /// <summary>"1st", "500th" etc. for career milestones (first goal reads "first").</summary>
    public static string Describe(int value, string stat) =>
        value == 1 ? $"first NHL {stat.TrimEnd('s')}" : $"{PlayoffRace.Ordinal(value)} career {stat.TrimEnd('s')}";
}
