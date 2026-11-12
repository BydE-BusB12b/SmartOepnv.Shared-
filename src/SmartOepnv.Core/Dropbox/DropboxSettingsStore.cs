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
        if (!OperatingSystem.IsWindows())
        {
            return ReadJson(PlainPath()) ?? new DropboxSettings();
        }

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
        if (!OperatingSystem.IsWindows())
        {
            var path = PlainPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return;
        }

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_settingsPath, protectedBytes);
    }

    private string PlainPath() =>
        Path.Combine(Path.GetDirectoryName(_settingsPath)!, "dropbox.settings.json");

    private static DropboxSettings? ReadJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DropboxSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
