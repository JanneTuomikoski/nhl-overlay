namespace NHLOverlay.Models;

/// <summary>
/// Persisted user configuration. New properties must have sensible initializers so that
/// settings files written by older versions load with defaults for anything missing.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    /// <summary>Clamps out-of-range values and replaces nulls with defaults.</summary>
    public AppSettings Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        General = (General ?? new()).Normalize();
        Appearance = (Appearance ?? new()).Normalize();
        Window = (Window ?? new()).Normalize();
        return this;
    }
}

public sealed class GeneralSettings
{
    public static readonly int[] AllowedRefreshIntervals = [15, 30, 60, 120];

    public bool LaunchWithWindows { get; set; }

    /// <summary>When false, the app starts with only the tray icon visible.</summary>
    public bool ShowOverlayOnStartup { get; set; } = true;

    public int RefreshIntervalSeconds { get; set; } = 30;

    internal GeneralSettings Normalize()
    {
        // Snap to the closest supported interval.
        RefreshIntervalSeconds = AllowedRefreshIntervals.MinBy(i => Math.Abs(i - RefreshIntervalSeconds));
        return this;
    }
}

public sealed class AppearanceSettings
{
    /// <summary>Opacity of the overlay background (text stays fully opaque). 0.0 - 1.0.</summary>
    public double BackgroundOpacity { get; set; } = 0.85;

    /// <summary>Overall UI scale factor. 0.75 - 2.0.</summary>
    public double UiScale { get; set; } = 1.0;

    public bool ShowGameClock { get; set; } = true;

    public bool ShowShotsOnGoal { get; set; }

    internal AppearanceSettings Normalize()
    {
        BackgroundOpacity = ClampOrDefault(BackgroundOpacity, 0.0, 1.0, 0.85);
        UiScale = ClampOrDefault(UiScale, 0.75, 2.0, 1.0);
        return this;
    }

    internal static double ClampOrDefault(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed class WindowSettings
{
    /// <summary>Last overlay position in WPF device-independent units. Null = default placement.</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 300;

    internal WindowSettings Normalize()
    {
        if (Left is { } l && !double.IsFinite(l)) Left = null;
        if (Top is { } t && !double.IsFinite(t)) Top = null;
        Width = AppearanceSettings.ClampOrDefault(Width, 200, 1200, 300);
        return this;
    }
}
