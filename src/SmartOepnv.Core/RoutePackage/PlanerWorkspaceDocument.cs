using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.Mitteilungen;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Vollständiger Planer-Arbeitsstand für Dropbox (planer_workspace.json).
/// Enthält Routen-Bearbeitung, Personal/Fahrzeuge, Fahrzeugdisposition und SEV-Entwürfe.
/// </summary>
public sealed class PlanerWorkspaceDocument
{
    public const string Kind = "planer_workspace";
    public const string FileVersion = "1.1";

    public string Version { get; set; } = FileVersion;

    public string DocumentType { get; set; } = Kind;

    public long SavedAtUtcMs { get; set; }

    /// <summary>Routen liegen in Dropbox unter planer_routes.json.</summary>
    public bool RoutesStoredExternally { get; set; }

    /// <summary>Routen-Paket nur für den Planer (nicht routes_export.json in Dropbox).</summary>
    public string? RoutesPackageJson { get; set; }

    public PlannerLocalOverlayData PlannerOverlay { get; set; } = new();

    public List<VehicleDispositionAssignment> VehicleDispositionAssignments { get; set; } = [];

    public List<DriverDispositionAssignment> DriverDispositionAssignments { get; set; } = [];

    public List<SevSignDraft> SevSignDrafts { get; set; } = [];

    public List<MitteilungDraft> MitteilungDrafts { get; set; } = [];

    public List<DutyTemplate> DutyTemplates { get; set; } = [];

    /// <summary>Gespeicherte Planer-Snapshots (Übersicht → Versionen).</summary>
    public List<PlannerPackageVersionSnapshotData> PackageVersionSnapshots { get; set; } = [];

    /// <summary>
    /// Ansagen-Rohdateien: Manifest (Dateiname, Größe, SHA-256) – Binärdaten liegen in Dropbox unter planer_ansagen_roh/.
    /// Legacy-Importe mit Base64 in <see cref="Data"/> werden weiterhin unterstützt.
    /// </summary>
    public Dictionary<string, PlanerWorkspaceBinaryPayload> AnnouncementRawSounds { get; set; } = [];
}
