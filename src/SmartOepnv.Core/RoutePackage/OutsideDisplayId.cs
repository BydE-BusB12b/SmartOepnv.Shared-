namespace SmartOepnv.Core.RoutePackage;

/// <summary>Stabile ID für Außenanzeigen-Ziele (Umbenennung bricht Haltestellen-Verknüpfung nicht).</summary>
public static class OutsideDisplayId
{
    /// <summary>Pipe-Index nach Protokoll-Tag (siehe <see cref="OutsideDisplayProgram"/>).</summary>
    public const int StoragePartIndex = 13;

    /// <summary>Platzhalter bis <see cref="NewUniqueId"/> die nächste freie Nummer vergibt.</summary>
    public static string NewId() => NewUniqueId(Array.Empty<string?>());

    /// <summary>Nächste freie vierstellige ID (0001–9999) aus bereits vergebenen IDs.</summary>
    public static string NewUniqueId(IEnumerable<string?> existingIds)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in existingIds)
        {
            var id = Normalize(raw);
            if (IsFourDigit(id))
            {
                used.Add(id);
            }
        }

        for (var n = 1; n <= 9999; n++)
        {
            var candidate = n.ToString("D4");
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return "9999";
    }

    /// <summary>Stabile Legacy-ID für Einträge ohne gespeicherte ID (Name+Protokoll).</summary>
    public static string LegacyStable(string? name, OutsideDisplayProtocolKind protocol)
    {
        var key = $"{protocol}|{(name ?? string.Empty).Trim()}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        // 4-stellige Nummer, GUID "N" oder Legacy-Hash
        return new string(trimmed.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').ToArray());
    }

    /// <summary>Benutzer-Eingabe → 0001–9999, sonst null.</summary>
    public static string? TryNormalizeEditableId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var n) || n is < 1 or > 9999)
        {
            return null;
        }

        return n.ToString("D4");
    }

    public static bool IsFourDigit(string? raw)
    {
        var id = Normalize(raw);
        return id.Length == 4 &&
               id.All(char.IsDigit) &&
               int.TryParse(id, out var n) &&
               n is >= 1 and <= 9999;
    }

    public static bool IsValid(string? raw)
    {
        var id = Normalize(raw);
        return IsFourDigit(id) || id.Length >= 8;
    }

    /// <summary>Kurznummer für UI: 4-stellig gespeichert, sonst (nur Legacy) stabil aus langer ID.</summary>
    public static string ToDisplayNumber(string? raw)
    {
        var id = Normalize(raw);
        if (IsFourDigit(id))
        {
            return id;
        }

        if (string.IsNullOrEmpty(id))
        {
            return "----";
        }

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        var value = ((hash[0] << 8) | hash[1]) % 10_000;
        if (value == 0)
        {
            value = 1;
        }

        return value.ToString("D4");
    }

    public static string Ensure(string? existing)
    {
        var id = Normalize(existing);
        return IsValid(id) ? id : NewId();
    }

    public static string EnsureUnique(string? existing, IEnumerable<string?> existingIds)
    {
        var id = Normalize(existing);
        if (IsFourDigit(id))
        {
            return id;
        }

        return IsValid(id) ? id : NewUniqueId(existingIds);
    }

    /// <summary>
    /// Vergibt allen Programmen eindeutige 0001–9999-IDs; behält bestehende freie Viersteller.
    /// </summary>
    public static bool AssignUniqueFourDigitIds(IEnumerable<OutsideDisplayProgram> programs)
    {
        var changed = false;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in programs)
        {
            var id = Normalize(program.Id);
            if (IsFourDigit(id) && used.Add(id))
            {
                continue;
            }

            var next = NewUniqueId(used);
            used.Add(next);
            if (!string.Equals(program.Id, next, StringComparison.Ordinal))
            {
                program.Id = next;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>ID aus Pipe-Eintrag lesen (fehlend = leer).</summary>
    public static string FromStorageEntry(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return string.Empty;
        }

        var parts = entry.Split('|');
        if (parts.Length <= StoragePartIndex)
        {
            return string.Empty;
        }

        try
        {
            var raw = parts[StoragePartIndex];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            // Wie andere Textfelder: Base64-UTF8 oder Klartext
            try
            {
                var bytes = Convert.FromBase64String(raw);
                return Normalize(System.Text.Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                return Normalize(raw);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string NameFromStorageEntry(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return string.Empty;
        }

        return entry.Split('|')[0].Trim();
    }
}
