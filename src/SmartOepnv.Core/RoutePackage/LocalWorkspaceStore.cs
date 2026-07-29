using System.IO;
using System.Text.Json;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Lokaler Arbeits-Speicher für das Route-Paket (Routen, Navidaten, Fahrer, …).
/// <c>routes_cache.json</c> ist nur der lokale Zwischenspeicher (kein App-Export).
/// Schwere Audio-Blöcke liegen in <c>routes_cache.heavymedia.json</c>.
/// Dropbox <c>routes_export.json</c> für die Apps bleibt der manuelle Vollstand.
/// Zusätzlich Unterordner <c>embedded_sounds</c> und <c>ansagen_roh</c>.
/// </summary>
public sealed class LocalWorkspaceStore
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _packagePath;
    private readonly string _heavyMediaPath;
    private readonly string _legacyPackagePath;
    private readonly string _legacyHeavyMediaPath;
    private readonly string _metaPath;

    public LocalWorkspaceStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _packagePath = Path.Combine(workspaceDir, DropboxConstants.LocalRouteCacheFileName);
        _heavyMediaPath = Path.Combine(workspaceDir, DropboxConstants.LocalRouteCacheHeavyMediaFileName);
        _legacyPackagePath = Path.Combine(workspaceDir, DropboxConstants.RouteFileName);
        _legacyHeavyMediaPath = Path.Combine(workspaceDir, "routes_export.heavymedia.json");
        _metaPath = Path.Combine(workspaceDir, "workspace.meta.json");
        TryMigrateLegacyPackageFiles();
    }

    public string PackageFilePath => _packagePath;

    public string HeavyMediaSidecarPath => _heavyMediaPath;

    public bool Exists => File.Exists(_packagePath) || File.Exists(_legacyPackagePath);

    public string? TryLoadPackageJson()
    {
        TryMigrateLegacyPackageFiles();
        if (!File.Exists(_packagePath))
        {
            return null;
        }

        try
        {
            var body = File.ReadAllText(_packagePath);
            return AttachHeavyMediaSidecarIfNeeded(body);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Speichert den schlanken Paket-Body. Audio-Sidecar wird nur geschrieben, wenn
    /// <paramref name="heavyMediaJson"/> gesetzt ist (Ansagen-Änderung / Erst-Migration).
    /// </summary>
    public void SavePackageBody(
        string bodyJson,
        string source,
        bool archivePrevious = false,
        string? heavyMediaJson = null,
        bool updateHeavyMediaSidecar = false)
    {
        if (string.IsNullOrWhiteSpace(bodyJson))
        {
            return;
        }

        var dir = Path.GetDirectoryName(_packagePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Einmalige Migration: bisher volles JSON → Body + Sidecar trennen.
        if (!updateHeavyMediaSidecar &&
            heavyMediaJson is null &&
            !File.Exists(_heavyMediaPath) &&
            PackageJsonContainsHeavyMedia(bodyJson))
        {
            if (TrySplitHeavyMedia(bodyJson, out var slim, out var heavy))
            {
                bodyJson = slim;
                heavyMediaJson = heavy;
                updateHeavyMediaSidecar = true;
            }
        }
        else if (PackageJsonContainsHeavyMedia(bodyJson))
        {
            // Keine Audio-Blöcke mehr im lokalen Cache ablegen.
            if (TrySplitHeavyMedia(bodyJson, out var slim, out var heavy))
            {
                bodyJson = slim;
                if (updateHeavyMediaSidecar || !File.Exists(_heavyMediaPath))
                {
                    heavyMediaJson = heavy;
                    updateHeavyMediaSidecar = true;
                }
            }
        }

        var previousMeta = TryLoadMeta();
        SafeDataFileStore.WriteAllText(_packagePath, bodyJson, archivePrevious: archivePrevious);

        if (updateHeavyMediaSidecar && !string.IsNullOrWhiteSpace(heavyMediaJson))
        {
            SafeDataFileStore.WriteAllText(_heavyMediaPath, heavyMediaJson, archivePrevious: false);
        }

        var meta = new WorkspaceMeta
        {
            LastSavedUtc = DateTimeOffset.UtcNow,
            Source = source,
            PackageTimestamp = ExtractPackageTimestamp(bodyJson),
            LastMergedRouteUpdateTimestamp = previousMeta?.LastMergedRouteUpdateTimestamp
        };
        SafeDataFileStore.WriteAllText(_metaPath, JsonSerializer.Serialize(meta, MetaJsonOptions));
    }

    /// <summary>
    /// Alte lokale <c>routes_export.json</c> / <c>routes_export.heavymedia.json</c>
    /// einmalig nach <c>routes_cache*.json</c> umbenennen (Dropbox-App-Datei bleibt unberührt).
    /// </summary>
    private void TryMigrateLegacyPackageFiles()
    {
        try
        {
            if (!File.Exists(_packagePath) && File.Exists(_legacyPackagePath))
            {
                File.Move(_legacyPackagePath, _packagePath);
            }

            if (!File.Exists(_heavyMediaPath) && File.Exists(_legacyHeavyMediaPath))
            {
                File.Move(_legacyHeavyMediaPath, _heavyMediaPath);
            }
        }
        catch
        {
            // Beim nächsten Start erneut versuchen
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

    public void SavePackage(string json, string source, bool archivePrevious = false) =>
        SavePackageBody(json, source, archivePrevious, heavyMediaJson: null, updateHeavyMediaSidecar: false);

    public long GetLastMergedRouteUpdateTimestamp() =>
        TryLoadMeta()?.LastMergedRouteUpdateTimestamp ?? 0;

    public void SaveLastMergedRouteUpdateTimestamp(long timestamp)
    {
        var meta = TryLoadMeta() ?? new WorkspaceMeta();
        meta.LastMergedRouteUpdateTimestamp = timestamp;
        meta.LastSavedUtc = DateTimeOffset.UtcNow;
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

    private string AttachHeavyMediaSidecarIfNeeded(string body)
    {
        if (PackageJsonContainsHeavyMedia(body) || !File.Exists(_heavyMediaPath))
        {
            return body;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_heavyMediaPath));
            string? sounds = null;
            string? special = null;
            if (doc.RootElement.TryGetProperty("embeddedSounds", out var s))
            {
                sounds = s.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("specialAnnouncements", out var a))
            {
                special = a.GetRawText();
            }

            return EditableRoutePackage.InjectHeavyMediaProperties(body, sounds, special);
        }
        catch
        {
            return body;
        }
    }

    private static bool PackageJsonContainsHeavyMedia(string json) =>
        json.Contains("\"embeddedSounds\"", StringComparison.Ordinal) ||
        json.Contains("\"specialAnnouncements\"", StringComparison.Ordinal);

    private static bool TrySplitHeavyMedia(string json, out string slimBody, out string heavySidecar)
    {
        slimBody = json;
        heavySidecar = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? sounds = null;
            string? special = null;
            if (doc.RootElement.TryGetProperty("embeddedSounds", out var s))
            {
                sounds = s.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("specialAnnouncements", out var a))
            {
                special = a.GetRawText();
            }

            if (sounds is null && special is null)
            {
                return false;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("embeddedSounds") || prop.NameEquals("specialAnnouncements"))
                    {
                        continue;
                    }

                    prop.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            slimBody = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            heavySidecar = EditableRoutePackage.InjectHeavyMediaProperties("{}", sounds, special);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
