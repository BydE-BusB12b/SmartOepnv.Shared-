using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared;

/// <summary>
/// Stimme 2 bleibt eine WAV unter dem Namen <c>*_2.wav</c>.
/// MP3, OGG und M4A werden dafür umgewandelt, eine echte WAV nur kopiert.
/// </summary>
internal static class AnnouncementWavImport
{
    public static bool Store(string sourcePath, string wavTargetPath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return StoreWithMediaFoundation(sourcePath, wavTargetPath);
        }

        return EmbeddedSoundConcatenator.StorePcmWav(sourcePath, wavTargetPath);
    }

    private static bool StoreWithMediaFoundation(string sourcePath, string wavTargetPath)
    {
        MediaFoundationApi.Startup();
        var directory = Path.GetDirectoryName(wavTargetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = wavTargetPath + ".tmp.wav";
        try
        {
            using (var reader = new MediaFoundationReader(sourcePath))
            {
                WaveFileWriter.CreateWaveFile16(temp, reader.ToSampleProvider());
            }

            File.Move(temp, wavTargetPath, overwrite: true);
            return true;
        }
        catch
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }

            throw;
        }
    }
}
