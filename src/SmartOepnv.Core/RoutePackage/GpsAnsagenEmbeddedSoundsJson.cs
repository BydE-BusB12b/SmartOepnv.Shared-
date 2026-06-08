using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// <c>embeddedSounds</c> als Objekt (Schlüssel = Dateiname) – wie RouteDistributionManager auf dem Handy.
/// </summary>
public static class GpsAnsagenEmbeddedSoundsJson
{
    public static void SyncToRoot(
        JsonObject root,
        IEnumerable<string> requiredFileNames,
        LocalWorkspaceStore? workspace = null)
    {
        var names = requiredFileNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            if (ReadAllEntries(root).Count == 0)
            {
                root.Remove("embeddedSounds");
            }
            return;
        }

        var existing = ReadAllEntries(root);
        var output = new JsonObject();

        foreach (var fileName in names)
        {
            if (TryResolveEntry(fileName, existing, root, workspace, out var entry))
            {
                output[fileName] = entry;
                continue;
            }

            // Bereits eingebettete Töne behalten (z. B. nach „Tondatei wählen“, bevor Workspace-Kopie da ist)
            if (existing.TryGetValue(fileName, out var cached) && !string.IsNullOrWhiteSpace(cached.Base64))
            {
                output[fileName] = new JsonObject
                {
                    ["data"] = cached.Base64,
                    ["size"] = cached.Size > 0 ? cached.Size : EstimateSize(cached.Base64)
                };
            }
        }

        root["embeddedSounds"] = output;
    }

    public static IReadOnlyDictionary<string, (string Base64, int Size)> ReadAllEntries(JsonObject root)
    {
        var result = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);

        if (root["embeddedSounds"] is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                if (prop.Value is not JsonObject soundObj)
                {
                    continue;
                }

                var data = JsonNodeReading.GetString(soundObj["data"], JsonNodeReading.GetString(soundObj["soundData"]));
                if (string.IsNullOrWhiteSpace(data))
                {
                    data = JsonNodeReading.GetString(soundObj["audioData"]);
                }
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                var size = JsonNodeReading.GetInt32(soundObj["size"], JsonNodeReading.GetInt32(soundObj["fileSize"]));
                result[prop.Key] = (data, size);
            }

            return result;
        }

        if (root["embeddedSounds"] is not JsonArray arr)
        {
            return result;
        }

        foreach (var node in arr.OfType<JsonObject>())
        {
            var fileName = JsonNodeReading.GetString(node["fileName"]);
            var data = JsonNodeReading.GetString(node["soundData"], JsonNodeReading.GetString(node["data"]));
            if (string.IsNullOrWhiteSpace(data))
            {
                data = JsonNodeReading.GetString(node["audioData"]);
            }
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            var size = JsonNodeReading.GetInt32(node["fileSize"], JsonNodeReading.GetInt32(node["size"]));
            result[fileName] = (data, size);
        }

        return result;
    }

    private static bool TryResolveEntry(
        string fileName,
        IReadOnlyDictionary<string, (string Base64, int Size)> existing,
        JsonObject root,
        LocalWorkspaceStore? workspace,
        out JsonObject entry)
    {
        entry = new JsonObject();

        if (existing.TryGetValue(fileName, out var cached))
        {
            entry["data"] = cached.Base64;
            entry["size"] = cached.Size > 0 ? cached.Size : EstimateSize(cached.Base64);
            return true;
        }

        if (workspace is not null)
        {
            var path = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, fileName)
                ?? TryFindWorkspaceAudio(workspace, fileName);
            if (path is not null && File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0)
                {
                    entry["data"] = Convert.ToBase64String(bytes);
                    entry["size"] = bytes.Length;
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TryFindWorkspaceAudio(LocalWorkspaceStore workspace, string fileName)
    {
        try
        {
            var dir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(workspace);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(stem))
            {
                return null;
            }

            foreach (var path in Directory.EnumerateFiles(dir))
            {
                if (!EmbeddedSoundCatalog.IsAudioFile(path))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            var digits = new string(stem.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                var code = digits[^4..];
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    if (EmbeddedSoundCatalog.IsAudioFile(path) &&
                        Path.GetFileName(path).StartsWith(code, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }
        }
        catch
        {
            // Workspace optional
        }

        return null;
    }

    private static int EstimateSize(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64).Length;
        }
        catch
        {
            return 0;
        }
    }
}
