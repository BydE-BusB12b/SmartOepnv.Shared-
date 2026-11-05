using System.Reflection;
using NAudio.Wave;
using SIPSorceryMedia.Windows;

namespace SmartOepnv.AppShared.Voip;

/// <summary>
/// Steuert Wiedergabe-Lautstärke und leert den NAudio-Puffer (verhindert verzögertes Nachspielen).
/// </summary>
internal static class VoipLeitstelleAudioHelper
{
    private const float PlaybackVolumeFraction = 1.0f;

    private static readonly FieldInfo? WaveOutField = typeof(WindowsAudioEndPoint).GetField(
        "_waveOutEvent",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? WaveProviderField = typeof(WindowsAudioEndPoint).GetField(
        "_waveProvider",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void ApplyPlaybackVolume(WindowsAudioEndPoint endPoint)
    {
        try
        {
            if (WaveOutField?.GetValue(endPoint) is WaveOutEvent waveOut)
            {
                waveOut.Volume = PlaybackVolumeFraction;
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Verwirft gepufferte Tablet-Audiodaten (kein 6-Sekunden-Nachlauf).</summary>
    public static void FlushPlaybackBuffer(WindowsAudioEndPoint endPoint)
    {
        try
        {
            if (WaveProviderField?.GetValue(endPoint) is BufferedWaveProvider provider)
            {
                provider.ClearBuffer();
            }
        }
        catch
        {
            // ignore
        }
    }
}
