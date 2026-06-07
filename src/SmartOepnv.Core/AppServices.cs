using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Sev;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Core;

public static class AppServices
{
    private static DropboxSettingsStore? _dropboxSettingsStore;
    private static DropboxApiClient? _dropbox;
    private static VehicleTrackingService? _vehicleTracking;
    private static LocalWorkspaceStore? _workspace;
    private static bool _initialized;

    public static DropboxSettingsStore DropboxSettingsStore =>
        _dropboxSettingsStore ?? throw new InvalidOperationException("AppServices.Initialize wurde nicht aufgerufen.");

    public static DropboxApiClient Dropbox =>
        _dropbox ?? throw new InvalidOperationException("AppServices.Initialize wurde nicht aufgerufen.");

    public static RoutePackageService Routes { get; } = new();

    public static LocalWorkspaceStore Workspace =>
        _workspace ?? throw new InvalidOperationException("AppServices.Initialize wurde nicht aufgerufen.");

    public static VehicleTrackingService VehicleTracking =>
        _vehicleTracking ?? throw new InvalidOperationException("AppServices.Initialize wurde nicht aufgerufen.");

    public static string SettingsSubfolder { get; private set; } = "Planer";

    public static bool IsPlannerApp =>
        string.Equals(SettingsSubfolder, "Planer", StringComparison.OrdinalIgnoreCase);

    public static bool IsInitialized => _initialized;

    public static PlannerLocalOverlayService? PlannerLocal => _plannerLocal;

    public static PlannerPackageVersionStore? PlannerVersions => _plannerVersions;

    public static DeviceRegistrationDropboxService? DeviceRegistration => _deviceRegistration;

    public static SevSignDraftStore? SevSignDrafts => _sevSignDrafts;

    private static PlannerLocalOverlayService? _plannerLocal;
    private static PlannerPackageVersionStore? _plannerVersions;
    private static DeviceRegistrationDropboxService? _deviceRegistration;
    private static SevSignDraftStore? _sevSignDrafts;

    private static readonly List<Action> _flushBeforeExport = [];

    /// <summary>Registriert z. B. „Ansagen speichern“ vor Dropbox-Export.</summary>
    public static void RegisterFlushBeforeExport(Action flush) => _flushBeforeExport.Add(flush);

    public static void FlushAllPendingEdits()
    {
        foreach (var flush in _flushBeforeExport.ToArray())
        {
            try
            {
                flush();
            }
            catch
            {
                // Einzelbereich darf Export nicht blockieren
            }
        }
    }

    public static void Initialize(string settingsSubfolder)
    {
        SettingsSubfolder = settingsSubfolder;
        _dropboxSettingsStore = new DropboxSettingsStore(settingsSubfolder);
        _dropbox = new DropboxApiClient(_dropboxSettingsStore);
        _vehicleTracking = new VehicleTrackingService(_dropbox);
        _workspace = new LocalWorkspaceStore(settingsSubfolder);
        if (IsPlannerApp)
        {
            _plannerLocal = new PlannerLocalOverlayService(settingsSubfolder);
            _plannerVersions = new PlannerPackageVersionStore(settingsSubfolder);
            _deviceRegistration = new DeviceRegistrationDropboxService();
            _sevSignDrafts = new SevSignDraftStore(settingsSubfolder);
        }
        _initialized = true;
    }
}
