namespace SmartOepnv.Core.RoutePackage;

public sealed class PlannerPackageVersionInfo
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }

    public long ByteSize { get; set; }

    public int RouteCount { get; set; }

    public long? PackageTimestampMs { get; set; }

    public string DisplayLine
    {
        get
        {
            var ts = PackageTimestampMs is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(PackageTimestampMs.Value).LocalDateTime.ToString("dd.MM.yyyy HH:mm")
                : SavedAtUtc.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
            var size = $"{ByteSize / 1024} KB";
            return string.IsNullOrWhiteSpace(Label)
                ? $"{ts} · {RouteCount} Routen · {size}"
                : $"{Label} · {ts} · {RouteCount} Routen";
        }
    }
}
