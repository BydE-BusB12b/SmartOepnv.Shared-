namespace SmartOepnv.Core.RoutePackage;

/// <summary>Route-Paket-Snapshot für planer_workspace.json (Übersicht → Versionen).</summary>
public sealed class PlannerPackageVersionSnapshotData
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }

    public long ByteSize { get; set; }

    public int RouteCount { get; set; }

    public long? PackageTimestampMs { get; set; }

    public string PackageJson { get; set; } = string.Empty;
}
