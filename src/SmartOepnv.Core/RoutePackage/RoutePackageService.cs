using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

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

    /// <summary>Erhöht sich bei Load/Apply – für verzögertes Refresh in UI-Bereichen.</summary>
    public int EditorDataRevision { get; private set; }

    public void LoadFromJson(string json, bool persistLocally = true, string source = "import")
    {
        Validate(json);
        if (AppServices.IsPlannerApp && AppServices.PlannerLocal is not null)
        {
            json = AppServices.PlannerLocal.StripDeletedFromPackageJson(json);
        }

        var rosterSnapshot = RoutePackageRosterPreserve.CaptureFromEditor(Editor);
        var incomingHasRoster = RoutePackageRosterPreserve.JsonContainsRosterData(json);

        _currentJson = json;
        Editor = EditableRoutePackage.FromJson(json);
        Stats = ParseStats(json);
        EditorDataRevision++;

        if (!incomingHasRoster)
        {
            RoutePackageRosterPreserve.RestoreIfIncomingEmpty(Editor!, rosterSnapshot);
        }

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
            AppServices.Workspace.SavePackage(GetPersistableJson(), source, archivePrevious: true);
        }
    }

    public async Task LoadFromFileAsync(string filePath, bool persistLocally = true, string source = "file-import")
    {
        var json = await File.ReadAllTextAsync(filePath);
        LoadFromJson(json, persistLocally, source);
    }

    public async Task SaveToFileAsync(string filePath)
    {
        var json = GetFullPackageJson(rebuildEmbeddedMedia: false);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(filePath, json);
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
            if (AppServices.IsPlannerApp)
            {
                _currentJson = StripPlannerSecretsFromExportJson(_currentJson);
            }

            Stats = ParseStats(_currentJson);
            if (AppServices.IsInitialized)
            {
                AppServices.Workspace.SavePackage(_currentJson, "export", archivePrevious: true);
            }

            return RoutePackageVersionStamp.Stamp(_currentJson, RoutePackageVersionStamp.Kind.Export);
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
                if (prop.NameEquals("timestamp") ||
                    prop.NameEquals("packageVersion") ||
                    prop.NameEquals("packageKind"))
                {
                    continue;
                }

                prop.WriteTo(writer);
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
        return RoutePackageVersionStamp.Stamp(_currentJson, RoutePackageVersionStamp.Kind.Export);
    }

    /// <summary>Teil-Export für Fahrzeuge (ausgewählte Routen per Update oder Senden).</summary>
    public string PrepareVehicleTransferJson(
        IReadOnlyList<string> selectedRouteNames,
        bool pruneOthersOnDevice,
        bool liteVehicleUpdate = false)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.FlushAllPendingEdits();
        }

        if (Editor is null)
        {
            throw new InvalidOperationException("Kein Route-Paket geladen.");
        }

        if (selectedRouteNames.Count == 0)
        {
            throw new InvalidOperationException("Mindestens eine Route auswählen.");
        }

        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        var json = GpsAnsagenRouteExportSync.BuildVehicleTransferJson(
            Editor,
            selectedRouteNames,
            pruneOthersOnDevice,
            workspace,
            liteVehicleUpdate);

        if (AppServices.IsPlannerApp)
        {
            json = StripPlannerSecretsFromExportJson(json);
        }

        return RoutePackageVersionStamp.Stamp(
            json,
            liteVehicleUpdate
                ? RoutePackageVersionStamp.Kind.Update
                : RoutePackageVersionStamp.Kind.Export);
    }

    /// <summary>Alle Routen ohne Audio – für routes_update.json (Merge auf dem Gerät).</summary>
    public string PrepareFullLiteVehicleUpdateJson()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.FlushAllPendingEdits();
        }

        if (Editor is null)
        {
            throw new InvalidOperationException("Kein Route-Paket geladen.");
        }

        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        var json = GpsAnsagenRouteExportSync.BuildFullLiteVehicleUpdateJson(Editor, workspace);

        if (AppServices.IsPlannerApp)
        {
            json = StripPlannerSecretsFromExportJson(json);
        }

        return RoutePackageVersionStamp.Stamp(json, RoutePackageVersionStamp.Kind.Update);
    }

    public void ApplyEditorChanges(
        string source = "editor",
        bool archivePreviousSave = false,
        bool rebuildEmbeddedMedia = true)
    {
        if (Editor is null)
        {
            return;
        }

        // Lokale Edits: kein Versions-Backup (nur Export/Import archiviert).
        // Audio-Rebuild nur wenn der Aufrufer es anfordert (Ansagen mit neuem Ton).
        var body = Editor.ToJson(
            indented: false,
            rebuildEmbeddedMedia: rebuildEmbeddedMedia,
            includeHeavyMedia: false);
        _currentJson = body;
        Stats = ParseStats(body);
        EditorDataRevision++;

        if (AppServices.IsInitialized)
        {
            var updateHeavy = rebuildEmbeddedMedia || !File.Exists(AppServices.Workspace.HeavyMediaSidecarPath);
            string? heavy = null;
            if (updateHeavy)
            {
                heavy = Editor.TryGetHeavyMediaSidecarJson();
            }

            AppServices.Workspace.SavePackageBody(
                body,
                source,
                archivePreviousSave,
                heavyMediaJson: heavy,
                updateHeavyMediaSidecar: updateHeavy && !string.IsNullOrWhiteSpace(heavy));
        }
    }

    /// <summary>
    /// Schreibt den aktuellen PackageRoot erneut auf Disk, ohne SyncToRoot/Editor-Rebuild
    /// (z. B. nach erneutem Setzen von routePathDrafts).
    /// </summary>
    public void PersistPackageBodyOnly(string source = "persist")
    {
        if (Editor is null || !AppServices.IsInitialized)
        {
            return;
        }

        var body = Editor.SerializeCurrentRootWithoutSync(includeHeavyMedia: false);
        _currentJson = body;
        AppServices.Workspace.SavePackageBody(
            body,
            source,
            archivePrevious: false,
            heavyMediaJson: null,
            updateHeavyMediaSidecar: false);
    }

    /// <summary>Vollständiges Paket inkl. Audio (für Merge/Datei-Export); baut bei Bedarf aus Cache/Sidecar.</summary>
    public string GetFullPackageJson(bool rebuildEmbeddedMedia = false)
    {
        if (Editor is not null)
        {
            return Editor.ToJson(indented: false, rebuildEmbeddedMedia: rebuildEmbeddedMedia, includeHeavyMedia: true);
        }

        if (!string.IsNullOrWhiteSpace(_currentJson))
        {
            return AppServices.IsInitialized
                ? AppServices.Workspace.TryLoadPackageJson() ?? _currentJson
                : _currentJson;
        }

        throw new InvalidOperationException("Kein Route-Paket geladen.");
    }

    /// <summary>
    /// Legt bei fehlendem Paket ein leeres Route-Paket an (neuer Betrieb / leerer Workspace).
    /// </summary>
    /// <returns><c>true</c>, wenn danach ein Editor verfügbar ist.</returns>
    public bool EnsureEmptyPackageIfNeeded(string source = "empty-package")
    {
        if (Editor is not null && HasPackage)
        {
            return true;
        }

        var json = BuildEmptyPackageJson();
        LoadFromJson(json, persistLocally: true, source: source);
        return Editor is not null;
    }

    public static string BuildEmptyPackageJson()
    {
        var root = new JsonObject
        {
            ["version"] = "1.0",
            ["exportType"] = "routes",
            ["autoImport"] = true,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["routes"] = new JsonArray(),
            ["routeStops"] = new JsonObject(),
            ["stopTemplates"] = new JsonArray(),
            ["outsideDisplays"] = new JsonArray(),
            ["employeeRoster"] = new JsonArray(),
            ["registeredVehicles"] = new JsonArray(),
            ["messageTemplates"] = new JsonArray(),
            ["mailTemplates"] = new JsonArray(),
            ["announcementTemplates"] = new JsonArray(),
            ["dateBasedHints"] = new JsonArray()
        };
        return root.ToJsonString();
    }

    private string GetPersistableJson() =>
        Editor is not null
            ? Editor.ToJson(indented: false, rebuildEmbeddedMedia: true, includeHeavyMedia: true)
            : _currentJson!;

    private static string StripPlannerSecretsFromExportJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject root)
        {
            return json;
        }

        EmployeeRosterEditor.StripPlannerSecretsFromRoot(root);
        EmbedDriverDutyDispatches(root);
        return root.ToJsonString();
    }

    private static void EmbedDriverDutyDispatches(JsonObject root)
    {
        if (!AppServices.IsInitialized || !AppServices.IsPlannerApp)
        {
            return;
        }

        var assignments = AppServices.PlannerLocal?.LoadDriverDisposition() ?? [];
        var employees = AppServices.Routes.Editor?.Employees.ToList() ?? [];
        var templates = AppServices.DutyTemplates?.LoadAll() ?? [];
        DriverDutyDispatchExporter.EmbedIntoRoot(root, assignments, employees, templates);
    }

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

        // Route-Namen weiterhin aus routeStops (Fahrten), Haltestellen-Zahl aber aus der
        // Stammliste managedStopTemplates – nicht Summe aller Routen-Einträge.
        if (root.TryGetProperty("routeStops", out var routeStops) &&
            routeStops.ValueKind == JsonValueKind.Object)
        {
            foreach (var route in routeStops.EnumerateObject())
            {
                routeNames.Add(route.Name);
            }
        }

        var stopCount = 0;
        if (root.TryGetProperty("managedStopTemplates", out var stopTemplates) &&
            stopTemplates.ValueKind == JsonValueKind.Array)
        {
            stopCount = stopTemplates.GetArrayLength();
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
        ApplyEditorChanges("leitstelle-stand-merge", rebuildEmbeddedMedia: false);
    }

    /// <summary>
    /// Übernimmt ein Lite-Routenpaket (ohne Audio) in den Editor – Fernroute &amp; Karte.
    /// </summary>
    /// <param name="trackLeitstelleRoutes">
    /// true = Timestamp von <c>leitstelle_routes.json</c>; false = <c>routes_update.json</c>.
    /// </param>
    public bool TryMergeLiteRouteUpdateJson(
        string json,
        out string message,
        bool trackLeitstelleRoutes = false)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "Lite-Update ist leer.";
            return false;
        }

        if (!LiteRouteUpdateMerge.IsLiteVehicleUpdate(json))
        {
            message = "Datei ist kein Lite-Routenpaket (ohne Audio).";
            return false;
        }

        var fileLabel = trackLeitstelleRoutes
            ? DropboxConstants.LeitstelleRoutesFileName
            : DropboxConstants.RouteUpdateFileName;
        var updateTimestamp = LocalWorkspaceStore.ExtractPackageTimestamp(json);
        if (AppServices.IsInitialized)
        {
            var lastMerged = trackLeitstelleRoutes
                ? AppServices.Workspace.GetLastMergedLeitstelleRoutesTimestamp()
                : AppServices.Workspace.GetLastMergedRouteUpdateTimestamp();
            if (updateTimestamp > 0 &&
                updateTimestamp <= lastMerged &&
                !LiteRouteUpdateMerge.ContainsRoutesMissingFromEditor(json, Editor) &&
                !LiteRouteUpdateMerge.HasStaleRoutePathGeometry(json, Editor))
            {
                message = $"{fileLabel} bereits übernommen.";
                return false;
            }
        }

        try
        {
            var source = trackLeitstelleRoutes ? "leitstelle-routes-merge" : "routes-update-merge";
            if (!HasPackage || Editor is null || string.IsNullOrWhiteSpace(_currentJson))
            {
                LoadFromJson(json, persistLocally: true, source: source);
            }
            else
            {
                var baseJson = GetFullPackageJson(rebuildEmbeddedMedia: false);
                var merged = LiteRouteUpdateMerge.MergeIntoPackageJson(baseJson, json);
                LoadFromJson(merged, persistLocally: true, source: source);
            }

            if (AppServices.IsInitialized && updateTimestamp > 0)
            {
                if (trackLeitstelleRoutes)
                {
                    AppServices.Workspace.SaveLastMergedLeitstelleRoutesTimestamp(updateTimestamp);
                }
                else
                {
                    AppServices.Workspace.SaveLastMergedRouteUpdateTimestamp(updateTimestamp);
                }
            }

            message =
                $"{fileLabel} übernommen ({Stats.RouteCount} Routen, {Stats.StopCount} Haltestellen).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Lite-Merge fehlgeschlagen: {ex.Message}";
            return false;
        }
    }
}
