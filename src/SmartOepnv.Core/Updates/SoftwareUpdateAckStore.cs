using System.Text.Json;

namespace SmartOepnv.Core.Updates;

/// <summary>Merkt sich, für welche verfügbare Version der Hinweis bereits bestätigt wurde.</summary>
public static class SoftwareUpdateAckStore
{
    private static readonly object Gate = new();

    public static bool WasAcknowledged(string appKey, string availableVersion)
    {
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(availableVersion))
        {
            return false;
        }

        lock (Gate)
        {
            return string.Equals(
                TryReadStore()[appKey.Trim()],
                availableVersion.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void MarkAcknowledged(string appKey, string availableVersion)
    {
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(availableVersion))
        {
            return;
        }

        lock (Gate)
        {
            var store = TryReadStore();
            store[appKey.Trim()] = availableVersion.Trim();
            WriteStore(store);
        }
    }

    private static string StorePath =>
        Path.Combine(AppPaths.GetLocalDataDirectory(AppServices.SettingsSubfolder), "software_update_ack.json");

    private static Dictionary<string, string> TryReadStore()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(StorePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteStore(Dictionary<string, string> store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Hinweis ist optional – Speichern darf Start nicht stören
        }
    }
}
