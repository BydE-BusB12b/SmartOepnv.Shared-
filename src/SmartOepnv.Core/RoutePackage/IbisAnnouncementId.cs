namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Nummer am IBIS-Ansagegerät (HAG/HIG). Versand als DS036 <c>hP</c> + vier Ziffern.
/// Die Ansage trägt die Nummer; Haltestellen übernehmen sie, solange dort nichts Abweichendes steht.
/// </summary>
public static class IbisAnnouncementIds
{
    public static string Normalize(string? raw)
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

        if (digits.Length > 4)
        {
            digits = digits[^4..];
        }

        return digits.PadLeft(4, '0');
    }

    /// <summary>Ansage zur Haltestelle: zuerst Verknüpfung <c>stopTemplateId</c>, sonst gleiche Tondatei.</summary>
    public static string Lookup(
        ManagedStopTemplateItem stop,
        IEnumerable<ManagedAnnouncementTemplateItem>? announcements)
    {
        if (announcements is null)
        {
            return string.Empty;
        }

        ManagedAnnouncementTemplateItem? match = null;
        if (!string.IsNullOrWhiteSpace(stop.Id))
        {
            match = announcements.FirstOrDefault(a =>
                string.Equals(a.StopTemplateId, stop.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(Normalize(a.IbisAnnouncementId)));
        }

        if (match is null && !string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))
        {
            var sound = stop.EmbeddedSoundFileName.Trim();
            match = announcements.FirstOrDefault(a =>
                string.Equals(a.EmbeddedSoundFileName?.Trim(), sound, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(Normalize(a.IbisAnnouncementId)));
        }

        return match is null ? string.Empty : Normalize(match.IbisAnnouncementId);
    }

    /// <summary>Nur wenn die Haltestelle noch keine eigene Nummer hat.</summary>
    public static bool FillIfEmpty(
        ManagedStopTemplateItem stop,
        IEnumerable<ManagedAnnouncementTemplateItem>? announcements)
    {
        if (!string.IsNullOrEmpty(stop.IbisAnnouncementId))
        {
            return false;
        }

        var id = Lookup(stop, announcements);
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        stop.IbisAnnouncementId = id;
        return true;
    }

    /// <summary>Nach Tonzuordnung: Nummer der verknüpften Ansage setzen, falls vorhanden.</summary>
    public static bool ApplyLinked(
        ManagedStopTemplateItem stop,
        IEnumerable<ManagedAnnouncementTemplateItem>? announcements)
    {
        var id = Lookup(stop, announcements);
        if (string.IsNullOrEmpty(id) ||
            string.Equals(stop.IbisAnnouncementId, id, StringComparison.Ordinal))
        {
            return false;
        }

        stop.IbisAnnouncementId = id;
        return true;
    }

    /// <summary>
    /// Ansage-ID auf passende Haltestellen schreiben.
    /// Eine abweichende Nummer an der Haltestelle bleibt, solange sie nicht die bisherige Ansage-ID war.
    /// </summary>
    public static int Propagate(
        IEnumerable<ManagedStopTemplateItem> stops,
        IEnumerable<ManagedAnnouncementTemplateItem> current,
        IEnumerable<ManagedAnnouncementTemplateItem>? previous)
    {
        var count = 0;
        foreach (var stop in stops)
        {
            var id = Lookup(stop, current);
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var previousId = Lookup(stop, previous);
            var empty = string.IsNullOrEmpty(stop.IbisAnnouncementId);
            var matchesPrevious = !string.IsNullOrEmpty(previousId) &&
                                  string.Equals(stop.IbisAnnouncementId, previousId, StringComparison.Ordinal);
            if (!empty && !matchesPrevious)
            {
                continue;
            }

            if (string.Equals(stop.IbisAnnouncementId, id, StringComparison.Ordinal))
            {
                continue;
            }

            stop.IbisAnnouncementId = id;
            count++;
        }

        return count;
    }
}
