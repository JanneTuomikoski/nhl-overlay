using System.Windows.Threading;
using NHLOverlay.Models;
using NHLOverlay.Services;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Tests;

public sealed class NotificationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NHLOverlayTests", Guid.NewGuid().ToString("N"));
    private readonly SettingsService _settings;
    private readonly RuntimeState _runtime = new();
    private readonly ToastService _toasts = new(Dispatcher.CurrentDispatcher);
    private readonly FakeSound _sound = new();
    private readonly FakeSpeech _speech = new();
    private readonly List<string> _desktop = [];
    private DateTimeOffset _now = new(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(3));
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _settings = new SettingsService(_directory, isPortable: false);
        _settings.Update(s =>
        {
            s.Teams.Favorites = ["EDM"];
            foreach (var rule in s.Notifications.Rules.Values)
            {
                rule.Desktop = true;
                rule.Sound = true;
                rule.Speech = true;
            }
        });
        _service = new NotificationService(new GameStateService(), _settings, _runtime, _toasts, _sound, _speech,
            (title, _) => _desktop.Add(title), Dispatcher.CurrentDispatcher, () => _now);
    }

    private static GameEvent Event(GameEventKind kind, string away = "EDM", string home = "TOR", string key = "k") =>
        new(kind, key, new HockeyGame { Id = 1, Away = new HockeyTeam { Abbrev = away }, Home = new HockeyTeam { Abbrev = home } },
            $"{kind} title", "body", $"{kind} speech");

    [Fact]
    public void Delivers_to_every_enabled_channel()
    {
        _service.Deliver([Event(GameEventKind.Goal)]);

        Assert.Equal("Goal title", Assert.Single(_toasts.Toasts).Title);
        Assert.Equal(["Goal title"], _desktop);
        Assert.Equal([AlertSound.Horn], _sound.Played);
        Assert.Equal(["Goal speech"], _speech.Spoken);
    }

    [Fact]
    public void Favorites_only_rules_skip_other_games()
    {
        _service.Deliver([Event(GameEventKind.Goal, "BOS", "MTL")]);
        Assert.Empty(_toasts.Toasts);

        // Hat tricks are league-wide by default.
        _service.Deliver([Event(GameEventKind.HatTrickCompleted, "BOS", "MTL")]);
        Assert.Single(_toasts.Toasts);
        Assert.Equal([AlertSound.Fanfare], _sound.Played);
    }

    [Fact]
    public void Disabled_rule_or_master_switch_delivers_nothing()
    {
        _settings.Update(s => s.Notifications.Rules[GameEventKind.Goal].Enabled = false);
        _service.Deliver([Event(GameEventKind.Goal)]);
        Assert.Empty(_toasts.Toasts);

        _settings.Update(s =>
        {
            s.Notifications.Rules[GameEventKind.Goal].Enabled = true;
            s.Notifications.Enabled = false;
        });
        _service.Deliver([Event(GameEventKind.Goal)]);
        Assert.Empty(_toasts.Toasts);
    }

    [Fact]
    public void Gaming_profile_mutes_the_channels_it_is_told_to()
    {
        _settings.Update(s =>
        {
            s.Gaming.DesktopNotifications = false;
            s.Gaming.Sounds = false;
            s.Gaming.Speech = true;
        });
        _runtime.SetGaming(true, "Wow");

        _service.Deliver([Event(GameEventKind.Goal)]);

        Assert.Single(_toasts.Toasts);
        Assert.Empty(_desktop);
        Assert.Empty(_sound.Played);
        Assert.Single(_speech.Spoken);
    }

    [Fact]
    public void Quiet_hours_mute_sound_and_voice_but_keep_toasts()
    {
        _settings.Update(s =>
        {
            s.Notifications.QuietHoursEnabled = true;
            s.Notifications.QuietFrom = new TimeOnly(23, 0);
            s.Notifications.QuietTo = new TimeOnly(7, 0);
        });

        _service.Deliver([Event(GameEventKind.Goal)]); // 02:00 local

        Assert.Single(_toasts.Toasts);
        Assert.Empty(_sound.Played);
        Assert.Empty(_speech.Spoken);
    }

    [Theory]
    [InlineData("23:00", "07:00", "02:00", true)]
    [InlineData("23:00", "07:00", "12:00", false)]
    [InlineData("09:00", "17:00", "12:00", true)]
    [InlineData("09:00", "17:00", "17:00", false)]
    public void Quiet_hours_handle_ranges_over_midnight(string from, string to, string now, bool quiet)
    {
        var n = new NotificationSettings { QuietHoursEnabled = true, QuietFrom = TimeOnly.Parse(from), QuietTo = TimeOnly.Parse(to) };
        Assert.Equal(quiet, n.IsQuietTime(TimeOnly.Parse(now)));
    }

    [Fact]
    public void Do_not_disturb_holds_alerts_and_summarizes_them_afterwards()
    {
        _runtime.DoNotDisturb = true;
        _service.Deliver([Event(GameEventKind.Goal, key: "a"), Event(GameEventKind.GameFinal, key: "b")]);
        Assert.Empty(_toasts.Toasts);
        Assert.Empty(_sound.Played);

        _runtime.DoNotDisturb = false;

        var summary = Assert.Single(_toasts.Toasts);
        Assert.Equal("WHILE DO NOT DISTURB: 2 UPDATES", summary.Title);
        Assert.Contains("Goal title", summary.Body);
        Assert.Equal(["While you were away: 2 updates."], _speech.Spoken);
    }

    [Fact]
    public void Worth_watching_has_a_cooldown()
    {
        _service.Deliver([Event(GameEventKind.WorthWatching, "BOS", "MTL", "a")]);
        _now = _now.AddMinutes(5);
        _service.Deliver([Event(GameEventKind.WorthWatching, "NYR", "OTT", "b")]);
        Assert.Single(_toasts.Toasts);

        _now = _now.AddMinutes(6);
        _service.Deliver([Event(GameEventKind.WorthWatching, "NYR", "OTT", "c")]);
        Assert.Equal(2, _toasts.Toasts.Count);
    }

    [Fact]
    public void Bursts_are_capped_with_a_summary()
    {
        _service.Deliver(Enumerable.Range(0, 6).Select(i => Event(GameEventKind.Goal, key: $"g{i}")).ToList());

        Assert.Equal(3, _sound.Played.Count);
        Assert.Equal(ToastService.MaxVisible, _toasts.Toasts.Count);
        Assert.Equal("+3 MORE UPDATES", _toasts.Toasts[0].Title);
    }

    [Theory]
    [InlineData(AlertSound.Horn)]
    [InlineData(AlertSound.Chime)]
    [InlineData(AlertSound.Fanfare)]
    public void Synthesized_sounds_are_valid_pcm_wav(AlertSound sound)
    {
        var wav = SoundService.Synthesize(sound, 0.6);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.InRange(wav.Length, 22050, 22050 * 2 * 3); // between 0.5 and 3 seconds of 16-bit mono
        Assert.Contains(wav.Skip(44), b => b != 0);
    }

    [Fact]
    public void Suppression_hook_drops_events()
    {
        _service.IsSuppressed = e => e.Game.Id == 1;
        _service.Deliver([Event(GameEventKind.Goal)]);
        Assert.Empty(_toasts.Toasts);
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

    private sealed class FakeSound : ISoundOutput
    {
        public List<AlertSound> Played { get; } = [];

        public void Play(AlertSound sound, double volume) => Played.Add(sound);
    }

    private sealed class FakeSpeech : ISpeechOutput
    {
        public List<string> Spoken { get; } = [];

        public void Configure(string voice, int rate)
        {
        }

        public void Speak(string text) => Spoken.Add(text);
    }
}
