using System.IO;
using System.Text.Json;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Lokaler Arbeits-Speicher für das vollständige Route-Paket (Routen, Navidaten, Fahrer, …).
/// Dieselbe <c>routes_export.json</c> wie auf Dropbox – damit andere Planer-PCs nach
/// Upload/Download denselben Stand haben (routePathDrafts, Hinweise, …).
/// Zusätzlich Unterordner <c>embedded_sounds</c> für Tondateien aus dem JSON.
/// </summary>
public sealed class LocalWorkspaceStore
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _packagePath;
    private readonly string _metaPath;

    public LocalWorkspaceStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _packagePath = Path.Combine(workspaceDir, DropboxConstants.RouteFileName);
        _metaPath = Path.Combine(workspaceDir, "workspace.meta.json");
    }

    public string PackageFilePath => _packagePath;

    public bool Exists => File.Exists(_packagePath);

    public string? TryLoadPackageJson()
    {
        if (!File.Exists(_packagePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(_packagePath);
        }
        catch
        {
            return null;
        }
    }

    public WorkspaceMeta? TryLoadMeta()
    {
        if (!File.Exists(_metaPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_metaPath);
            return JsonSerializer.Deserialize<WorkspaceMeta>(json, MetaJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SavePackage(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var dir = Path.GetDirectoryName(_packagePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        SafeDataFileStore.WriteAllText(_packagePath, json);
        var meta = new WorkspaceMeta
        {
            LastSavedUtc = DateTimeOffset.UtcNow,
            Source = source,
            PackageTimestamp = ExtractPackageTimestamp(json)
        };
        SafeDataFileStore.WriteAllText(_metaPath, JsonSerializer.Serialize(meta, MetaJsonOptions));
    }

    public static long ExtractPackageTimestamp(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var value))
            {
                return value;
            }
        }
        catch
        {
            // ignore parse errors
        }

        return 0;
    }
}
