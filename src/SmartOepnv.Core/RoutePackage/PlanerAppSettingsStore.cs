using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettingsStore
{
    public const string SettingsFileName = "planer_app_settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _settingsPath;

    public PlanerAppSettingsStore(string appSubfolder)
    {
        _settingsPath = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), SettingsFileName);
    }

    /// <summary>Test: schreibt nicht in das echte Planer-Verzeichnis.</summary>
    internal static PlanerAppSettingsStore CreateForFile(string settingsPath) => new(settingsPath, explicitFile: true);

    private PlanerAppSettingsStore(string settingsPath, bool explicitFile)
    {
        _ = explicitFile;
        _settingsPath = settingsPath;
    }

    public string SettingsFilePath => _settingsPath;

    public PlanerAppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new PlanerAppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<PlanerAppSettings>(json, JsonOptions) ?? new PlanerAppSettings();
        }
        catch
        {
            return new PlanerAppSettings();
        }
    }

    public void Save(PlanerAppSettings settings)
    {
        var document = new
        {
            version = PlanerAppSettings.FileVersion,
            companyLogoFileName = settings.CompanyLogoFileName,
            companyLogos = settings.CompanyLogos,
            devicePassword = settings.DevicePassword,
            unlockPassword = settings.UnlockPassword,
            sondergongFileName = settings.SondergongFileName,
            specialBuildingBlockLines = settings.SpecialBuildingBlockLines,
            specialBuildingBlockLinesHidden = settings.SpecialBuildingBlockLinesHidden,
            suppressedStopAnnouncementSoundKeys = settings.SuppressedStopAnnouncementSoundKeys,
            lastRoutesExportPackageVersion = settings.LastRoutesExportPackageVersion,
            lastRoutesUpdatePackageVersion = settings.LastRoutesUpdatePackageVersion,
            lastVehicleSendUtcMs = settings.LastVehicleSendUtcMs,
            showSmartOepnvLogoInPdfs = settings.ShowSmartOepnvLogoInPdfs,
            useLightTheme = settings.UseLightTheme,
            ds009TextLength = settings.Ds009TextLength <= 16 ? 16 : 20,
            vdv454ParticipantRef = settings.Vdv454ParticipantRef,
            vdv454OperatorRef = settings.Vdv454OperatorRef,
            vdv454LocalCodespace = settings.Vdv454LocalCodespace,
            vdv454ExternalStopCodespace = settings.Vdv454ExternalStopCodespace,
            sevTftHost = settings.SevTftHost,
            sevTftPort = settings.SevTftPort is >= 1 and <= 65535 ? settings.SevTftPort : 8080,
            sevTftEnabled = settings.SevTftEnabled,
            sevTftAutoSend = settings.SevTftAutoSend
        };
        SafeDataFileStore.WriteAllText(_settingsPath, JsonSerializer.Serialize(document, JsonOptions));
    }
}
