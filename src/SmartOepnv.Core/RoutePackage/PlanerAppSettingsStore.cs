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
            unlockPassword = settings.UnlockPassword
        };
        SafeDataFileStore.WriteAllText(_settingsPath, JsonSerializer.Serialize(document, JsonOptions));
    }
}
