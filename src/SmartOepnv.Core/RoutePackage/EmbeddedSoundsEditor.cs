using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Pflegt das GPSAnsagen-Feld <c>embeddedSounds</c> (fileName + soundData Base64).
/// </summary>
public static class EmbeddedSoundsEditor
{
    public const int MaxEmbeddedBytes = 5 * 1024 * 1024;
    private const int MaxBytes = MaxEmbeddedBytes;

    public static void UpsertFromFile(JsonObject root, string fileName, string localPath)
    {
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("Audiodatei nicht gefunden.", localPath);
        }

        var bytes = File.ReadAllBytes(localPath);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Audiodatei ist leer.");
        }

        if (bytes.Length > MaxBytes)
        {
            throw new InvalidOperationException($"Audiodatei zu groß (max. {MaxBytes / (1024 * 1024)} MB).");
        }

        UpsertBase64(root, fileName, Convert.ToBase64String(bytes), bytes.Length);
    }

    public static void UpsertBase64(JsonObject root, string fileName, string base64, int fileSize)
    {
        var arr = root["embeddedSounds"] as JsonArray ?? new JsonArray();
        JsonObject? existing = null;
        foreach (var node in arr.OfType<JsonObject>())
        {
            if (string.Equals(node["fileName"]?.GetValue<string>(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                existing = node;
                break;
            }
        }

        var entry = new JsonObject
        {
            ["fileName"] = fileName,
            ["soundData"] = base64,
            ["fileSize"] = fileSize,
            ["base64Length"] = base64.Length
        };

        if (existing is not null)
        {
            foreach (var prop in entry)
            {
                existing[prop.Key] = prop.Value?.DeepClone();
            }
        }
        else
        {
            arr.Add(entry);
        }

        root["embeddedSounds"] = arr;
    }

    public static IReadOnlyList<string> ListFileNames(JsonObject root) =>
        GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root).Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
