using System.IO;
using System.Text.Json;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Liest <c>DEVICE_REGISTER:name|phone</c> aus Dropbox-ZBL-Dateien (an Hauptgerät-Nummern gesendet)
/// und trägt Fahrzeuge im Planer-Editor ein.
/// </summary>
public sealed class DeviceRegistrationDropboxService
{
    private const string Prefix = "DEVICE_REGISTER:";

    private readonly string _processedPath;
    private HashSet<string> _processed = new(StringComparer.Ordinal);

    public DeviceRegistrationDropboxService()
    {
        var dir = Path.Combine(AppPaths.GetRoamingDataDirectory("Planer"), "workspace");
        Directory.CreateDirectory(dir);
        _processedPath = Path.Combine(dir, "processed_device_register.json");
        LoadProcessed();
    }

    public async Task<DeviceRegistrationResult> TryProcessPendingAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsPlannerApp || !AppServices.Dropbox.Settings.IsConnected)
        {
            return DeviceRegistrationResult.None;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return DeviceRegistrationResult.None;
        }

        var added = new List<string>();
        var files = await AppServices.Dropbox.ListZblMessageFilesAsync(ct).ConfigureAwait(false);

        foreach (var file in files)
        {
            try
            {
                var content = await AppServices.Dropbox.DownloadNamedFileAsync(file, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var message = ReadMessage(root);
                if (!message.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var timestamp = root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t)
                    ? t
                    : 0L;
                var key = $"{file}|{timestamp}";
                if (_processed.Contains(key))
                {
                    continue;
                }

                if (!TryParseRegistration(message, out var name, out var phone))
                {
                    _processed.Add(key);
                    continue;
                }

                var phoneKey = RegisteredVehiclesEditor.NormalizePhoneKey(phone);
                var exists = editor.RegisteredVehicles.Any(v =>
                    RegisteredVehiclesEditor.NormalizePhoneKey(v.PhoneNumber) == phoneKey);
                if (exists)
                {
                    _processed.Add(key);
                    continue;
                }

                editor.RegisteredVehicles.Add(new RegisteredVehicleItem
                {
                    Name = name,
                    PhoneNumber = phone,
                    LoadedPhoneNumber = phone
                });
                _processed.Add(key);
                added.Add($"{name} ({phone})");
            }
            catch
            {
                // Einzeldatei ignorieren
            }
        }

        if (added.Count > 0)
        {
            AppServices.PlannerLocal?.PersistFromEditor(editor);
            AppServices.Routes.ApplyEditorChanges("device-register-dropbox");
        }

        SaveProcessed();
        return added.Count == 0
            ? DeviceRegistrationResult.None
            : new DeviceRegistrationResult(added);
    }

    private static string ReadMessage(JsonElement root)
    {
        if (root.TryGetProperty("message", out var messageProp))
        {
            var m = messageProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(m))
            {
                return m;
            }
        }

        if (root.TryGetProperty("text", out var textProp))
        {
            return textProp.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryParseRegistration(string payload, out string name, out string phone)
    {
        name = string.Empty;
        phone = string.Empty;
        var data = payload.Substring(Prefix.Length).Trim();
        var parts = data.Split('|', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        name = parts[0].Trim();
        phone = parts[1].Trim();
        return name.Length > 0 && phone.Length > 0;
    }

    private void LoadProcessed()
    {
        if (!File.Exists(_processedPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_processedPath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is not null)
            {
                _processed = list.ToHashSet(StringComparer.Ordinal);
            }
        }
        catch
        {
            _processed = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void SaveProcessed()
    {
        try
        {
            File.WriteAllText(_processedPath, JsonSerializer.Serialize(_processed.OrderBy(x => x).ToList()));
        }
        catch
        {
            // optional
        }
    }
}

public sealed class DeviceRegistrationResult
{
    public static DeviceRegistrationResult None { get; } = new([]);

    public DeviceRegistrationResult(IReadOnlyList<string> addedVehicles)
    {
        AddedVehicles = addedVehicles;
    }

    public IReadOnlyList<string> AddedVehicles { get; }

    public bool AnyAdded => AddedVehicles.Count > 0;
}
