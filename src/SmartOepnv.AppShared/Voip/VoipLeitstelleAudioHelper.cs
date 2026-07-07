using System.Reflection;
using NAudio.Wave;
using SIPSorceryMedia.Windows;

namespace SmartOepnv.AppShared.Voip;

/// <summary>
/// WindowsAudioEndPoint hat keine Echo-Unterdrückung – Wiedergabe-Lautstärke dämpfen reduziert Mikrofon-Rückkopplung.
/// </summary>
internal static class VoipLeitstelleAudioHelper
{
    private const float PlaybackVolumeFraction = 0.4f;

    private static readonly FieldInfo? WaveOutField = typeof(WindowsAudioEndPoint).GetField(
        "_waveOutEvent",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void ApplyEchoMitigation(WindowsAudioEndPoint endPoint)
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
            // Kein Abbruch bei fehlendem Zugriff auf NAudio-internes Feld.
        }
    }
}
