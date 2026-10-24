using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));

    private SettingsService CreateService() => new(_directory, isPortable: false);

    private void WriteSettingsFile(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, SettingsService.FileName), json);
    }

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var service = CreateService();
        service.Load();

        Assert.Equal(30, service.Current.General.RefreshIntervalSeconds);
        Assert.True(service.Current.General.ShowOverlayOnStartup);
        Assert.Null(service.LoadWarning);
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var service = CreateService();
        service.Update(s =>
        {
            s.General.RefreshIntervalSeconds = 60;
            s.Appearance.BackgroundOpacity = 0.5;
            s.Appearance.ShowShotsOnGoal = true;
            s.Window.Left = 100;
            s.Window.Top = -1080; // monitor above the primary one
        });

        var reloaded = CreateService();
        reloaded.Load();

        Assert.Equal(60, reloaded.Current.General.RefreshIntervalSeconds);
        Assert.Equal(0.5, reloaded.Current.Appearance.BackgroundOpacity);
        Assert.True(reloaded.Current.Appearance.ShowShotsOnGoal);
        Assert.Equal(100, reloaded.Current.Window.Left);
        Assert.Equal(-1080, reloaded.Current.Window.Top);
        Assert.False(File.Exists(Path.Combine(_directory, SettingsService.FileName + ".tmp")));
    }

    [Fact]
    public void Update_raises_changed_with_new_settings()
    {
        var service = CreateService();
        AppSettings? received = null;
        service.Changed += (_, s) => received = s;

        service.Update(s => s.General.RefreshIntervalSeconds = 120);

        Assert.Equal(120, received?.General.RefreshIntervalSeconds);
        Assert.Same(received, service.Current);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_backed_up()
    {
        WriteSettingsFile("{ this is not json");
        var service = CreateService();
        service.Load();

        Assert.Equal(30, service.Current.General.RefreshIntervalSeconds);
        Assert.NotNull(service.LoadWarning);
        Assert.Single(Directory.GetFiles(_directory, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Invalid_section_resets_only_that_section()
    {
        WriteSettingsFile("""
            {
              "general": { "refreshIntervalSeconds": "not a number" },
              "appearance": { "backgroundOpacity": 0.4 }
            }
            """);
        var service = CreateService();
        service.Load();

        Assert.Equal(30, service.Current.General.RefreshIntervalSeconds);
        Assert.Equal(0.4, service.Current.Appearance.BackgroundOpacity);
        Assert.Contains("general", service.LoadWarning);
    }

    [Fact]
    public void Older_file_without_new_properties_gets_defaults_and_unknown_properties_are_ignored()
    {
        WriteSettingsFile("""
            {
              // comments and trailing commas are tolerated in hand-edited files
              "general": { "refreshIntervalSeconds": 15, "removedSetting": true, },
              "futureSection": { "x": 1 },
            }
            """);
        var service = CreateService();
        service.Load();

        Assert.Equal(15, service.Current.General.RefreshIntervalSeconds);
        Assert.True(service.Current.Appearance.ShowGameClock);
        Assert.Equal(300, service.Current.Window.Width);
        Assert.Null(service.LoadWarning);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(45, 30)]
    [InlineData(50, 60)]
    [InlineData(10_000, 120)]
    public void Refresh_interval_snaps_to_a_supported_value(int stored, int expected)
    {
        WriteSettingsFile($$"""{ "general": { "refreshIntervalSeconds": {{stored}} } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(expected, service.Current.General.RefreshIntervalSeconds);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        WriteSettingsFile("""{ "appearance": { "backgroundOpacity": 7, "uiScale": 0.1 }, "window": { "width": 5 } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(1.0, service.Current.Appearance.BackgroundOpacity);
        Assert.Equal(0.75, service.Current.Appearance.UiScale);
        Assert.Equal(200, service.Current.Window.Width);
    }

    [Fact]
    public void Reset_restores_defaults()
    {
        var service = CreateService();
        service.Update(s =>
        {
            s.General.RefreshIntervalSeconds = 120;
            s.Window.Left = 50;
        });

        service.ResetToDefaults();

        Assert.Equal(30, service.Current.General.RefreshIntervalSeconds);
        Assert.Null(service.Current.Window.Left);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
