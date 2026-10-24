using System.Globalization;

namespace NHLOverlay.Helpers;

/// <summary>
/// Minimal thread-safe file logger. Keeps the active log under <see cref="MaxBytes"/> by rolling
/// it over to a single ".old" file, so disk usage stays bounded.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "nhloverlay.log");
        }
        catch
        {
            _path = null; // Logging is best effort; never take the app down over it.
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (_path is null) return;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{(ex is null ? "" : Environment.NewLine + ex)}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, line);
            }
            catch
            {
                // Ignore: a locked or read-only log file must not affect the overlay.
            }
        }
    }
}
