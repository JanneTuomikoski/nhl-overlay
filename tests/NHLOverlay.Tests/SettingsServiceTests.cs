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
            s.Appearance.DisplayMode = DisplayMode.Expanded;
            s.Window.ScreenX = 100;
            s.Window.ScreenY = -1080; // monitor above the primary one
            s.Window.ClickThrough = true;
            s.Hotkeys.ToggleOverlay = "Ctrl+Shift+F10";
        });

        var reloaded = CreateService();
        reloaded.Load();

        Assert.Equal(60, reloaded.Current.General.RefreshIntervalSeconds);
        Assert.Equal(0.5, reloaded.Current.Appearance.BackgroundOpacity);
        Assert.True(reloaded.Current.Appearance.ShowShotsOnGoal);
        Assert.Equal(DisplayMode.Expanded, reloaded.Current.Appearance.DisplayMode);
        Assert.Equal(100, reloaded.Current.Window.ScreenX);
        Assert.Equal(-1080, reloaded.Current.Window.ScreenY);
        Assert.True(reloaded.Current.Window.ClickThrough);
        Assert.Equal("Ctrl+Shift+F10", reloaded.Current.Hotkeys.ToggleOverlay);
        Assert.Equal("Ctrl+Alt+I", reloaded.Current.Hotkeys.ToggleInteraction);
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
    public void Preview_applies_in_memory_without_saving_and_skips_no_op_changes()
    {
        var service = CreateService();
        service.Update(s => s.Appearance.UiScale = 1.5);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.True(service.Preview(s => s.Appearance.UiScale = 2.0));
        Assert.False(service.Preview(s => s.Appearance.UiScale = 2.0));

        Assert.Equal(1, changes);
        Assert.Equal(2.0, service.Current.Appearance.UiScale);
        var reloaded = CreateService();
        reloaded.Load();
        Assert.Equal(1.5, reloaded.Current.Appearance.UiScale); // the file still has the saved value
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
        WriteSettingsFile("""{ "appearance": { "backgroundOpacity": 7, "uiScale": 0.1, "goalFlashSeconds": 600 }, "window": { "width": 5 } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(1.0, service.Current.Appearance.BackgroundOpacity);
        Assert.Equal(0.75, service.Current.Appearance.UiScale);
        Assert.Equal(60, service.Current.Appearance.GoalFlashSeconds);
        Assert.Equal(200, service.Current.Window.Width);
    }

    [Fact]
    public void Ui_scale_allows_large_monitors_up_to_300_percent()
    {
        WriteSettingsFile("""{ "appearance": { "uiScale": 2.5 } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(2.5, service.Current.Appearance.UiScale);
    }

    [Fact]
    public void Unknown_display_mode_resets_only_the_appearance_section()
    {
        WriteSettingsFile("""{ "appearance": { "displayMode": "Huge" }, "general": { "refreshIntervalSeconds": 60 } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(DisplayMode.Compact, service.Current.Appearance.DisplayMode);
        Assert.Equal(60, service.Current.General.RefreshIntervalSeconds);
        Assert.Contains("appearance", service.LoadWarning);
    }

    [Fact]
    public void Export_and_import_round_trip_but_keep_local_window_positions()
    {
        var source = CreateService();
        source.Update(s =>
        {
            s.Teams.Favorites = ["EDM", "NJD"];
            s.General.ShowLiiga = true;
            s.Window.ScreenX = 4000;
        });
        var file = Path.Combine(_directory, "export.json");
        source.Export(file);

        var target = new SettingsService(Path.Combine(_directory, "other"), isPortable: false);
        target.Update(s => s.Window.ScreenX = -1200);
        Assert.Null(target.Import(file));

        Assert.Equal(["EDM", "NJD"], target.Current.Teams.Favorites);
        Assert.True(target.Current.General.ShowLiiga);
        Assert.Equal(-1200, target.Current.Window.ScreenX);
    }

    [Fact]
    public void Import_rejects_a_file_that_is_not_settings()
    {
        WriteSettingsFile("{}");
        var service = CreateService();
        var file = Path.Combine(_directory, "garbage.json");
        File.WriteAllText(file, "not json at all");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => service.Import(file));
    }

    [Fact]
    public void Team_settings_are_normalized()
    {
        WriteSettingsFile("""{ "teams": { "favorites": [" edm", "EDM", "", "tor", "../x", "A", "NJ1", "TOOLONG", "kär"], "filter": "Live", "sortOrder": "StartTime" } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(["EDM", "TOR", "KÄR"], service.Current.Teams.Favorites);
        Assert.Equal(GameFilter.Live, service.Current.Teams.Filter);
        Assert.Equal(GameSortOrder.StartTime, service.Current.Teams.SortOrder);
        Assert.Equal(FavoriteHighlight.BarAndTint, service.Current.Teams.Highlight);
    }

    [Fact]
    public void Collapsed_sections_are_normalized()
    {
        WriteSettingsFile("""{ "appearance": { "collapsedSections": [" liiga", "LIIGA", "SHL", null] } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(["LIIGA"], service.Current.Appearance.CollapsedSections);
    }

    [Theory]
    [InlineData("Ctrl+Alt+N", "Ctrl+Alt+N")]
    [InlineData("alt + ctrl + n", "Ctrl+Alt+N")]
    [InlineData("", "")] // disabled
    [InlineData("N", "Ctrl+Alt+N")] // invalid -> default
    public void Hotkeys_are_validated(string stored, string expected)
    {
        WriteSettingsFile($$"""{ "hotkeys": { "toggleOverlay": "{{stored}}" } }""");
        var service = CreateService();
        service.Load();

        Assert.Equal(expected, service.Current.Hotkeys.ToggleOverlay);
    }

    [Fact]
    public void Reset_restores_defaults()
    {
        var service = CreateService();
        service.Update(s =>
        {
            s.General.RefreshIntervalSeconds = 120;
            s.Window.ScreenX = 50;
            s.Hotkeys.ToggleOverlay = "";
        });

        service.ResetToDefaults();

        Assert.Equal(30, service.Current.General.RefreshIntervalSeconds);
        Assert.Null(service.Current.Window.ScreenX);
        Assert.Equal("Ctrl+Alt+N", service.Current.Hotkeys.ToggleOverlay);
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
