namespace SmartOepnv.Core.RoutePackage;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Vorlage in der Ansagen-Kartei (managedAnnouncementTemplates) – Handy-kompatibel.
/// Jede Ansage hat eine feste 4-stellige Kennung (announcementCode).
/// </summary>
public sealed class ManagedAnnouncementTemplateItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Verknüpfung zur Haltestelle in managedStopTemplates (Feld id).</summary>
    public string StopTemplateId { get; set; } = string.Empty;

    /// <summary>4-stellige Kennung, z. B. 0001–9999.</summary>
    public string AnnouncementCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Planer: Linien, die diese Ansage nutzen könnten (kommagetrennt). Nur Suche, nicht in der Kartei-Liste.</summary>
    public string Lines { get; set; } = string.Empty;

    /// <summary>haltestelle | sonder | sonstiges</summary>
    public string Category { get; set; } = "haltestelle";

    public string EmbeddedSoundFileName { get; set; } = string.Empty;

    /// <summary>In Sonderansagen-Listen (ITCS) anzeigen – entspricht Android-Slider.</summary>
    public bool IncludeInSpecialAnnouncements { get; set; }

    public bool IncludeGong { get; set; }
    public bool IncludeSondergong { get; set; }
    public bool IncludeNextStopGerman { get; set; }
    public bool IncludeNextStopMp3 { get; set; }
    public bool IncludeFollowingStops { get; set; }

    /// <summary>Dateiname des Sondergongs in embeddedSounds (nur wenn IncludeSondergong).</summary>
    public string SondergongFileName { get; set; } = string.Empty;

    /// <summary>Audio/Pause-Schritte für App-Sequenz-Wiedergabe (ohne Standard-Prefix).</summary>
    public List<AnnouncementSequenceEntry> AnnouncementSequence { get; set; } = [];

    /// <summary>Nur Planer: lokale Audiodatei vor dem Einbetten in embeddedSounds.</summary>
    public string? LocalAudioPath { get; set; }

    public bool HasAssignedAudio => !string.IsNullOrWhiteSpace(LocalAudioPath);

    /// <summary>Anzeige in der Liste (✓ = Ton zugeordnet, ⚠ = noch keine Tondatei).</summary>
    public string DisplayLabel => FormatDisplayLabel(
        HasAssignedAudio || !string.IsNullOrWhiteSpace(EmbeddedSoundFileName));

    /// <summary>Zusammenfüge-Datei (<c>…_zusammen.wav</c>) – in der Kartei gelb markieren.</summary>
    public bool IsMergedAnnouncement =>
        AnnouncementSequenceExport.IsLegacyMergedFileName(EmbeddedSoundFileName);

    public void NotifyDisplayLabelChanged()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(IsMergedAnnouncement));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string FormatDisplayLabel(bool hasAudio)
    {
        var code = NormalizeCode(AnnouncementCode);
        var name = string.IsNullOrWhiteSpace(DisplayName) ? "Ohne Bezeichnung" : DisplayName.Trim();
        var desc = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        var prefix = hasAudio ? "✓ " : "⚠ ";
        var title = desc is null ? name : $"{name} – {desc}";
        return string.IsNullOrEmpty(code) ? $"{prefix}{title}" : $"{prefix}{code} – {title}";
    }

    public static string NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        if (digits.Length >= 4)
        {
            return digits[^4..];
        }

        return digits.PadLeft(4, '0');
    }

    public static bool IsValidCode(string? raw) =>
        NormalizeCode(raw).Length == 4;

    public static string SuggestNextCode(IEnumerable<string?> existingCodes)
    {
        var used = CollectUsedCodeNumbers(existingCodes);

        for (var i = 1; i <= 9999; i++)
        {
            if (!used.Contains(i))
            {
                return i.ToString("D4");
            }
        }

        return "0001";
    }

    /// <summary>
    /// Nächste freie ID ab der höchsten vergebenen Nummer (für Mehrfach-Import ohne Lücken füllen).
    /// </summary>
    public static string SuggestNextSequentialCode(IEnumerable<string?> existingCodes)
    {
        var used = CollectUsedCodeNumbers(existingCodes);
        var next = used.Count > 0 ? used.Max() + 1 : 1;
        while (next <= 9999 && used.Contains(next))
        {
            next++;
        }

        return next <= 9999 ? next.ToString("D4") : "9999";
    }

    public static IReadOnlyList<string> AllocateSequentialCodes(IEnumerable<string?> existingCodes, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var used = CollectUsedCodeNumbers(existingCodes);
        var next = used.Count > 0 ? used.Max() + 1 : 1;
        var codes = new List<string>(count);
        while (codes.Count < count && next <= 9999)
        {
            while (used.Contains(next))
            {
                next++;
            }

            if (next > 9999)
            {
                break;
            }

            codes.Add(next.ToString("D4"));
            used.Add(next);
            next++;
        }

        return codes;
    }

    private static HashSet<int> CollectUsedCodeNumbers(IEnumerable<string?> existingCodes)
    {
        var used = new HashSet<int>();
        foreach (var raw in existingCodes)
        {
            var norm = NormalizeCode(raw);
            if (norm.Length == 4 && int.TryParse(norm, out var n) && n >= 0 && n <= 9999)
            {
                used.Add(n);
            }
        }

        return used;
    }

    public static string DefaultEmbeddedFileName(string code, string displayName)
    {
        var safeName = string.Concat(
            (displayName ?? string.Empty).Trim()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "ansage";
        }

        if (safeName.Length > 40)
        {
            safeName = safeName[..40];
        }

        return $"{NormalizeCode(code)}_{safeName}.mp3";
    }
}
