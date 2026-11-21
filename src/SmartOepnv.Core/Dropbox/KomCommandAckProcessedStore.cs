using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Verhindert doppelte Anzeige derselben Fahrzeug-Bestätigung in der Leitstelle.</summary>
internal static class KomCommandAckProcessedStore
{
    private static readonly object Gate = new();

    public static bool WasProcessed(string normalizedPhone, long commandId)
    {
        if (commandId <= 0 || string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return false;
        }

        lock (Gate)
        {
            return TryReadStore().Contains(BuildKey(normalizedPhone, commandId));
        }
    }

    public static void MarkProcessed(string normalizedPhone, long commandId)
    {
        if (commandId <= 0 || string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return;
        }

        lock (Gate)
        {
            var store = TryReadStore();
            store.Add(BuildKey(normalizedPhone, commandId));
            WriteStore(store);
        }
    }

    private static string BuildKey(string normalizedPhone, long commandId) =>
        $"{normalizedPhone}_{commandId}";

    private static string StorePath =>
        Path.Combine(AppPaths.GetLocalDataDirectory(AppServices.SettingsSubfolder), "kom_command_ack_processed.json");

    private static HashSet<string> TryReadStore()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(StorePath);
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : parsed.ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static void WriteStore(HashSet<string> store)
    {
        try
        {
            var trimmed = store
                .OrderByDescending(k => k, StringComparer.Ordinal)
                .Take(500)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            var json = JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Bestätigungsanzeige ist optional
        }
    }
}
