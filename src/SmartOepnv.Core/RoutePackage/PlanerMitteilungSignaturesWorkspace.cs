using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Gespeicherte Unterschriften für Mitteilungs-PDFs (workspace/mitteilung-signatures).</summary>
public static class PlanerMitteilungSignaturesWorkspace
{
    public const string FolderName = "mitteilung-signatures";
    private const string IndexFileName = "signatures.json";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    public static string GetDirectory(string appSubfolder)
    {
        var dir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace", FolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static IReadOnlyList<MitteilungSignatureEntry> GetSignatures(string appSubfolder)
    {
        var index = LoadIndex(appSubfolder);
        return index.Signatures
            .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.FileName))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? TryGetSignaturePath(string appSubfolder, string? signatureId)
    {
        if (string.IsNullOrWhiteSpace(signatureId))
        {
            return null;
        }

        var entry = GetSignatures(appSubfolder)
            .FirstOrDefault(s => string.Equals(s.Id, signatureId.Trim(), StringComparison.Ordinal));
        if (entry is null || string.IsNullOrWhiteSpace(entry.FileName))
        {
            return null;
        }

        var path = Path.Combine(GetDirectory(appSubfolder), entry.FileName.Trim());
        return File.Exists(path) ? path : null;
    }

    public static MitteilungSignatureEntry AddFromFile(
        string appSubfolder,
        string sourcePath,
        string? displayName = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Unterschriftsdatei nicht gefunden.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            extension = ".png";
        }

        var id = Guid.NewGuid().ToString("N");
        var fileName = $"signature_{id}{extension.ToLowerInvariant()}";
        var target = Path.Combine(GetDirectory(appSubfolder), fileName);
        File.Copy(sourcePath, target, overwrite: true);

        return RegisterEntry(appSubfolder, id, fileName, displayName, Path.GetFileNameWithoutExtension(sourcePath));
    }

    public static MitteilungSignatureEntry AddFromPngBytes(
        string appSubfolder,
        byte[] pngBytes,
        string? displayName = null)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            throw new ArgumentException("Unterschriftsbild ist leer.", nameof(pngBytes));
        }

        var id = Guid.NewGuid().ToString("N");
        var fileName = $"signature_{id}.png";
        var target = Path.Combine(GetDirectory(appSubfolder), fileName);
        File.WriteAllBytes(target, pngBytes);
        return RegisterEntry(appSubfolder, id, fileName, displayName, "Unterschrift");
    }

    private static MitteilungSignatureEntry RegisterEntry(
        string appSubfolder,
        string id,
        string fileName,
        string? displayName,
        string fallbackName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? fallbackName : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Unterschrift";
        }

        var entry = new MitteilungSignatureEntry
        {
            Id = id,
            Name = name,
            FileName = fileName
        };

        var index = LoadIndex(appSubfolder);
        index.Signatures.Add(entry);
        SaveIndex(appSubfolder, index);
        return entry;
    }

    public static bool TryDelete(string appSubfolder, string signatureId)
    {
        var index = LoadIndex(appSubfolder);
        var entry = index.Signatures.FirstOrDefault(s =>
            string.Equals(s.Id, signatureId.Trim(), StringComparison.Ordinal));
        if (entry is null)
        {
            return false;
        }

        index.Signatures.Remove(entry);
        SaveIndex(appSubfolder, index);

        if (!string.IsNullOrWhiteSpace(entry.FileName))
        {
            var path = Path.Combine(GetDirectory(appSubfolder), entry.FileName);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Index ist maßgeblich
                }
            }
        }

        return true;
    }

    private static SignatureIndex LoadIndex(string appSubfolder)
    {
        var path = Path.Combine(GetDirectory(appSubfolder), IndexFileName);
        if (!File.Exists(path))
        {
            return new SignatureIndex();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SignatureIndex>(json) ?? new SignatureIndex();
        }
        catch
        {
            return new SignatureIndex();
        }
    }

    private static void SaveIndex(string appSubfolder, SignatureIndex index)
    {
        var path = Path.Combine(GetDirectory(appSubfolder), IndexFileName);
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private sealed class SignatureIndex
    {
        [JsonPropertyName("signatures")]
        public List<MitteilungSignatureEntry> Signatures { get; set; } = [];
    }
}

public sealed class MitteilungSignatureEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
}
