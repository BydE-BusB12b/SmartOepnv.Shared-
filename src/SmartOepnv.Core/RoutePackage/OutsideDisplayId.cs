namespace SmartOepnv.Core.RoutePackage;

/// <summary>Stabile ID für Außenanzeigen-Ziele (Umbenennung bricht Haltestellen-Verknüpfung nicht).</summary>
public static class OutsideDisplayId
{
    /// <summary>Pipe-Index nach Protokoll-Tag (siehe <see cref="OutsideDisplayProgram"/>).</summary>
    public const int StoragePartIndex = 13;

    public static string NewId() => Guid.NewGuid().ToString("N");

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
        // Nur alphanumerisch (GUID "N" oder Legacy)
        return new string(trimmed.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').ToArray());
    }

    public static bool IsValid(string? raw)
    {
        var id = Normalize(raw);
        return id.Length >= 8;
    }

    public static string Ensure(string? existing)
    {
        var id = Normalize(existing);
        return IsValid(id) ? id : NewId();
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
