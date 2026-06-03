using System.IO;
using System.Text.Json;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

public sealed class RoutePackageStats
{
    public int RouteCount { get; set; }
    public int StopCount { get; set; }
    public int DriverCount { get; set; }
    public int AnnouncementTemplateCount { get; set; }
    public string? Version { get; set; }
    public string? ExportType { get; set; }
    public long? Timestamp { get; set; }
}

public sealed class RoutePackageService
{
    private string? _currentJson;

    public bool HasPackage => !string.IsNullOrWhiteSpace(_currentJson);

    public string? CurrentJson => _currentJson;

    public RoutePackageStats Stats { get; private set; } = new();

    public EditableRoutePackage? Editor { get; private set; }

    public void LoadFromJson(string json, bool persistLocally = true, string source = "import")
    {
        Validate(json);
        if (AppServices.IsPlannerApp && AppServices.PlannerLocal is not null)
        {
            json = AppServices.PlannerLocal.StripDeletedFromPackageJson(json);
        }

        _currentJson = json;
        Editor = EditableRoutePackage.FromJson(json);
        Stats = ParseStats(json);

        if (AppServices.IsPlannerApp && AppServices.PlannerLocal is not null && Editor is not null)
        {
            AppServices.PlannerLocal.ApplyAfterPackageLoad(Editor);
            _currentJson = Editor.ToJson();
            Stats = ParseStats(_currentJson);
        }

        if (AppServices.IsInitialized)
        {
            var extracted = PlanerEmbeddedSoundsWorkspace.ExtractFromJsonToWorkspace(AppServices.Workspace, json);
            if (extracted > 0)
            {
                // Töne lokal für Bearbeitung / erneuten Export
            }
        }

        if (persistLocally && AppServices.IsInitialized)
        {
            AppServices.Workspace.SavePackage(GetPersistableJson(), source);
        }
    }

    public async Task LoadFromFileAsync(string filePath, bool persistLocally = true, string source = "file-import")
    {
        var json = await File.ReadAllTextAsync(filePath);
        LoadFromJson(json, persistLocally, source);
    }

    public async Task SaveToFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(_currentJson))
        {
            throw new InvalidOperationException("Kein Route-Paket geladen.");
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(filePath, _currentJson);
    }

    public string PrepareExportJson()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.FlushAllPendingEdits();
        }

        if (Editor is not null)
        {
            _currentJson = Editor.ToJson();
            Stats = ParseStats(_currentJson);
            if (AppServices.IsInitialized)
            {
                AppServices.Workspace.SavePackage(_currentJson, "export");
            }

            return _currentJson;
        }

        if (string.IsNullOrWhiteSpace(_currentJson))
        {
            throw new InvalidOperationException("Kein Route-Paket geladen.");
        }

        using var doc = JsonDocument.Parse(_currentJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("timestamp"))
                {
                    writer.WriteNumber("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            if (!doc.RootElement.TryGetProperty("timestamp", out _))
            {
                writer.WriteNumber("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }

            if (!doc.RootElement.TryGetProperty("exportType", out _))
            {
                writer.WriteString("exportType", "routes");
            }

            if (!doc.RootElement.TryGetProperty("version", out _))
            {
                writer.WriteString("version", "1.0");
            }

            if (!doc.RootElement.TryGetProperty("autoImport", out _))
            {
                writer.WriteBoolean("autoImport", true);
            }

            writer.WriteEndObject();
        }

        _currentJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Stats = ParseStats(_currentJson);
        return _currentJson;
    }

    public void ApplyEditorChanges(string source = "editor")
    {
        if (Editor is null)
        {
            return;
        }

        _currentJson = Editor.ToJson();
        Stats = ParseStats(_currentJson);

        if (AppServices.IsInitialized)
        {
            AppServices.Workspace.SavePackage(_currentJson, source);
        }
    }

    private string GetPersistableJson() => Editor is not null ? Editor.ToJson() : _currentJson!;

    private static void Validate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Ungültiges JSON: Wurzel muss ein Objekt sein.");
        }

        if (root.TryGetProperty("exportType", out var et))
        {
            var type = et.GetString();
            if (!string.Equals(type, "routes", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unbekannter exportType: {type}");
            }
        }
    }

    private static RoutePackageStats ParseStats(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var stats = new RoutePackageStats
        {
            Version = root.TryGetProperty("version", out var v) ? v.GetString() : null,
            ExportType = root.TryGetProperty("exportType", out var et) ? et.GetString() : null,
            Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t) ? t : null
        };

        var routeNames = new HashSet<string>(StringComparer.Ordinal);

        if (root.TryGetProperty("routes", out var routes) && routes.ValueKind == JsonValueKind.Array)
        {
            foreach (var route in routes.EnumerateArray())
            {
                if (route.ValueKind == JsonValueKind.String)
                {
                    var name = route.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        routeNames.Add(name);
                    }
                }
            }
        }

        var stopCount = 0;
        if (root.TryGetProperty("routeStops", out var routeStops) &&
            routeStops.ValueKind == JsonValueKind.Object)
        {
            foreach (var route in routeStops.EnumerateObject())
            {
                routeNames.Add(route.Name);
                if (route.Value.ValueKind == JsonValueKind.Array)
                {
                    stopCount += route.Value.GetArrayLength();
                }
            }
        }

        var driverCount = 0;
        if (root.TryGetProperty("employeeRoster", out var roster) &&
            roster.ValueKind == JsonValueKind.Array)
        {
            driverCount = roster.GetArrayLength();
        }

        var announcementCount = 0;
        if (root.TryGetProperty("managedAnnouncementTemplates", out var announcements) &&
            announcements.ValueKind == JsonValueKind.Array)
        {
            announcementCount = announcements.GetArrayLength();
        }

        stats.RouteCount = routeNames.Count;
        stats.StopCount = stopCount;
        stats.DriverCount = driverCount;
        stats.AnnouncementTemplateCount = announcementCount;
        return stats;
    }

    public static RoutePackageStats ParseStatsPublic(string json) => ParseStats(json);

    public string BuildLeitstelleStandJson()
    {
        if (Editor is null)
        {
            throw new InvalidOperationException("Kein Route-Paket geladen.");
        }

        return LeitstelleStandPackage.BuildJson(Editor);
    }

    public void TryMergeLeitstelleStandJson(string json)
    {
        if (Editor is null || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
        if (node is null)
        {
            return;
        }

        LeitstelleStandPackage.ApplyToEditor(Editor, node);
        ApplyEditorChanges("leitstelle-stand-merge");
    }
}
