using SmartOepnv.Core.Sev;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Vollständiger Planer-Arbeitsstand für Dropbox (planer_workspace.json).
/// Enthält Routen-Bearbeitung, Personal/Fahrzeuge, Fahrzeugdisposition und SEV-Entwürfe.
/// </summary>
public sealed class PlanerWorkspaceDocument
{
    public const string Kind = "planer_workspace";
    public const string FileVersion = "1.0";

    public string Version { get; set; } = FileVersion;

    public string DocumentType { get; set; } = Kind;

    public long SavedAtUtcMs { get; set; }

    /// <summary>Routen-Paket nur für den Planer (nicht routes_export.json in Dropbox).</summary>
    public string? RoutesPackageJson { get; set; }

    public PlannerLocalOverlayData PlannerOverlay { get; set; } = new();

    public List<VehicleDispositionAssignment> VehicleDispositionAssignments { get; set; } = [];

    public List<DriverDispositionAssignment> DriverDispositionAssignments { get; set; } = [];

    public List<SevSignDraft> SevSignDrafts { get; set; } = [];

    /// <summary>Gespeicherte Planer-Snapshots (Übersicht → Versionen).</summary>
    public List<PlannerPackageVersionSnapshotData> PackageVersionSnapshots { get; set; } = [];
}
