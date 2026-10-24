using System.Media;
using NHLOverlay.Helpers;

namespace NHLOverlay.Services;

public enum AlertSound
{
    /// <summary>Short arena-style horn (goals).</summary>
    Horn,

    /// <summary>Two-tone chime (general alerts).</summary>
    Chime,

    /// <summary>Rising four-note fanfare (hat tricks).</summary>
    Fanfare,
}

public interface ISoundOutput
{
    void Play(AlertSound sound, double volume);
}

/// <summary>
/// Notification sounds, synthesized in memory (no audio assets to ship). Plays asynchronously through the
/// default output device; a new sound replaces one that is still playing.
/// </summary>
public sealed class SoundService : ISoundOutput, IDisposable
{
    private const int SampleRate = 22050;

    private readonly Dictionary<(AlertSound, int), byte[]> _cache = [];
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public void Play(AlertSound sound, double volume)
    {
        try
        {
            var key = (sound, (int)Math.Round(Math.Clamp(volume, 0, 1) * 20));
            if (!_cache.TryGetValue(key, out var wav))
            {
                wav = Synthesize(sound, key.Item2 / 20.0);
                _cache[key] = wav;
            }

            _player?.Stop();
            _player?.Dispose();
            _stream?.Dispose();
            _stream = new MemoryStream(wav);
            _player = new SoundPlayer(_stream);
            _player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            Log.Warn("Could not play a notification sound.", ex);
        }
    }

    internal static byte[] Synthesize(AlertSound sound, double volume)
    {
        var samples = sound switch
        {
            AlertSound.Horn => Horn(),
            AlertSound.Fanfare => Notes([(523.25, 0.13), (659.25, 0.13), (783.99, 0.13), (1046.5, 0.55)]),
            _ => Notes([(987.77, 0.22), (1318.51, 0.5)]),
        };
        return ToWav(samples, volume * 0.85);
    }

    /// <summary>Detuned brass-like tone with soft clipping, 1.6 s.</summary>
    private static float[] Horn()
    {
        const double duration = 1.6;
        var data = new float[(int)(SampleRate * duration)];
        for (var i = 0; i < data.Length; i++)
        {
            var t = (double)i / SampleRate;
            var envelope = Math.Min(1, t / 0.06) * Math.Min(1, (duration - t) / 0.35);
            double x = 0;
            foreach (var (f, a) in new[] { (155.0, 1.0), (310.0, 0.6), (465.0, 0.45), (620.0, 0.3), (775.0, 0.2) })
                x += a * (Math.Sin(2 * Math.PI * f * t) + Math.Sin(2 * Math.PI * f * 1.006 * t)) / 2;
            data[i] = (float)(Math.Tanh(1.6 * x / 2.55) * envelope);
        }

        return data;
    }

    /// <summary>Sequence of bell-like notes with exponential decay.</summary>
    private static float[] Notes(IReadOnlyList<(double Frequency, double Seconds)> notes)
    {
        var data = new List<float>();
        foreach (var (frequency, seconds) in notes)
        {
            var count = (int)(SampleRate * seconds);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                var envelope = Math.Min(1, t / 0.005) * Math.Exp(-4.5 * t / seconds);
                var x = Math.Sin(2 * Math.PI * frequency * t) + 0.3 * Math.Sin(2 * Math.PI * frequency * 2 * t);
                data.Add((float)(x / 1.3 * envelope));
            }
        }

        return data.ToArray();
    }

    /// <summary>16-bit mono PCM WAV.</summary>
    private static byte[] ToWav(float[] samples, double volume)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataBytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var s in samples)
            writer.Write((short)Math.Clamp(s * volume * short.MaxValue, short.MinValue, short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        _player?.Dispose();
        _stream?.Dispose();
    }
}
