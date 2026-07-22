using System.Text.Json;
using System.Text.Json.Serialization;
using SmartOepnv.Core;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Lokale Vorlagen für den Dialog „Einstellungen senden“ (Planer).
/// Datei: remote_device_settings_templates.json im Roaming-Ordner.
/// </summary>
public sealed class RemoteDeviceSettingsTemplateStore
{
    public const string FileName = "remote_device_settings_templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public RemoteDeviceSettingsTemplateStore(string? appSubfolder = null)
    {
        var folder = string.IsNullOrWhiteSpace(appSubfolder)
            ? AppServices.SettingsSubfolder
            : appSubfolder;
        _path = Path.Combine(AppPaths.GetRoamingDataDirectory(folder), FileName);
    }

    public string FilePath => _path;

    public RemoteDeviceSettingsTemplateDocument Load()
    {
        if (!File.Exists(_path))
        {
            return new RemoteDeviceSettingsTemplateDocument();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<RemoteDeviceSettingsTemplateDocument>(json, JsonOptions)
                   ?? new RemoteDeviceSettingsTemplateDocument();
        }
        catch
        {
            return new RemoteDeviceSettingsTemplateDocument();
        }
    }

    public void Save(RemoteDeviceSettingsTemplateDocument document)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public void Upsert(string name, RemoteDeviceSettingsPayload settings)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Vorlagenname fehlt.", nameof(name));
        }

        var doc = Load();
        var existing = doc.Templates.FirstOrDefault(t =>
            string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            doc.Templates.Add(new RemoteDeviceSettingsNamedTemplate
            {
                Name = trimmed,
                Settings = settings
            });
        }
        else
        {
            existing.Name = trimmed;
            existing.Settings = settings;
        }

        doc.LastUsedName = trimmed;
        Save(doc);
    }
}

public sealed class RemoteDeviceSettingsTemplateDocument
{
    public List<RemoteDeviceSettingsNamedTemplate> Templates { get; set; } = [];
    public string? LastUsedName { get; set; }
}

public sealed class RemoteDeviceSettingsNamedTemplate
{
    public string Name { get; set; } = "";
    public RemoteDeviceSettingsPayload Settings { get; set; } = new();
}
