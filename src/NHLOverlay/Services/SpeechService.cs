using System.Speech.Synthesis;
using NHLOverlay.Helpers;

namespace NHLOverlay.Services;

public interface ISpeechOutput
{
    void Configure(string voice, int rate);

    void Speak(string text);
}

/// <summary>
/// Spoken alerts with the Windows text-to-speech voices. Announcements queue up; if they pile up (e.g. a
/// burst after reconnecting) the backlog is dropped so the voice never lags far behind the game.
/// </summary>
public sealed class SpeechService : ISpeechOutput, IDisposable
{
    private const int MaxPending = 3;

    private readonly SpeechSynthesizer? _synth;
    private int _pending;

    public SpeechService()
    {
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _synth.SpeakCompleted += (_, _) => Interlocked.Decrement(ref _pending);
        }
        catch (Exception ex)
        {
            Log.Warn("Text-to-speech is not available.", ex);
            _synth = null;
        }
    }

    public bool IsAvailable => _synth is not null;

    /// <summary>Names of the installed, enabled voices.</summary>
    public IReadOnlyList<string> Voices =>
        _synth?.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList() ?? [];

    public void Configure(string voice, int rate)
    {
        if (_synth is null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(voice) && Voices.Contains(voice)) _synth.SelectVoice(voice);
            _synth.Rate = Math.Clamp(rate, -10, 10);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Log.Warn($"Could not select voice '{voice}'.", ex);
        }
    }

    public void Speak(string text)
    {
        if (_synth is null || string.IsNullOrWhiteSpace(text)) return;
        if (Volatile.Read(ref _pending) >= MaxPending)
        {
            _synth.SpeakAsyncCancelAll();
            Volatile.Write(ref _pending, 0);
        }

        Interlocked.Increment(ref _pending);
        _synth.SpeakAsync(text);
    }

    public void Dispose()
    {
        _synth?.SpeakAsyncCancelAll();
        _synth?.Dispose();
    }
}
