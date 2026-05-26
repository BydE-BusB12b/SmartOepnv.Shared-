using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SmartOepnv.Core.Dropbox;

public sealed class DropboxSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private readonly string _settingsPath;

    public DropboxSettingsStore(string appSubfolder = "Planer")
    {
        var dir = AppPaths.GetRoamingDataDirectory(appSubfolder);
        _settingsPath = Path.Combine(dir, "dropbox.settings.dat");
    }

    public DropboxSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new DropboxSettings();
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_settingsPath);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize<DropboxSettings>(json, JsonOptions) ?? new DropboxSettings();
        }
        catch
        {
            return new DropboxSettings();
        }
    }

    public void Save(DropboxSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_settingsPath, protectedBytes);
    }
}
