using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Planer: gespeicherte Snapshots des Route-Pakets (ohne lokale Fahrer/Fahrzeug-Priorität beim Laden).</summary>
public sealed class PlannerPackageVersionStore
{
    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _versionsDir;
    private readonly string _indexPath;

    public PlannerPackageVersionStore(string appSubfolder)
    {
        _versionsDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace", "versions");
        Directory.CreateDirectory(_versionsDir);
        _indexPath = Path.Combine(_versionsDir, "index.json");
    }

    public string VersionsDirectory => _versionsDir;

    public IReadOnlyList<PlannerPackageVersionInfo> List()
    {
        var index = LoadIndex();
        return index.OrderByDescending(v => v.SavedAtUtc).ToList();
    }

    public PlannerPackageVersionInfo Save(string label, string packageJson)
    {
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(_versionsDir, $"{id}.json");
        File.WriteAllText(filePath, packageJson);

        var stats = RoutePackageService.ParseStatsPublic(packageJson);
        var info = new PlannerPackageVersionInfo
        {
            Id = id,
            Label = label.Trim(),
            SavedAtUtc = DateTimeOffset.UtcNow,
            ByteSize = System.Text.Encoding.UTF8.GetByteCount(packageJson),
            RouteCount = stats.RouteCount,
            PackageTimestampMs = stats.Timestamp
        };

        var index = LoadIndex();
        index.RemoveAll(v => string.Equals(v.Id, id, StringComparison.Ordinal));
        index.Add(info);
        SaveIndex(index);
        return info;
    }

    public string? TryLoadPackageJson(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var safeId = Path.GetFileName(id);
        var filePath = Path.Combine(_versionsDir, $"{safeId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }
    }

    public bool TryDelete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var safeId = Path.GetFileName(id);
        var filePath = Path.Combine(_versionsDir, $"{safeId}.json");
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                return false;
            }
        }

        var index = LoadIndex();
        var removed = index.RemoveAll(v => string.Equals(v.Id, safeId, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            SaveIndex(index);
        }

        return removed || !File.Exists(filePath);
    }

    private List<PlannerPackageVersionInfo> LoadIndex()
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_indexPath);
            var entries = JsonSerializer.Deserialize<List<PlannerPackageVersionIndexEntry>>(json, IndexJsonOptions);
            if (entries is null)
            {
                return [];
            }

            return entries.Select(e => new PlannerPackageVersionInfo
            {
                Id = e.Id ?? string.Empty,
                Label = e.Label ?? string.Empty,
                SavedAtUtc = e.SavedAtUtc,
                ByteSize = e.ByteSize,
                RouteCount = e.RouteCount,
                PackageTimestampMs = e.PackageTimestampMs
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    private void SaveIndex(List<PlannerPackageVersionInfo> index)
    {
        var entries = index.Select(v => new PlannerPackageVersionIndexEntry
        {
            Id = v.Id,
            Label = v.Label,
            SavedAtUtc = v.SavedAtUtc,
            ByteSize = v.ByteSize,
            RouteCount = v.RouteCount,
            PackageTimestampMs = v.PackageTimestampMs
        }).ToList();
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(entries, IndexJsonOptions));
    }

    private sealed class PlannerPackageVersionIndexEntry
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public long ByteSize { get; set; }
        public int RouteCount { get; set; }

        public long? PackageTimestampMs { get; set; }
    }
}
