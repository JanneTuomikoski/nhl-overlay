using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as human-readable JSON.
/// <list type="bullet">
/// <item>Default location: %AppData%\NHLOverlay\settings.json.</item>
/// <item>Portable mode: if a file named <c>portable</c> (or <c>settings.json</c>) exists next to the
/// executable and that folder is writable, settings live there instead.</item>
/// <item>Each section is read independently, so one malformed section only resets that section.</item>
/// <item>Writes go to a temp file that then replaces the real file, so a crash never leaves a half-written file.</item>
/// </list>
/// </summary>
public sealed class SettingsService
{
    public const string FileName = "settings.json";
    public const string PortableMarkerFileName = "portable";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();

    public SettingsService(string settingsDirectory, bool isPortable, string? cacheDirectory = null)
    {
        SettingsDirectory = settingsDirectory;
        IsPortable = isPortable;
        CacheDirectory = cacheDirectory ?? Path.Combine(settingsDirectory, "cache");
    }

    public string SettingsDirectory { get; }

    /// <summary>Re-creatable downloads such as team logos (%LocalAppData% unless portable).</summary>
    public string CacheDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, FileName);

    public bool IsPortable { get; }

    public AppSettings Current { get; private set; } = new();

    /// <summary>Non-fatal problem found while loading (shown in the settings window).</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>Raised on the thread that saved, after <see cref="Current"/> has been replaced.</summary>
    public event EventHandler<AppSettings>? Changed;

    public static SettingsService CreateDefault()
    {
        var exeDirectory = AppContext.BaseDirectory;
        var wantsPortable = File.Exists(Path.Combine(exeDirectory, PortableMarkerFileName))
                            || File.Exists(Path.Combine(exeDirectory, FileName));
        if (wantsPortable && IsDirectoryWritable(exeDirectory))
            return new SettingsService(exeDirectory, isPortable: true);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SettingsService(Path.Combine(appData, "NHLOverlay"), isPortable: false, Path.Combine(localAppData, "NHLOverlay", "cache"));
    }

    public void Load()
    {
        lock (_gate)
        {
            LoadWarning = null;
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                return;
            }

            try
            {
                Current = Parse(File.ReadAllText(SettingsPath), out var brokenSections);
                if (brokenSections.Count > 0)
                    LoadWarning = $"Invalid values in section(s) {string.Join(", ", brokenSections)} were reset to defaults.";
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LoadWarning = "The settings file could not be read and defaults are in use. " + ex.Message;
                Log.Warn("Settings file unreadable; using defaults.", ex);
                BackUpCorruptFile();
                Current = new AppSettings();
            }

            if (LoadWarning is not null)
                Log.Warn(LoadWarning);
        }
    }

    /// <summary>
    /// Parses settings JSON section by section. Unknown properties are ignored and missing
    /// properties keep their defaults, which keeps older and newer files compatible.
    /// </summary>
    internal static AppSettings Parse(string json, out List<string> brokenSections)
    {
        brokenSections = [];
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new JsonException("Settings root must be a JSON object.");

        var settings = new AppSettings
        {
            General = ReadSection<GeneralSettings>(root, "general", brokenSections),
            Appearance = ReadSection<AppearanceSettings>(root, "appearance", brokenSections),
            Window = ReadSection<WindowSettings>(root, "window", brokenSections),
            Hotkeys = ReadSection<HotkeySettings>(root, "hotkeys", brokenSections),
            Teams = ReadSection<TeamSettings>(root, "teams", brokenSections),
            HatTrick = ReadSection<HatTrickSettings>(root, "hatTrick", brokenSections),
            Notifications = ReadSection<NotificationSettings>(root, "notifications", brokenSections),
            Gaming = ReadSection<GamingSettings>(root, "gaming", brokenSections),
            CountryWatch = ReadSection<CountryWatchSettings>(root, "countryWatch", brokenSections),
        };
        return settings.Normalize();
    }

    private static T ReadSection<T>(JsonObject root, string name, List<string> broken) where T : new()
    {
        var node = root.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (node is null) return new T();
        try
        {
            return node.Deserialize<T>(JsonOptions) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            broken.Add(name);
            return new T();
        }
    }

    /// <summary>Applies a change to the current settings, persists it and notifies listeners.</summary>
    public void Update(Action<AppSettings> change)
    {
        AppSettings snapshot;
        lock (_gate)
        {
            var copy = Clone(Current);
            change(copy);
            snapshot = copy.Normalize();
            Current = snapshot;
            TrySave(snapshot);
        }

        Changed?.Invoke(this, snapshot);
    }

    public void ResetToDefaults()
    {
        var defaults = new AppSettings().Normalize();
        lock (_gate)
        {
            Current = defaults;
            TrySave(defaults);
        }

        Changed?.Invoke(this, defaults);
    }

    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new AppSettings();

    private void TrySave(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Saving settings to {SettingsPath} failed.", ex);
        }
    }

    private void BackUpCorruptFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(SettingsPath, Path.Combine(SettingsDirectory, $"settings.corrupt-{stamp}.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not back up the corrupt settings file.", ex);
        }
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
