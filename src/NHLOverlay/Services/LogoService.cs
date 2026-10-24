using System.Windows.Media;
using NHLOverlay.Helpers;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace NHLOverlay.Services;

/// <summary>
/// Team logos. The NHL publishes logos only as SVG, which WPF cannot display natively, so each logo is
/// downloaded once, cached on disk and converted to a vector <see cref="DrawingImage"/> with SharpVectors
/// (stays sharp at any UI scale). The "_dark" variant is preferred because it is designed for dark
/// backgrounds; the URL the API supplied is the fallback. Failures yield null and the card shows the
/// team abbreviation only.
/// </summary>
public sealed class LogoService : IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly Func<string, ImageSource?> _convert;
    private readonly Dictionary<string, Task<ImageSource?>> _images = new(StringComparer.OrdinalIgnoreCase);

    public LogoService(string cacheDirectory, HttpClient? httpClient = null, Func<string, ImageSource?>? convert = null)
    {
        _cacheDirectory = cacheDirectory;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _convert = convert ?? ConvertSvg;
    }

    /// <summary>Must be called on the UI thread; concurrent requests for the same logo share one task.</summary>
    public Task<ImageSource?> GetLogoAsync(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return Task.FromResult<ImageSource?>(null);
        if (!_images.TryGetValue(logoUrl, out var task))
        {
            task = LoadAsync(logoUrl);
            _images[logoUrl] = task;
        }

        return task;
    }

    private async Task<ImageSource?> LoadAsync(string logoUrl)
    {
        foreach (var url in CandidateUrls(logoUrl))
        {
            var path = await GetCachedFileAsync(url).ConfigureAwait(true);
            if (path is null) continue;
            var image = _convert(path); // back on the UI thread (ConfigureAwait(true))
            if (image is not null) return image;
        }

        Log.Warn($"No usable logo for {logoUrl}.");
        return null;
    }

    /// <summary>".../LAK_light.svg" -> ".../LAK_dark.svg" first, then the original.</summary>
    internal static IEnumerable<string> CandidateUrls(string logoUrl)
    {
        if (logoUrl.EndsWith("_light.svg", StringComparison.OrdinalIgnoreCase))
            yield return logoUrl[..^"_light.svg".Length] + "_dark.svg";
        yield return logoUrl;
    }

    internal async Task<string?> GetCachedFileAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var fileName = string.Concat(Path.GetFileName(uri.AbsolutePath).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (fileName.Length == 0) return null;
        var path = Path.Combine(_cacheDirectory, fileName);

        var info = new FileInfo(path);
        if (info.Exists && info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc < CacheLifetime)
            return path;

        try
        {
            var bytes = await _http.GetByteArrayAsync(uri).ConfigureAwait(false);
            Directory.CreateDirectory(_cacheDirectory);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Downloading logo {url} failed: {ex.Message}");
            // An expired cached copy is still better than nothing.
            return info.Exists && info.Length > 0 ? path : null;
        }
    }

    private static ImageSource? ConvertSvg(string path)
    {
        try
        {
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
            using var reader = new FileSvgReader(settings);
            var drawing = reader.Read(path);
            if (drawing is null) return null;
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not render logo {path}.", ex);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
