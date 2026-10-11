using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Mikrofon-Aufnahme für Leitstellen-Durchsage (M4A/AAC wie GPSAnsagen).</summary>
public sealed class LeitstelleDurchsageRecorder : IDisposable
{
    public const int MaxRecordingSeconds = 180;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _wavPath;
    private DateTimeOffset _startedAt;
    private bool _mediaFoundationStarted;
    private TaskCompletionSource? _stopTcs;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        Stop();
        MediaFoundationApi.Startup();
        _mediaFoundationStarted = true;

        var dir = Path.Combine(Path.GetTempPath(), "SmartOepnv", "leitstelle_durchsage");
        Directory.CreateDirectory(dir);
        _wavPath = Path.Combine(dir, $"record_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(44_100, 1)
        };
        _writer = new WaveFileWriter(_wavPath, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();
        _startedAt = DateTimeOffset.UtcNow;
        IsRecording = true;
    }

    public Task StopAsync()
    {
        if (!IsRecording || _waveIn is null)
        {
            return Task.CompletedTask;
        }

        _stopTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.StopRecording();
        return _stopTcs.Task;
    }

    public void Stop()
    {
        _ = StopAsync();
    }

    public TimeSpan Elapsed => IsRecording ? DateTimeOffset.UtcNow - _startedAt : TimeSpan.Zero;

    public byte[]? FinishToM4aBytes()
    {
        var wavPath = _wavPath;
        CleanupWave();
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
        {
            return null;
        }

        var m4aPath = Path.ChangeExtension(wavPath, ".m4a");
        try
        {
            return LeitstelleDurchsageAudioEncoder.WavFileToM4aBytes(wavPath);
        }
        finally
        {
            TryDelete(wavPath);
            TryDelete(m4aPath);
            _wavPath = null;
        }
    }

    public void Discard()
    {
        var wavPath = _wavPath;
        CleanupWave();
        TryDelete(wavPath);
        _wavPath = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        if (Elapsed.TotalSeconds >= MaxRecordingSeconds)
        {
            Stop();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsRecording = false;
        _writer?.Dispose();
        _writer = null;
        _waveIn?.Dispose();
        _waveIn = null;
        _stopTcs?.TrySetResult();
        _stopTcs = null;
    }

    private void CleanupWave()
    {
        if (_waveIn is not null)
        {
            try
            {
                _waveIn.StopRecording();
            }
            catch
            {
                // ignore
            }
        }

        _writer?.Dispose();
        _writer = null;
        _waveIn?.Dispose();
        _waveIn = null;
        IsRecording = false;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        Discard();
        if (_mediaFoundationStarted)
        {
            try
            {
                MediaFoundationApi.Shutdown();
            }
            catch
            {
                // ignore
            }

            _mediaFoundationStarted = false;
        }
    }
}
