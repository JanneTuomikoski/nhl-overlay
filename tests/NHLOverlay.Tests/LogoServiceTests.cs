using System.Net;
using System.Windows.Media;
using NHLOverlay.Services;

namespace NHLOverlay.Tests;

public sealed class LogoServiceTests : IDisposable
{
    private const string LightUrl = "https://assets.nhle.com/logos/nhl/svg/LAK_light.svg";
    private const string DarkUrl = "https://assets.nhle.com/logos/nhl/svg/LAK_dark.svg";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _converted = [];

    private LogoService Create(StubHandler handler) => new(_directory, new HttpClient(handler), path =>
    {
        _converted.Add(Path.GetFileName(path));
        return new DrawingImage();
    });

    private static HttpResponseMessage Svg() => new(HttpStatusCode.OK) { Content = new StringContent("<svg xmlns=\"http://www.w3.org/2000/svg\"/>") };

    [Fact]
    public void Prefers_the_dark_variant() =>
        Assert.Equal([DarkUrl, LightUrl], LogoService.CandidateUrls(LightUrl));

    [Fact]
    public async Task Downloads_once_then_uses_the_disk_cache()
    {
        var handler = new StubHandler(_ => Svg());
        using (var logos = Create(handler))
            Assert.NotNull(await logos.GetLogoAsync(LightUrl));
        using (var logos = Create(handler))
            Assert.NotNull(await logos.GetLogoAsync(LightUrl));

        Assert.Single(handler.Requests);
        Assert.Equal(DarkUrl, handler.Requests[0].ToString());
        Assert.True(File.Exists(Path.Combine(_directory, "LAK_dark.svg")));
    }

    [Fact]
    public async Task Falls_back_to_the_supplied_url_when_the_dark_variant_is_missing()
    {
        var handler = new StubHandler(r => r.RequestUri!.ToString() == DarkUrl ? new HttpResponseMessage(HttpStatusCode.NotFound) : Svg());
        using var logos = Create(handler);

        Assert.NotNull(await logos.GetLogoAsync(LightUrl));
        Assert.Equal(["LAK_light.svg"], _converted);
    }

    [Fact]
    public async Task Returns_null_when_no_logo_is_available()
    {
        using var logos = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await logos.GetLogoAsync(LightUrl));
        Assert.Null(await logos.GetLogoAsync(null));
        Assert.Null(await logos.GetLogoAsync("http://insecure.example/logo.svg"));
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
