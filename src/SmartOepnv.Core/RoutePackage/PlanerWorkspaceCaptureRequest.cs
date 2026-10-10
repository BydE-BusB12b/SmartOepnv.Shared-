namespace SmartOepnv.Core.RoutePackage;

/// <summary>Optionen für schnelleres Erfassen des Planer-Arbeitsstands (z. B. beim Beenden).</summary>
public sealed class PlanerWorkspaceCaptureRequest
{
    /// <summary>Kein erneutes Flush aller ViewModels (bereits vor dem Export erfolgt).</summary>
    public bool SkipFlush { get; init; }

    /// <summary>Routes-Paket aus dem Cache statt erneut aus dem Editor serialisieren.</summary>
    public bool PreferCachedRoutesJson { get; init; }

    /// <summary>Unveränderte Versions-Snapshots aus der letzten planer_workspace.json wiederverwenden.</summary>
    public IReadOnlyList<PlannerPackageVersionSnapshotData>? ReuseSnapshotPackageJsonFrom { get; init; }

    /// <summary>Unveränderte Ansagen-Manifest-Einträge (Größe/Hash) aus der letzten planer_workspace.json wiederverwenden.</summary>
    public IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? ReuseAnnouncementRawSoundsFrom { get; init; }
}
