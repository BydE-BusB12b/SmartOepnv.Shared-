namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Vorlagen-Haltestelle (managedStopTemplates) – unabhängig von Routen, Handy-kompatibel.
/// </summary>
public sealed class ManagedStopTemplateItem
{
    public const int DefaultRadiusMeters = 25;

    /// <summary>Standardtitel beim Anlegen – wird nicht dauerhaft gespeichert, solange keine Stammdaten fehlen.</summary>
    public const string PlaceholderStopName = "Neue Haltestelle";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>5-stellige Kennung der Haltestelle (z. B. 00001–99999), nur Planer / Verwaltung.</summary>
    public string StopCode { get; set; } = string.Empty;

    public string StopNameItcs { get; set; } = string.Empty;
    public string StopDisplay { get; set; } = string.Empty;
    public string VrrStopId { get; set; } = string.Empty;
    public string DirectionDescription { get; set; } = string.Empty;
    public string AnnouncementLat { get; set; } = string.Empty;
    public string AnnouncementLng { get; set; } = string.Empty;
    public string StopLat { get; set; } = string.Empty;
    public string StopLng { get; set; } = string.Empty;
    public int RadiusMeters { get; set; } = DefaultRadiusMeters;
    public string ExternalSoundUri { get; set; } = string.Empty;
    public string EmbeddedSoundFileName { get; set; } = string.Empty;

    /// <summary>Nur Planer: lokale Audiodatei vor dem Einbetten in embeddedSounds.</summary>
    public string? LocalAudioPath { get; set; }

    /// <summary>Eingebettete Ansage, lokale Datei oder URI gesetzt.</summary>
    public bool HasAssignedAudio =>
        !string.IsNullOrWhiteSpace(EmbeddedSoundFileName) ||
        !string.IsNullOrWhiteSpace(LocalAudioPath) ||
        !string.IsNullOrWhiteSpace(ExternalSoundUri);

    public string FormatDisplayLabel(bool hasAudio)
    {
        var code = PlannerStopCode.Normalize(StopCode);
        var name = string.IsNullOrWhiteSpace(StopNameItcs) ? "Ohne Name" : StopNameItcs.Trim();
        var dir = string.IsNullOrWhiteSpace(DirectionDescription) ? null : DirectionDescription.Trim();
        var prefix = hasAudio ? "✓ " : "⚠ ";
        var title = dir is null ? name : $"{name} – {dir}";
        return string.IsNullOrEmpty(code) ? $"{prefix}{title}" : $"{prefix}{code} – {title}";
    }

    public string DisplayLabel => FormatDisplayLabel(HasAssignedAudio);

    /// <summary>Ansagen-Kartei: ohne Richtung/Linie (nur Haltestellen-ID und Name).</summary>
    public string FormatAnnouncementsLibraryDisplayLabel(bool hasAudio)
    {
        var code = PlannerStopCode.Normalize(StopCode);
        var name = string.IsNullOrWhiteSpace(StopNameItcs) ? "Ohne Name" : StopNameItcs.Trim();
        var prefix = hasAudio ? "✓ " : "⚠ ";
        return string.IsNullOrEmpty(code) ? $"{prefix}{name}" : $"{prefix}{code} – {name}";
    }

    public string AnnouncementsLibraryDisplayLabel => FormatAnnouncementsLibraryDisplayLabel(HasAssignedAudio);

    public static bool IsPlaceholderStopName(string? name) =>
        string.Equals(name?.Trim(), PlaceholderStopName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Leere „Neue Haltestelle“-Entwürfe ohne Stammdaten (werden nicht exportiert).</summary>
    public bool IsEmptyDraft() => !HasPersistableContent();

    public bool HasPersistableContent()
    {
        if (!string.IsNullOrWhiteSpace(VrrStopId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(StopDisplay) ||
            !string.IsNullOrWhiteSpace(DirectionDescription) ||
            !string.IsNullOrWhiteSpace(EmbeddedSoundFileName) ||
            !string.IsNullOrWhiteSpace(LocalAudioPath) ||
            !string.IsNullOrWhiteSpace(ExternalSoundUri))
        {
            return true;
        }

        if (CoordinateFormatting.TryParseParts(AnnouncementLat, AnnouncementLng, out _, out _))
        {
            return true;
        }

        if (CoordinateFormatting.TryParseParts(StopLat, StopLng, out _, out _))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(StopNameItcs) && !IsPlaceholderStopName(StopNameItcs);
    }

    public RouteStopItem ToRouteStop(string routeName)
    {
        var gps = FormatCoordinatePair(AnnouncementLat, AnnouncementLng);
        var stop = FormatCoordinatePair(StopLat, StopLng);
        return new RouteStopItem
        {
            RouteName = routeName,
            PlannerStopCode = PlannerStopCode.Normalize(StopCode),
            Name = StopNameItcs.Trim(),
            StopDisplay = StopDisplay.Trim(),
            VrrStopId = VrrStopId.Trim(),
            GpsCoordinates = gps,
            StopCoordinates = string.IsNullOrEmpty(stop) ? gps : stop,
            Radius = RadiusMeters > 0 ? RadiusMeters : DefaultRadiusMeters,
            EmbeddedSoundFileName = EmbeddedSoundFileName.Trim(),
            IsAnnouncementEnabled = true
        };
    }

    public static ManagedStopTemplateItem FromRouteStop(RouteStopItem stop)
    {
        var (annLat, annLon) = ParseCoordinatePair(stop.GpsCoordinates);
        var (stopLat, stopLon) = ParseCoordinatePair(stop.StopCoordinates);
        return new ManagedStopTemplateItem
        {
            StopCode = PlannerStopCode.Normalize(stop.PlannerStopCode),
            StopNameItcs = stop.Name,
            StopDisplay = stop.StopDisplay,
            VrrStopId = stop.VrrStopId,
            AnnouncementLat = annLat,
            AnnouncementLng = annLon,
            StopLat = stopLat,
            StopLng = stopLon,
            RadiusMeters = stop.Radius > 0 ? stop.Radius : DefaultRadiusMeters,
            EmbeddedSoundFileName = stop.EmbeddedSoundFileName
        };
    }

    private static string FormatCoordinatePair(string lat, string lon)
    {
        if (!CoordinateFormatting.TryParseParts(lat, lon, out var latVal, out var lonVal))
        {
            return string.Empty;
        }

        return $"{CoordinateFormatting.FormatComponent(latVal)},{CoordinateFormatting.FormatComponent(lonVal)}";
    }

    private static (string Lat, string Lon) ParseCoordinatePair(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, string.Empty);
        }

        if (CoordinateFormatting.TryParsePair(raw, out var lat, out var lon))
        {
            return (lat, lon);
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return (string.Empty, string.Empty);
        }

        return (parts[0], parts[1]);
    }
}
