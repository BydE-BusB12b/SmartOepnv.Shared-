namespace SmartOepnv.Core.RoutePackage;

public enum AnnouncementExportEntryKind
{
    Audio,
    Pause,
    /// <summary>Linienansage aus Routennamen (PassengerLine / Liniencode).</summary>
    Line,
    /// <summary>Standard-Ton „Nach.wav“.</summary>
    Nach,
    /// <summary>Endhaltestellen-Name (ohne Endhaltestellen-Ansage).</summary>
    RouteEndDestination
}

/// <summary>
/// Ein Schritt in der Haltestellen-Ansage-Sequenz (Export / App-Wiedergabe).
/// </summary>
public sealed class AnnouncementSequenceEntry
{
    public AnnouncementExportEntryKind Kind { get; set; }

    /// <summary>Dateiname unter embeddedSounds (nur bei Kind=Audio).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Pause in Sekunden (nur bei Kind=Pause).</summary>
    public double PauseSeconds { get; set; }
}
