namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Vorlage in der Ansagen-Kartei (managedAnnouncementTemplates) – Handy-kompatibel.
/// Jede Ansage hat eine feste 4-stellige Kennung (announcementCode).
/// </summary>
public sealed class ManagedAnnouncementTemplateItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Verknüpfung zur Haltestelle in managedStopTemplates (Feld id).</summary>
    public string StopTemplateId { get; set; } = string.Empty;

    /// <summary>4-stellige Kennung, z. B. 0001–9999.</summary>
    public string AnnouncementCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>haltestelle | sonder | sonstiges</summary>
    public string Category { get; set; } = "haltestelle";

    public string EmbeddedSoundFileName { get; set; } = string.Empty;

    /// <summary>In Sonderansagen-Listen (ITCS) anzeigen – entspricht Android-Slider.</summary>
    public bool IncludeInSpecialAnnouncements { get; set; }

    /// <summary>Nur Planer: lokale Audiodatei vor dem Einbetten in embeddedSounds.</summary>
    public string? LocalAudioPath { get; set; }

    public bool HasAssignedAudio =>
        !string.IsNullOrWhiteSpace(EmbeddedSoundFileName) ||
        !string.IsNullOrWhiteSpace(LocalAudioPath);

    /// <summary>Anzeige in der Liste (✓ = Ton zugeordnet, ⚠ = noch keine Tondatei).</summary>
    public string FormatDisplayLabel(bool hasAudio)
    {
        var code = NormalizeCode(AnnouncementCode);
        var name = string.IsNullOrWhiteSpace(DisplayName) ? "Ohne Bezeichnung" : DisplayName.Trim();
        var prefix = hasAudio ? "✓ " : "⚠ ";
        return string.IsNullOrEmpty(code) ? $"{prefix}{name}" : $"{prefix}{code} – {name}";
    }

    public string DisplayLabel => FormatDisplayLabel(HasAssignedAudio);

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
        var used = new HashSet<int>();
        foreach (var raw in existingCodes)
        {
            var norm = NormalizeCode(raw);
            if (norm.Length == 4 && int.TryParse(norm, out var n) && n >= 0 && n <= 9999)
            {
                used.Add(n);
            }
        }

        for (var i = 1; i <= 9999; i++)
        {
            if (!used.Contains(i))
            {
                return i.ToString("D4");
            }
        }

        return "0001";
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
