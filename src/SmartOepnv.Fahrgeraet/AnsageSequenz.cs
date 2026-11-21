using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Haltestellenansage wie auf dem Tablet: Gong, Nächste, Name, folgende Halte.
/// Petra wird an stillen Rändern gekürzt, Gong und Stimme 2 bleiben ganz.
/// Links ist innen, rechts ist außen.
/// </summary>
public static class AnsageSequenz
{
    private const int GapMs = 150;
    private const int GongToNextMs = 1000;
    private const double SilenceRms = 350;
    private const int TrailKeepMs = 40;

    public static void PlayStop(FahrSession session, RouteStopItem stop, bool specialList = false)
    {
        if (session.Package is null)
        {
            return;
        }

        var voice2 = specialList
            ? GeraetEinstellungen.Bool("special_list_announcement_voice_2", false)
            : AnsageWahl.RouteUsesVoice2(session);
        var template = TemplateFor(session, stop);
        var steps = Build(session, stop, template);
        var zone = AnnouncementAudioOutput.Normalize(template?.AudioOutput);
        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        var played = false;
        var previousAudio = false;
        FahrAnsage.Active = true;
        try
        {
            foreach (var step in steps)
            {
                if (step.PauseMs > 0)
                {
                    Thread.Sleep(step.PauseMs);
                    previousAudio = false;
                    continue;
                }

                var file = AnsageWahl.Prefer(sounds, step.File, voice2 && !AnsageWahl.KeepPetra(step.File));
                if (file is null)
                {
                    continue;
                }

                if (previousAudio)
                {
                    Thread.Sleep(GapMs);
                }

                PlayBytes(sounds[file].Base64, file, zone, 1);
                previousAudio = true;
                played = true;
            }
        }
        finally
        {
            FahrAnsage.Active = false;
        }

        if (!played)
        {
            session.Status?.Invoke($"Keine Ansage für {stop.Name}");
        }
    }

    public static void PlayClip(FahrSession session, string? fileName, bool voice2, string zone, double volume = 1, bool display = false)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        var wanted = fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".wav";
        var key = AnsageWahl.Prefer(sounds, wanted, voice2 && !AnsageWahl.KeepPetra(wanted))
                  ?? AnsageWahl.Prefer(sounds, fileName, voice2 && !AnsageWahl.KeepPetra(fileName));
        if (key is null)
        {
            return;
        }

