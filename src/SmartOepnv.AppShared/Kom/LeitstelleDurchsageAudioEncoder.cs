using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace SmartOepnv.AppShared.Kom;

internal static class LeitstelleDurchsageAudioEncoder
{
    public static byte[]? WavFileToM4aBytes(string wavPath)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
        {
            return null;
        }

        MediaFoundationApi.Startup();
        var m4aPath = Path.ChangeExtension(wavPath, ".m4a");
        try
        {
            using var reader = new AudioFileReader(wavPath);
            MediaFoundationEncoder.EncodeToAac(reader, m4aPath, 128_000);
            var bytes = File.ReadAllBytes(m4aPath);
            return bytes.Length > 0 ? bytes : null;
        }
        finally
        {
            TryDelete(m4aPath);
            MediaFoundationApi.Shutdown();
        }
    }

    private static void TryDelete(string path)
    {
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
}
