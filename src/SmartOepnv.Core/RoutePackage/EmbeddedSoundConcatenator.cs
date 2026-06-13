using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SmartOepnv.Core.RoutePackage;

public enum EmbeddedSoundSequencePartKind
{
    Audio,
    Pause
}

public sealed class EmbeddedSoundSequencePart
{
    public EmbeddedSoundSequencePartKind Kind { get; init; }

    public string? AudioPath { get; init; }

    public TimeSpan Pause { get; init; }
}

/// <summary>
/// Fügt mehrere Audiodateien (MP3/WAV/OGG) zu einer WAV-Datei zusammen – optional mit Pause dazwischen.
/// </summary>
public static class EmbeddedSoundConcatenator
{
    public static void ConcatenateSequenceToWav(
        IReadOnlyList<EmbeddedSoundSequencePart> parts,
        string outputWavPath)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Sequenz-Eintrag erforderlich.", nameof(parts));
        }

        var readers = new List<IDisposable>();
        try
        {
            var providers = new List<ISampleProvider>();

            foreach (var part in parts)
            {
                switch (part.Kind)
                {
                    case EmbeddedSoundSequencePartKind.Audio:
                        if (string.IsNullOrWhiteSpace(part.AudioPath) || !File.Exists(part.AudioPath))
                        {
                            throw new FileNotFoundException("Audiodatei nicht gefunden.", part.AudioPath);
                        }

                        var reader = OpenReader(part.AudioPath);
                        readers.Add(reader);
                        providers.Add(NormalizeProvider(reader.ToSampleProvider()));
                        break;
                    case EmbeddedSoundSequencePartKind.Pause:
                        if (part.Pause > TimeSpan.Zero)
                        {
                            providers.Add(new TimedSilenceProvider(
                                WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels),
                                part.Pause));
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(parts));
                }
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException("Die Sequenz enthält keine Audiodaten.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
            WaveFileWriter.CreateWaveFile16(outputWavPath, new ConcatenatingSampleProvider(providers));

            var info = new FileInfo(outputWavPath);
            if (info.Length == 0)
            {
                throw new InvalidOperationException("Die zusammengefügte Ansage ist leer.");
            }

            if (info.Length > EmbeddedSoundsEditor.MaxEmbeddedBytes)
            {
                File.Delete(outputWavPath);
                throw new InvalidOperationException(
                    $"Zusammengefügte Ansage zu groß (max. {EmbeddedSoundsEditor.MaxEmbeddedBytes / (1024 * 1024)} MB).");
            }
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private static ISampleProvider NormalizeProvider(ISampleProvider sample)
    {
        if (sample.WaveFormat.Channels > TargetChannels)
        {
            sample = sample.ToMono();
        }

        if (sample.WaveFormat.SampleRate != TargetSampleRate)
        {
            sample = new WdlResamplingSampleProvider(sample, TargetSampleRate);
        }

        return sample;
    }

    private const int TargetSampleRate = 44100;
    private const int TargetChannels = 1;

    public static void ConcatenateToWav(
        IReadOnlyList<string> sourcePaths,
        string outputWavPath,
        TimeSpan pauseBetweenClips)
    {
        if (sourcePaths.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Audiodatei erforderlich.", nameof(sourcePaths));
        }

        foreach (var path in sourcePaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Audiodatei nicht gefunden.", path);
            }
        }

        var readers = new List<IDisposable>();
        try
        {
            var providers = new List<ISampleProvider>();

            for (var i = 0; i < sourcePaths.Count; i++)
            {
                var reader = OpenReader(sourcePaths[i]);
                readers.Add(reader);

                providers.Add(NormalizeProvider(reader.ToSampleProvider()));

                if (i < sourcePaths.Count - 1 && pauseBetweenClips > TimeSpan.Zero)
                {
                    providers.Add(new TimedSilenceProvider(
                        WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels),
                        pauseBetweenClips));
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
            WaveFileWriter.CreateWaveFile16(outputWavPath, new ConcatenatingSampleProvider(providers));

            var info = new FileInfo(outputWavPath);
            if (info.Length == 0)
            {
                throw new InvalidOperationException("Die zusammengefügte Ansage ist leer.");
            }

            if (info.Length > EmbeddedSoundsEditor.MaxEmbeddedBytes)
            {
                File.Delete(outputWavPath);
                throw new InvalidOperationException(
                    $"Zusammengefügte Ansage zu groß (max. {EmbeddedSoundsEditor.MaxEmbeddedBytes / (1024 * 1024)} MB).");
            }
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private static WaveStream OpenReader(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(path),
            ".mp3" => new Mp3FileReader(path),
            ".ogg" => new VorbisWaveReader(path),
            _ => throw new NotSupportedException(
                $"Audioformat „{ext}“ wird nicht unterstützt (MP3, WAV, OGG).")
        };
    }

    private sealed class TimedSilenceProvider : ISampleProvider
    {
        private readonly WaveFormat _format;
        private long _remainingSamples;

        public TimedSilenceProvider(WaveFormat format, TimeSpan duration)
        {
            _format = format;
            _remainingSamples = (long)(duration.TotalSeconds * format.SampleRate * format.Channels);
        }

        public WaveFormat WaveFormat => _format;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_remainingSamples <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, _remainingSamples);
            Array.Clear(buffer, offset, toRead);
            _remainingSamples -= toRead;
            return toRead;
        }
    }
}