        PlayBytes(sounds[key].Base64, key, zone, volume, display);
    }

    private static List<Step> Build(FahrSession session, RouteStopItem stop, ManagedAnnouncementTemplateItem? template)
    {
        var steps = new List<Step>();
        var legacy = AnnouncementSequenceExport.IsLegacyMergedFileName(stop.EmbeddedSoundFileName);
        if (template is not null && AnnouncementSequenceExport.HasSequencePlayback(template) && !legacy)
        {
            AddFlag(steps, template.IncludeGong, PlanerGongSoundResolver.GongFileName);
            if (template.IncludeGong && template.IncludeSondergong)
            {
                steps.Add(Step.Pause(GapMs));
            }

            if (template.IncludeSondergong && template.SondergongFileName.Trim().Length > 0)
            {
                steps.Add(Step.Audio(template.SondergongFileName));
            }

            var chime = template.IncludeGong || template.IncludeSondergong;
            if (chime && template.IncludeNextStopGerman)
            {
                steps.Add(Step.Pause(GongToNextMs));
            }

            AddFlag(steps, template.IncludeNextStopGerman, PlanerNextStopSoundResolver.FileName);
            if (template.IncludeNextStopGerman && template.IncludeNextStopMp3)
            {
                steps.Add(Step.Pause(GapMs));
            }

            AddFlag(steps, template.IncludeNextStopMp3, FirstExisting(session, PlanerNextStopMp3SoundResolver.CandidateFileNames) ?? PlanerNextStopMp3SoundResolver.FileName);
            var prefix = chime || template.IncludeNextStopGerman || template.IncludeNextStopMp3;
            if (prefix && template.IncludeFollowingStops)
            {
                steps.Add(Step.Pause(GapMs));
            }

            AddFlag(steps, template.IncludeFollowingStops, FirstExisting(session, PlanerFollowingStopsSoundResolver.CandidateFileNames) ?? PlanerFollowingStopsSoundResolver.FileName);
            foreach (var entry in template.AnnouncementSequence)
            {
                if (entry.Kind == AnnouncementExportEntryKind.Pause && entry.PauseSeconds > 0)
                {
                    steps.Add(Step.Pause((int)(entry.PauseSeconds * 1000)));
                }
                else if (entry.Kind == AnnouncementExportEntryKind.Audio && entry.FileName.Trim().Length > 0)
                {
                    steps.Add(Step.Audio(entry.FileName));
                }
            }
        }

        if (!legacy && stop.EmbeddedSoundFileName.Trim().Length > 0 &&
            steps.All(step => !string.Equals(step.File, stop.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase)))
        {
            if (steps.Count > 0 && steps[^1].PauseMs == 0)
            {
                steps.Add(Step.Pause(GapMs));
            }

            steps.Add(Step.Audio(stop.EmbeddedSoundFileName));
        }
        else if (steps.Count == 0 && stop.EmbeddedSoundFileName.Trim().Length > 0)
        {
            steps.Add(Step.Audio(stop.EmbeddedSoundFileName));
        }

        return steps;
    }

    private static void AddFlag(List<Step> steps, bool include, string file)
    {
        if (include)
        {
            steps.Add(Step.Audio(file));
        }
    }

    private static string? FirstExisting(FahrSession session, IReadOnlyList<string> names)
    {
        if (session.Package is null)
        {
            return null;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        foreach (var name in names)
        {
            if (AnsageWahl.Prefer(sounds, name, false) is not null)
            {
                return name;
            }
        }

        return null;
    }

    private static ManagedAnnouncementTemplateItem? TemplateFor(FahrSession session, RouteStopItem stop)
    {
        var templates = session.Package?.AnnouncementTemplates;
        if (templates is null)
        {
            return null;
        }

        var file = stop.EmbeddedSoundFileName.Trim();
        var byFile = templates.FirstOrDefault(item =>
            file.Length > 0 && string.Equals(item.EmbeddedSoundFileName.Trim(), file, StringComparison.OrdinalIgnoreCase));
        if (byFile is not null)
        {
            return byFile;
        }

        return templates.FirstOrDefault(item =>
            string.Equals(item.DisplayName.Trim(), stop.Name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static void PlayBytes(string base64, string fileName, string zone, double volume, bool display = false)
    {
        var bytes = Convert.FromBase64String(base64);
        var keep = AnsageWahl.KeepPetra(fileName) ||
                   Path.GetFileNameWithoutExtension(fileName).EndsWith("_2", StringComparison.OrdinalIgnoreCase);
        if (!keep)
        {
            bytes = TrimPetra(bytes);
        }

        if (volume < 0.99)
        {
            bytes = Scale(bytes, volume);
        }

        if (display)
        {
            TonAusgang.PlayDisplay(bytes);
            return;
        }

        bool? side = AnnouncementAudioOutput.Normalize(zone) switch
        {
            AnnouncementAudioOutput.Outside => false,
            AnnouncementAudioOutput.Both => null,
            _ => true
        };
        StereoWav.Play(bytes, side);
    }

    private static byte[] TrimPetra(byte[] wav)
    {
        if (!Pcm.TryRead(wav, out var rate, out var samples))
        {
            return wav;
        }

        var window = Math.Max(1, rate / 50);
        var start = 0;
        while (start + window <= samples.Length && Pcm.Rms(samples, start, window) < SilenceRms)
        {
            start += window / 2;
        }

        var end = samples.Length;
        while (end - window >= start && Pcm.Rms(samples, end - window, window) < SilenceRms)
        {
            end -= window / 2;
        }

        if (start >= end)
        {
            return wav;
        }

        var keep = rate * TrailKeepMs / 1000;
        end = Math.Min(samples.Length, end + keep);
        start = Math.Max(0, start - keep / 2);
        return Pcm.Wrap(samples[start..end], rate);
    }

    private static byte[] Scale(byte[] wav, double volume)
    {
        if (!Pcm.TryRead(wav, out var rate, out var samples))
        {
            return wav;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (int)Math.Round(samples[i] * volume);
            samples[i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        return Pcm.Wrap(samples, rate);
    }

    private readonly record struct Step(string File, int PauseMs)
    {
        public static Step Audio(string file) => new(file, 0);
        public static Step Pause(int ms) => new("", ms);
    }
}

public static class FahrAnsage
{
    public static bool Active { get; set; }
}

file static class Pcm
{
    public static bool TryRead(byte[] wav, out int rate, out short[] samples)
    {
        rate = 0;
        samples = [];
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I')
        {
            return false;
        }

        var offset = 12;
        short channels = 1;
        short bits = 16;
        var dataAt = -1;
        var dataLen = 0;
        while (offset + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var size = BitConverter.ToInt32(wav, offset + 4);
            offset += 8;
            if (size < 0 || offset + size > wav.Length)
            {
                break;
            }

            if (id == "fmt " && size >= 16)
            {
                channels = BitConverter.ToInt16(wav, offset + 2);
                rate = BitConverter.ToInt32(wav, offset + 4);
                bits = BitConverter.ToInt16(wav, offset + 14);
            }
            else if (id == "data")
            {
                dataAt = offset;
                dataLen = size;
                break;
            }

            offset += size + (size % 2);
        }

        if (dataAt < 0 || bits != 16 || channels < 1 || rate <= 0)
        {
            return false;
        }

        var frames = dataLen / (channels * 2);
        samples = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += BitConverter.ToInt16(wav, dataAt + (i * channels + c) * 2);
            }

            samples[i] = (short)(sum / channels);
        }

        return true;
    }

    public static double Rms(short[] samples, int from, int len)
    {
        double sum = 0;
        var to = Math.Min(samples.Length, from + len);
        if (to <= from)
        {
            return 0;
        }

        for (var i = from; i < to; i++)
        {
            var v = (double)samples[i];
            sum += v * v;
        }

        return Math.Sqrt(sum / (to - from));
    }

    public static byte[] Wrap(short[] samples, int rate)
    {
        var data = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        var wav = new byte[44 + data.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + data.Length);
        System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), rate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), rate * 2);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BitConverter.TryWriteBytes(wav.AsSpan(40), data.Length);
        data.CopyTo(wav, 44);
        return wav;
    }
}
