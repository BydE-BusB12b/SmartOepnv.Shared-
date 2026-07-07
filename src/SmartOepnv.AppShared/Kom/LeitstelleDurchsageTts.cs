using System.IO;
using System.Speech.Synthesis;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Windows-Sprachausgabe für Leitstellen-Durchsagen (M4A/AAC wie Mikrofon-Aufnahme).</summary>
public static class LeitstelleDurchsageTts
{
    public const int MaxTextLength = 600;

    public sealed record VoiceOption(string Name, string DisplayName);

    public static IReadOnlyList<VoiceOption> ListVoices()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new VoiceOption(
                v.VoiceInfo.Name,
                $"{v.VoiceInfo.Description} ({v.VoiceInfo.Culture.Name})"))
            .OrderByDescending(v => v.DisplayName.StartsWith("de", StringComparison.OrdinalIgnoreCase) ||
                                    v.DisplayName.Contains("(de", StringComparison.OrdinalIgnoreCase))
            .ThenBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static byte[]? SynthesizeToM4aBytes(string text, string? voiceName = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTextLength)
        {
            return null;
        }

        var dir = Path.Combine(Path.GetTempPath(), "SmartOepnv", "leitstelle_durchsage");
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, $"tts_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        try
        {
            using (var synth = new SpeechSynthesizer())
            {
                if (!string.IsNullOrWhiteSpace(voiceName))
                {
                    synth.SelectVoice(voiceName);
                }

                synth.SetOutputToWaveFile(wavPath);
                synth.Speak(trimmed);
                synth.SetOutputToNull();
            }

            return LeitstelleDurchsageAudioEncoder.WavFileToM4aBytes(wavPath);
        }
        finally
        {
            TryDelete(wavPath);
        }
    }

    public static void Preview(string text, string? voiceName = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        using var synth = new SpeechSynthesizer();
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            synth.SelectVoice(voiceName);
        }

        synth.Speak(trimmed);
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
