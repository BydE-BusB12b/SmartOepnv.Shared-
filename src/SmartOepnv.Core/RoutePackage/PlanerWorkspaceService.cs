using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.Dropbox;
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

    private readonly string _localPath;
    private readonly PlannerLocalOverlayStore _overlayStore;
    private readonly VehicleDispositionStore _dispositionStore;
    private readonly DriverDispositionStore _driverDispositionStore;
    private readonly SevSignDraftStore _sevStore;
    private readonly DutyTemplateStore _dutyTemplateStore;

    public PlanerWorkspaceService(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _localPath = Path.Combine(workspaceDir, DropboxConstants.PlanerWorkspaceFileName);
        _overlayStore = new PlannerLocalOverlayStore(appSubfolder);
        _dispositionStore = new VehicleDispositionStore(appSubfolder);
        _driverDispositionStore = new DriverDispositionStore(appSubfolder);
        _sevStore = new SevSignDraftStore(appSubfolder);
        _dutyTemplateStore = new DutyTemplateStore(appSubfolder);
    }

    public string LocalFilePath => _localPath;

    public long GetLocalSavedAtUtcMs()
    {
        // Nur der letzte vollständige Workspace-Snapshot zählt – nicht routes_export.json allein.
        // Sonst blockiert ein neuer Routen-Timestamp den Import von Fahrzeugen, Disposition usw.
        return TryReadLocalDocument()?.SavedAtUtcMs ?? 0;
    }

    public PlanerWorkspaceDocument CaptureCurrent()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.FlushAllPendingEdits();
            if (AppServices.Routes.Editor is not null && AppServices.PlannerLocal is not null)
            {
                AppServices.PlannerLocal.PersistFromEditor(AppServices.Routes.Editor);
            }
        }

        string? routesJson = null;
        if (AppServices.Routes.HasPackage)
        {
            routesJson = AppServices.Routes.Editor?.ToJson() ?? AppServices.Routes.CurrentJson;
        }

        return new PlanerWorkspaceDocument
        {
            SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RoutesPackageJson = routesJson,
            PlannerOverlay = _overlayStore.LoadOrEmpty(),
            VehicleDispositionAssignments = _dispositionStore.Load().Select(a => a.Clone()).ToList(),
            DriverDispositionAssignments = _driverDispositionStore.Load().Select(a => a.Clone()).ToList(),
            SevSignDrafts = _sevStore.LoadAll().Select(CloneSevDraft).ToList(),
            DutyTemplates = _dutyTemplateStore.LoadAll().Select(CloneDutyTemplate).ToList(),
            PackageVersionSnapshots = CapturePackageVersionSnapshots()
        };
    }

    private static List<PlannerPackageVersionSnapshotData> CapturePackageVersionSnapshots()
    {
        if (AppServices.PlannerVersions is null)
        {
            return [];
        }

        return AppServices.PlannerVersions.ExportAllSnapshots().ToList();
    }

    public static int MergePackageVersionSnapshots(IReadOnlyList<PlannerPackageVersionSnapshotData> incoming)
    {
        if (AppServices.PlannerVersions is null || incoming.Count == 0)
        {
            return 0;
        }

        return AppServices.PlannerVersions.MergeFromWorkspace(incoming);
    }

    public void Apply(PlanerWorkspaceDocument document)
    {
        ApplyPlannerOverlay(document.PlannerOverlay);
        ApplyVehicleDisposition(document.VehicleDispositionAssignments);
        ApplyDriverDisposition(document.DriverDispositionAssignments ?? []);
        _sevStore.MergeIncoming(document.SevSignDrafts);
        _dutyTemplateStore.MergeIncoming(document.DutyTemplates ?? []);
        MergePackageVersionSnapshots(document.PackageVersionSnapshots);

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

        incoming.Employees = EmployeePlannerCredentialMerge.MergeLists(incoming.Employees, local.Employees);
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

    private PlanerWorkspaceDocument RefreshDocumentFromStores(PlanerWorkspaceDocument document) =>
        new()
        {
            SavedAtUtcMs = document.SavedAtUtcMs,
            RoutesPackageJson = document.RoutesPackageJson,
            PlannerOverlay = _overlayStore.LoadOrEmpty(),
            VehicleDispositionAssignments = _dispositionStore.Load().Select(a => a.Clone()).ToList(),
            DriverDispositionAssignments = _driverDispositionStore.Load().Select(a => a.Clone()).ToList(),
            SevSignDrafts = _sevStore.LoadAll().Select(CloneSevDraft).ToList(),
            DutyTemplates = _dutyTemplateStore.LoadAll().Select(CloneDutyTemplate).ToList(),
            PackageVersionSnapshots = CapturePackageVersionSnapshots()
        };

    public void WriteLocalCopy(PlanerWorkspaceDocument document)
    {
        SafeDataFileStore.WriteAllText(_localPath, Serialize(document), archivePrevious: false);
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
            return JsonNode.Parse(File.ReadAllText(_localPath)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    public PlanerWorkspaceDocument? TryReadLocalDocument() => Parse(ReadLocalJson());

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

    private static DutyTemplate CloneDutyTemplate(DutyTemplate template) => template.Clone();
}
