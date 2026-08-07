using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.Mitteilungen;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _appSubfolder;
    private readonly string _localPath;
    private readonly string _localRoutesPath;
    private readonly PlannerLocalOverlayStore _overlayStore;
    private readonly VehicleDispositionStore _dispositionStore;
    private readonly DriverDispositionStore _driverDispositionStore;
    private readonly SevSignDraftStore _sevStore;
    private readonly MitteilungDraftStore _mitteilungStore;
    private readonly DutyTemplateStore _dutyTemplateStore;

    public PlanerWorkspaceService(string appSubfolder)
    {
        _appSubfolder = appSubfolder;
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _localPath = Path.Combine(workspaceDir, DropboxConstants.PlanerWorkspaceFileName);
        _localRoutesPath = Path.Combine(workspaceDir, DropboxConstants.PlanerRoutesFileName);
        _overlayStore = new PlannerLocalOverlayStore(appSubfolder);
        _dispositionStore = new VehicleDispositionStore(appSubfolder);
        _driverDispositionStore = new DriverDispositionStore(appSubfolder);
        _sevStore = new SevSignDraftStore(appSubfolder);
        _mitteilungStore = new MitteilungDraftStore(appSubfolder);
        _dutyTemplateStore = new DutyTemplateStore(appSubfolder);
    }

    public string LocalFilePath => _localPath;

    public long GetLocalSavedAtUtcMs()
    {
        // Nur planer_workspace.json – routes_export.meta würde sonst einen neueren
        // lokalen Zeitstempel vortäuschen und einen frischeren Dropbox-Stand blockieren.
        return TryPeekLocalMeta()?.SavedAtUtcMs ?? 0;
    }

    public long GetLocalSyncGeneration() =>
        TryPeekLocalMeta()?.SyncGeneration
        ?? PlanerWorkspaceDropboxSyncStamp.TryLoad(_localPath)?.SyncGeneration
        ?? 0;

    public PlanerWorkspaceDocument CaptureCurrent() => CaptureCurrent(null);

    public PlanerWorkspaceDocument CaptureCurrent(PlanerWorkspaceCaptureRequest? request)
    {
        if (AppServices.IsInitialized && request?.SkipFlush != true)
        {
            AppServices.FlushAllPendingEditsBestEffort();
        }

        if (AppServices.IsInitialized &&
            AppServices.Routes.Editor is not null &&
            AppServices.PlannerLocal is not null)
        {
            AppServices.PlannerLocal.PersistFromEditor(AppServices.Routes.Editor);
        }

        return new PlanerWorkspaceDocument
        {
            SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SyncGeneration = PlanerWorkspaceDropboxSyncStamp.NextSyncGeneration(
                _localPath,
                GetLocalSyncGeneration()),
            RoutesPackageJson = ResolveRoutesPackageJson(request),
            PlannerOverlay = _overlayStore.LoadOrEmpty(),
            VehicleDispositionAssignments = _dispositionStore.Load().Select(a => a.Clone()).ToList(),
            DriverDispositionAssignments = _driverDispositionStore.Load().Select(a => a.Clone()).ToList(),
            SevSignDrafts = _sevStore.LoadAll().Select(CloneSevDraft).ToList(),
            MitteilungDrafts = _mitteilungStore.LoadAll().Select(CloneMitteilungDraft).ToList(),
            DutyTemplates = _dutyTemplateStore.LoadAll().Select(CloneDutyTemplate).ToList(),
            PackageVersionSnapshots = CapturePackageVersionSnapshots(request?.ReuseSnapshotPackageJsonFrom),
            AnnouncementRawSounds = AppServices.IsInitialized
                ? PlanerAnnouncementRawSoundsWorkspace.CaptureForSync(
                    AppServices.Workspace,
                    request?.ReuseAnnouncementRawSoundsFrom)
                : []
        };
    }

    private static string? ResolveRoutesPackageJson(PlanerWorkspaceCaptureRequest? request)
    {
        if (!AppServices.Routes.HasPackage)
        {
            return null;
        }

        if (request?.PreferCachedRoutesJson == true &&
            !string.IsNullOrWhiteSpace(AppServices.Routes.CurrentJson))
        {
            return AppServices.Routes.CurrentJson;
        }

        return AppServices.Routes.Editor?.ToJson(indented: false) ?? AppServices.Routes.CurrentJson;
    }

    private static List<PlannerPackageVersionSnapshotData> CapturePackageVersionSnapshots(
        IReadOnlyList<PlannerPackageVersionSnapshotData>? reuseFrom)
    {
        if (AppServices.PlannerVersions is null)
        {
            return [];
        }

        // Nur Metadaten in den Workspace – PackageJson liegt in workspace/versions/
        // und wird beim Dropbox-Upload bei Bedarf einzeln geladen.
        _ = reuseFrom;
        return AppServices.PlannerVersions.ExportSnapshotsMetadataOnly().ToList();
    }

    public static int MergePackageVersionSnapshots(IReadOnlyList<PlannerPackageVersionSnapshotData> incoming)
    {
        if (AppServices.PlannerVersions is null || incoming.Count == 0)
        {
            return 0;
        }

        return AppServices.PlannerVersions.MergeFromWorkspace(incoming);
    }

    public void Apply(PlanerWorkspaceDocument document, bool authoritative = true)
    {
        if (authoritative)
        {
            ApplyPlannerOverlayReplace(document.PlannerOverlay);
            ApplyVehicleDispositionReplace(document.VehicleDispositionAssignments);
            ApplyDriverDispositionReplace(document.DriverDispositionAssignments ?? []);
            _sevStore.ReplaceAll(document.SevSignDrafts);
            _mitteilungStore.ReplaceAll(document.MitteilungDrafts ?? []);
            _dutyTemplateStore.ReplaceAll(document.DutyTemplates ?? []);
        }
        else
        {
            ApplyPlannerOverlay(document.PlannerOverlay);
            ApplyVehicleDisposition(document.VehicleDispositionAssignments);
            ApplyDriverDisposition(document.DriverDispositionAssignments ?? []);
            _sevStore.MergeIncoming(document.SevSignDrafts);
            _mitteilungStore.MergeIncoming(document.MitteilungDrafts ?? []);
            _dutyTemplateStore.MergeIncoming(document.DutyTemplates ?? []);
        }

        MergePackageVersionSnapshots(document.PackageVersionSnapshots);

        if (AppServices.IsInitialized)
        {
            PlanerAnnouncementRawSoundsWorkspace.ApplyFromSync(
                AppServices.Workspace,
                document.AnnouncementRawSounds,
                replaceExtraneous: authoritative);
        }

        if (!string.IsNullOrWhiteSpace(document.RoutesPackageJson))
        {
            AppServices.Routes.LoadFromJson(
                document.RoutesPackageJson,
                persistLocally: true,
                source: "planer-workspace-sync");
        }
        else if (AppServices.Routes.Editor is not null && AppServices.PlannerLocal is not null)
        {
            AppServices.PlannerLocal.ApplyAfterPackageLoad(AppServices.Routes.Editor);
        }

        WriteLocalCopy(RefreshDocumentFromStores(document));
    }

    /// <summary>Lädt den lokalen planer_workspace.json vollständig (alle Bereiche).</summary>
    public bool TryApplyLocalDocument()
    {
        if (File.Exists(_localPath) && new FileInfo(_localPath).Length > 32 * 1024 * 1024)
        {
            return TryMigrateBloatedLocalWorkspace();
        }

        var document = TryReadLocalDocument();
        if (document is null)
        {
            return false;
        }

        Apply(document, authoritative: true);
        return true;
    }

    /// <summary>
    /// Altbestand: planer_workspace.json mit eingebetteten Versions-Snapshots (oft &gt;1 GB).
    /// Nutzt die bereits ausgelagerten Stores/Versionen/Routen-Cache und schreibt danach schlank.
    /// </summary>
    private bool TryMigrateBloatedLocalWorkspace()
    {
        var meta = TryPeekLocalMeta();
        var routesJson = TryReadLocalRoutesSidecarOrCache();
        var document = new PlanerWorkspaceDocument
        {
            SavedAtUtcMs = meta?.SavedAtUtcMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SyncGeneration = meta?.SyncGeneration ?? 0,
            RoutesStoredExternally = true,
            RoutesPackageJson = routesJson,
            PlannerOverlay = _overlayStore.LoadOrEmpty(),
            VehicleDispositionAssignments = _dispositionStore.Load().Select(a => a.Clone()).ToList(),
            DriverDispositionAssignments = _driverDispositionStore.Load().Select(a => a.Clone()).ToList(),
            SevSignDrafts = _sevStore.LoadAll().Select(CloneSevDraft).ToList(),
            MitteilungDrafts = _mitteilungStore.LoadAll().Select(CloneMitteilungDraft).ToList(),
            DutyTemplates = _dutyTemplateStore.LoadAll().Select(CloneDutyTemplate).ToList(),
            PackageVersionSnapshots = CapturePackageVersionSnapshots(null),
            AnnouncementRawSounds = AppServices.IsInitialized
                ? PlanerAnnouncementRawSoundsWorkspace.CaptureForSync(AppServices.Workspace)
                : []
        };

        Apply(document, authoritative: true);
        return !string.IsNullOrWhiteSpace(routesJson) || AppServices.Routes.HasPackage;
    }

    private string? TryReadLocalRoutesSidecarOrCache()
    {
        try
        {
            // routes_cache.json ist die Quelle nach Editor-/Navidaten-Speichern.
            // planer_routes.json wird oft nur beim Beenden geschrieben und kann älter sein.
            if (AppServices.IsInitialized)
            {
                var fromWorkspace = AppServices.Workspace.TryLoadPackageJson();
                if (!string.IsNullOrWhiteSpace(fromWorkspace))
                {
                    return fromWorkspace;
                }
            }

            if (File.Exists(_localRoutesPath))
            {
                var sidecar = File.ReadAllText(_localRoutesPath);
                if (!string.IsNullOrWhiteSpace(sidecar))
                {
                    return sidecar;
                }
            }
        }
        catch
        {
            // ignore
        }

        return AppServices.IsInitialized ? AppServices.Routes.CurrentJson : null;
    }

    private void ApplyPlannerOverlayReplace(PlannerLocalOverlayData incoming)
    {
        var local = _overlayStore.LoadOrEmpty();
        if (!incoming.HasContent && local.HasContent)
        {
            return;
        }

        if (!local.HasContent)
        {
            _overlayStore.Save(incoming);
            return;
        }

        // Autoritativer Import – aber leere Fahrzeug-/Personal-Listen dürfen lokale Einträge nicht löschen.
        incoming.Employees = EmployeePlannerCredentialMerge.MergeLists(incoming.Employees, local.Employees);
        incoming.Vehicles = PlannerLocalOverlayService.MergeVehiclesPreferLocal(incoming.Vehicles, local.Vehicles);
        incoming.PhoneRedirects = PlannerLocalOverlayService.MergePhoneRedirectsPreferLocal(
            incoming.PhoneRedirects,
            local.PhoneRedirects);
        incoming.DeletedEmployeePersonnel = MergeUnique(
            incoming.DeletedEmployeePersonnel,
            local.DeletedEmployeePersonnel);
        incoming.DeletedEmployeePhones = MergeUnique(
            incoming.DeletedEmployeePhones,
            local.DeletedEmployeePhones);
        incoming.DeletedVehiclePhoneKeys = MergeUnique(
            incoming.DeletedVehiclePhoneKeys,
            local.DeletedVehiclePhoneKeys);
        incoming.DeletedRouteKeys = MergeUnique(incoming.DeletedRouteKeys, local.DeletedRouteKeys);
        _overlayStore.Save(incoming);
    }

    private void ApplyVehicleDispositionReplace(IReadOnlyList<VehicleDispositionAssignment> incoming)
    {
        _dispositionStore.Save(incoming);
    }

    private void ApplyDriverDispositionReplace(IReadOnlyList<DriverDispositionAssignment> incoming)
    {
        _driverDispositionStore.Save(incoming);
    }

    private void ApplyPlannerOverlay(PlannerLocalOverlayData incoming)
    {
        var local = _overlayStore.LoadOrEmpty();
        if (!incoming.HasContent)
        {
            if (local.HasContent)
            {
                return;
            }

            _overlayStore.Save(incoming);
            return;
        }

        incoming.Employees = EmployeePlannerCredentialMerge.MergeLists(local.Employees, incoming.Employees);
        incoming.Vehicles = PlannerLocalOverlayService.MergeVehiclesPreferLocal(local.Vehicles, incoming.Vehicles);
        incoming.PhoneRedirects = PlannerLocalOverlayService.MergePhoneRedirectsPreferLocal(
            local.PhoneRedirects,
            incoming.PhoneRedirects);
        incoming.DeletedEmployeePersonnel = MergeUnique(
            local.DeletedEmployeePersonnel,
            incoming.DeletedEmployeePersonnel);
        incoming.DeletedEmployeePhones = MergeUnique(
            local.DeletedEmployeePhones,
            incoming.DeletedEmployeePhones);
        incoming.DeletedVehiclePhoneKeys = MergeUnique(
            local.DeletedVehiclePhoneKeys,
            incoming.DeletedVehiclePhoneKeys);
        _overlayStore.Save(incoming);
    }

    private static List<string> MergeUnique(IEnumerable<string> a, IEnumerable<string> b)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in a.Concat(b))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value);
            }
        }

        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private void ApplyVehicleDisposition(IReadOnlyList<VehicleDispositionAssignment> incoming)
    {
        var local = _dispositionStore.Load();
        if (incoming.Count == 0)
        {
            if (local.Count > 0)
            {
                return;
            }

            return;
        }

        if (local.Count == 0)
        {
            _dispositionStore.Save(incoming);
            return;
        }

        _dispositionStore.Save(VehicleDispositionMerge.Merge(local, incoming));
    }

    private void ApplyDriverDisposition(IReadOnlyList<DriverDispositionAssignment> incoming)
    {
        var local = _driverDispositionStore.Load();
        if (incoming.Count == 0)
        {
            if (local.Count > 0)
            {
                return;
            }

            return;
        }

        if (local.Count == 0)
        {
            _driverDispositionStore.Save(incoming);
            return;
        }

        _driverDispositionStore.Save(DriverDispositionMerge.Merge(local, incoming));
    }

    private PlanerWorkspaceDocument RefreshDocumentFromStores(PlanerWorkspaceDocument document)
    {
        var refreshed = new PlanerWorkspaceDocument
        {
            SavedAtUtcMs = document.SavedAtUtcMs,
            SyncGeneration = document.SyncGeneration,
            RoutesPackageJson = document.RoutesPackageJson,
            PlannerOverlay = _overlayStore.LoadOrEmpty(),
            VehicleDispositionAssignments = _dispositionStore.Load().Select(a => a.Clone()).ToList(),
            DriverDispositionAssignments = _driverDispositionStore.Load().Select(a => a.Clone()).ToList(),
            SevSignDrafts = _sevStore.LoadAll().Select(CloneSevDraft).ToList(),
            MitteilungDrafts = _mitteilungStore.LoadAll().Select(CloneMitteilungDraft).ToList(),
            DutyTemplates = _dutyTemplateStore.LoadAll().Select(CloneDutyTemplate).ToList(),
            PackageVersionSnapshots = CapturePackageVersionSnapshots(document.PackageVersionSnapshots),
            AnnouncementRawSounds = AppServices.IsInitialized
                ? PlanerAnnouncementRawSoundsWorkspace.CaptureForSync(
                    AppServices.Workspace,
                    document.AnnouncementRawSounds)
                : document.AnnouncementRawSounds
        };
        return refreshed;
    }

    public void WriteLocalCopy(PlanerWorkspaceDocument document)
    {
        // Wie Dropbox-Slim: keine eingebetteten Routen-/Versions-JSONs (sonst GB-große Datei).
        WriteLocalRoutesSidecar(document.RoutesPackageJson);
        var slim = ToLocalPersistDocument(document);
        var tempPath = _localPath + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, slim, CompactJsonOptions);
            }

            File.Move(tempPath, _localPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignore
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Lokale Persistenz ohne Routen-Paket und ohne Snapshot-JSON
    /// (liegen in <c>planer_routes.json</c> bzw. <c>workspace/versions/</c>).
    /// </summary>
    public static PlanerWorkspaceDocument ToLocalPersistDocument(PlanerWorkspaceDocument full) =>
        ToDropboxSlimDocument(full);

    /// <summary>
    /// Dropbox-Upload ohne Routen-Paket und ohne Snapshot-JSON (liegen in separaten Dateien).
    /// </summary>
    public static PlanerWorkspaceDocument ToDropboxSlimDocument(PlanerWorkspaceDocument full) =>
        new()
        {
            Version = PlanerWorkspaceDocument.FileVersion,
            DocumentType = PlanerWorkspaceDocument.Kind,
            SavedAtUtcMs = full.SavedAtUtcMs,
            SyncGeneration = full.SyncGeneration,
            RoutesStoredExternally = !string.IsNullOrWhiteSpace(full.RoutesPackageJson) ||
                                     full.RoutesStoredExternally,
            RoutesPackageJson = null,
            PlannerOverlay = full.PlannerOverlay,
            VehicleDispositionAssignments = full.VehicleDispositionAssignments,
            DriverDispositionAssignments = full.DriverDispositionAssignments,
            SevSignDrafts = full.SevSignDrafts,
            MitteilungDrafts = full.MitteilungDrafts,
            DutyTemplates = full.DutyTemplates,
            PackageVersionSnapshots = full.PackageVersionSnapshots
                .Select(snapshot => new PlannerPackageVersionSnapshotData
                {
                    Id = snapshot.Id,
                    Label = snapshot.Label,
                    SavedAtUtc = snapshot.SavedAtUtc,
                    ByteSize = snapshot.ByteSize,
                    RouteCount = snapshot.RouteCount,
                    PackageTimestampMs = snapshot.PackageTimestampMs,
                    PackageJson = string.Empty
                })
                .ToList(),
            AnnouncementRawSounds = full.AnnouncementRawSounds
        };

    public string WriteDropboxSlimCopyToTemp(PlanerWorkspaceDocument full)
    {
        var slim = ToDropboxSlimDocument(full);
        var tempPath = _localPath + ".dropbox-upload.tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, slim, CompactJsonOptions);
        }

        return tempPath;
    }

    public static async Task EnrichFromDropboxSidecarsAsync(
        PlanerWorkspaceDocument document,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (document.RoutesStoredExternally && string.IsNullOrWhiteSpace(document.RoutesPackageJson))
        {
            document.RoutesPackageJson = await PlanerRoutesDropboxSync.TryDownloadRoutesJsonAsync(ct)
                .ConfigureAwait(false);
        }

        await PlanerVersionSnapshotsDropboxSync
            .ImportMissingFilesAsync(document.PackageVersionSnapshots, progress, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Aktualisiert Routen-Paket in planer_workspace.json (ohne vollständigen Workspace-Flush).
    /// Verhindert, dass beim Neustart ein älterer Dropbox-Stand lokale Haltestellen überschreibt.
    /// </summary>
    public void PatchLocalRoutesPackage(string routesJson)
    {
        if (string.IsNullOrWhiteSpace(routesJson))
        {
            return;
        }

        WriteLocalRoutesSidecar(routesJson);

        // Workspace-Meta aktualisieren ohne die (ggf. riesige) Datei komplett neu zu parsen.
        var meta = TryPeekLocalMeta();
        var root = ReadOrCreateWorkspaceRootSlimSafe();
        root["savedAtUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (meta?.SyncGeneration is > 0)
        {
            root["syncGeneration"] = meta.Value.SyncGeneration;
        }

        root["routesStoredExternally"] = true;
        root.Remove("routesPackageJson");
        if (!root.ContainsKey("version"))
        {
            root["version"] = PlanerWorkspaceDocument.FileVersion;
        }

        if (!root.ContainsKey("documentType"))
        {
            root["documentType"] = PlanerWorkspaceDocument.Kind;
        }

        SafeDataFileStore.WriteAllText(
            _localPath,
            root.ToJsonString(CompactJsonOptions),
            archivePrevious: false);
    }

    /// <summary>
    /// Aktualisiert nur Fahrerdispo-Felder in planer_workspace.json (ohne Routen-Paket neu zu serialisieren).
    /// Für häufiges Speichern zu schwer – nur beim Verlassen der Ansicht / App-Ende aufrufen.
    /// </summary>
    public void PatchLocalDriverDisposition(IReadOnlyList<DriverDispositionAssignment> assignments)
    {
        if (assignments.Count == 0 && !File.Exists(_localPath))
        {
            return;
        }

        if (File.Exists(_localPath) && new FileInfo(_localPath).Length > 32 * 1024 * 1024)
        {
            _driverDispositionStore.Save(assignments);
            TryMigrateBloatedLocalWorkspace();
            return;
        }

        var root = ReadOrCreateWorkspaceRoot();
        root["savedAtUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["driverDispositionAssignments"] = JsonSerializer.SerializeToNode(assignments, JsonOptions);
        SafeDataFileStore.WriteAllText(
            _localPath,
            root.ToJsonString(JsonOptions),
            archivePrevious: false);
    }

    private JsonObject ReadOrCreateWorkspaceRoot()
    {
        return ReadOrCreateWorkspaceRootSlimSafe();
    }

    /// <summary>
    /// Liest die Workspace-Wurzel. Bei aufgeblähten Alt-Dateien (&gt;32 MB) nur Meta + Stores,
    /// damit Patches nicht 1+ GB JSON laden.
    /// </summary>
    private JsonObject ReadOrCreateWorkspaceRootSlimSafe()
    {
        if (!File.Exists(_localPath))
        {
            return new JsonObject
            {
                ["version"] = PlanerWorkspaceDocument.FileVersion,
                ["documentType"] = PlanerWorkspaceDocument.Kind
            };
        }

        try
        {
            var length = new FileInfo(_localPath).Length;
            if (length > 32 * 1024 * 1024)
            {
                var meta = TryPeekLocalMeta();
                return new JsonObject
                {
                    ["version"] = PlanerWorkspaceDocument.FileVersion,
                    ["documentType"] = PlanerWorkspaceDocument.Kind,
                    ["savedAtUtcMs"] = meta?.SavedAtUtcMs ?? 0,
                    ["syncGeneration"] = meta?.SyncGeneration ?? 0,
                    ["routesStoredExternally"] = true
                };
            }

            return JsonNode.Parse(File.ReadAllText(_localPath)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    public PlanerWorkspaceDocument? TryReadLocalDocument()
    {
        var document = Parse(ReadLocalJson());
        if (document is null)
        {
            return null;
        }

        EnrichFromLocalSidecars(document);
        return document;
    }

    private void EnrichFromLocalSidecars(PlanerWorkspaceDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.RoutesPackageJson))
        {
            // Altbestand: eingebettete Routen einmalig in Sidecar auslagern.
            WriteLocalRoutesSidecar(document.RoutesPackageJson);
            return;
        }

        var routes = TryReadLocalRoutesSidecarOrCache();
        if (string.IsNullOrWhiteSpace(routes))
        {
            return;
        }

        document.RoutesPackageJson = routes;
        document.RoutesStoredExternally = true;
    }

    private void WriteLocalRoutesSidecar(string? routesPackageJson)
    {
        if (string.IsNullOrWhiteSpace(routesPackageJson))
        {
            return;
        }

        var tempPath = _localRoutesPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, routesPackageJson);
            File.Move(tempPath, _localRoutesPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignore
                }
            }

            throw;
        }
    }

    public readonly record struct LocalWorkspaceMeta(long SavedAtUtcMs, long SyncGeneration);

    /// <summary>
    /// Liest nur Top-Level-Metadaten aus dem Dateianfang (ohne GB-große Felder zu laden).
    /// </summary>
    public LocalWorkspaceMeta? TryPeekLocalMeta()
    {
        if (!File.Exists(_localPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_localPath);
            var buffer = new byte[16 * 1024];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return null;
            }

            var head = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            long savedAt = 0;
            long syncGeneration = 0;
            var foundSavedAt = TryReadJsonInt64Property(head, "savedAtUtcMs", out savedAt);
            var foundSyncGeneration = TryReadJsonInt64Property(head, "syncGeneration", out syncGeneration);
            if (!foundSavedAt && !foundSyncGeneration)
            {
                return null;
            }

            return new LocalWorkspaceMeta(savedAt, syncGeneration);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadJsonInt64Property(string jsonHead, string propertyName, out long value)
    {
        value = 0;
        var needle = $"\"{propertyName}\"";
        var idx = jsonHead.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        idx = jsonHead.IndexOf(':', idx + needle.Length);
        if (idx < 0)
        {
            return false;
        }

        idx++;
        while (idx < jsonHead.Length && char.IsWhiteSpace(jsonHead[idx]))
        {
            idx++;
        }

        var start = idx;
        if (start < jsonHead.Length && jsonHead[start] == '-')
        {
            idx++;
        }

        while (idx < jsonHead.Length && char.IsDigit(jsonHead[idx]))
        {
            idx++;
        }

        return start < idx && long.TryParse(jsonHead.AsSpan(start, idx - start), out value);
    }

    public static PlanerWorkspaceDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PlanerWorkspaceDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(PlanerWorkspaceDocument document) =>
        JsonSerializer.Serialize(document, CompactJsonOptions);

    private string? ReadLocalJson()
    {
        if (!File.Exists(_localPath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(_localPath);
        }
        catch
        {
            return null;
        }
    }

    private static SevSignDraft CloneSevDraft(SevSignDraft draft) => new()
    {
        Id = draft.Id,
        Name = draft.Name,
        UpdatedAtUtcMs = draft.UpdatedAtUtcMs,
        Line = draft.Line,
        Destination = draft.Destination,
        Stops = draft.Stops.ToList(),
        Operators = draft.Operators.ToList(),
        SourceRoute = draft.SourceRoute,
        ImportRouteReverse = draft.ImportRouteReverse
    };

    private static MitteilungDraft CloneMitteilungDraft(MitteilungDraft draft) => new()
    {
        Id = draft.Id,
        Name = draft.Name,
        UpdatedAtUtcMs = draft.UpdatedAtUtcMs,
        Title = draft.Title,
        Body = draft.Body,
        ValidFrom = draft.ValidFrom,
        ValidTo = draft.ValidTo,
        UntilRevoked = draft.UntilRevoked,
        ShowSmartOepnvLogo = draft.ShowSmartOepnvLogo,
        CompanyLogoId = draft.CompanyLogoId,
        SignerNameAndDate = draft.SignerNameAndDate,
        SignatureId = draft.SignatureId
    };

    private static DutyTemplate CloneDutyTemplate(DutyTemplate template) => template.Clone();
}
