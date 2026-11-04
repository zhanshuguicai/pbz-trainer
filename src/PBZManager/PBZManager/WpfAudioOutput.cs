using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using PBZ.Core.Music;

namespace PBZManager;

// Single player for every rule (WPF MediaPlayer decodes mp3 through Windows Media Foundation).
// Starts with a short fade-in; FadeOut ramps the volume down and then stops. Looping tracks restart when they end.
public sealed class WpfAudioOutput : IAudioOutput
{
    private const int StepMs = 50;

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(StepMs) };
    private readonly Func<MusicSettings> _settings;
    private double _fadeFrom;
    private double _fadeTo;
    private double _fadeElapsed;
    private double _fadeTotal;
    private bool _stopAfterFade;
    private bool _loop;
    private double _startSeconds;
    private double _volumeFactor = 1;
    private double? _fadeOutSeconds;

    public WpfAudioOutput(Func<MusicSettings> settings)
    {
        _settings = settings;
        _fade.Tick += (_, _) => FadeStep();
        _player.MediaEnded += (_, _) =>
        {
            if (_loop && CurrentKey is not null)
            {
                // Each loop restarts from the rule's start offset, so a skipped intro stays skipped.
                _player.Position = TimeSpan.FromSeconds(_startSeconds);
                _player.Play();
            }
            else Stop();
        };
        // The position can only be set once the file is open.
        _player.MediaOpened += (_, _) => { if (_startSeconds > 0) _player.Position = TimeSpan.FromSeconds(_startSeconds); };
        _player.MediaFailed += (_, e) => { Stop(); Error?.Invoke($"播放失败：{e.ErrorException.Message}"); };
    }

    public string? CurrentKey { get; private set; }
    public string? CurrentPath { get; private set; }

    public event Action<string>? Error;
    public event Action? Changed;

    public void Play(string key, PlayRequest request)
    {
        var path = request.Path;
        if (!File.Exists(path))
        {
            Error?.Invoke($"找不到音频文件：{path}");
            return;
        }
        _fade.Stop();
        _player.Stop();
        _player.Open(new Uri(path));
        _player.Volume = 0;
        _player.Play();
        CurrentKey = key;
        CurrentPath = path;
        _loop = request.Loop;
        _startSeconds = request.StartSeconds;
        _volumeFactor = request.VolumeFactor;
        _fadeOutSeconds = request.FadeOutSeconds;
        StartFade(0, TargetVolume, Math.Max(request.FadeInSeconds * 1000, 1), stopAfter: false);
        Changed?.Invoke();
    }

    public void FadeOut()
    {
        if (CurrentKey is null) return;
        CurrentKey = null;
        CurrentPath = null;
        StartFade(_player.Volume, 0, _fadeOutSeconds is { } s ? s * 1000 : _settings().FadeOutMs, stopAfter: true);
        Changed?.Invoke();
    }

    public void Stop()
    {
        _fade.Stop();
        _player.Stop();
        _player.Close();
        CurrentKey = null;
        CurrentPath = null;
        Changed?.Invoke();
    }

    // Applies a volume change to the track that is playing, unless a fade is running.
    public void ApplyVolume()
    {
        if (CurrentKey is not null && !_fade.IsEnabled) _player.Volume = TargetVolume;
    }

    // Global volume times the rule's own volume (a rule may go up to 200 %), capped at full volume.
    private double TargetVolume => Math.Clamp(_settings().Volume * _volumeFactor, 0, 1);

    private void StartFade(double from, double to, double ms, bool stopAfter)
    {
        _fadeFrom = from;
        _fadeTo = to;
        _fadeElapsed = 0;
        _fadeTotal = Math.Max(ms, 1);
        _stopAfterFade = stopAfter;
        _player.Volume = from;
        _fade.Start();
    }

    private void FadeStep()
    {
        _fadeElapsed += StepMs;
        var t = Math.Min(1, _fadeElapsed / _fadeTotal);
        _player.Volume = _fadeFrom + (_fadeTo - _fadeFrom) * t;
        if (t < 1) return;
        _fade.Stop();
        if (_stopAfterFade)
        {
            _player.Stop();
            _player.Close();
        }
    }
}
