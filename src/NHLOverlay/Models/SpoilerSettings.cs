namespace NHLOverlay.Models;

/// <summary>Hide scores of games you'll watch later.</summary>
public sealed class SpoilerSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Teams whose games are always hidden until revealed (e.g. games you watch as a recording).</summary>
    public List<string> AlwaysShieldTeams { get; set; } = [];

    internal SpoilerSettings Normalize()
    {
        AlwaysShieldTeams = AppSettings.NormalizeTeamList(AlwaysShieldTeams);
        return this;
    }
}

/// <summary>The "last night" summary shown in the morning.</summary>
public sealed class RecapSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Local hour until which the recap stays on the today view (it appears at the 08:00 rollover).</summary>
    public int ShowUntilHour { get; set; } = 14;

    /// <summary>Also show the recap above yesterday's games.</summary>
    public bool ShowOnYesterday { get; set; } = true;

    internal RecapSettings Normalize()
    {
        ShowUntilHour = Math.Clamp(ShowUntilHour, 9, 23);
        return this;
    }
}
