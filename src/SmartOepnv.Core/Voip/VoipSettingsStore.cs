using System.IO;
using System.Text.Json;

namespace SmartOepnv.Core.Voip;

public sealed class VoipSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public VoipSettingsStore(string appSubfolder = "Leitstelle") =>
        _path = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "voip_settings.json");

    public VoipSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new VoipSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<VoipSettings>(File.ReadAllText(_path)) ?? new VoipSettings();
        }
        catch
        {
            return new VoipSettings();
        }
    }

    public void Save(VoipSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
