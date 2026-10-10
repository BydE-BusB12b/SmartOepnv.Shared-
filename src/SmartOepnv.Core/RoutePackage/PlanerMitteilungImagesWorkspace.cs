using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Bildanhänge für Mitteilungs-PDFs (workspace/mitteilung-images).</summary>
public static class PlanerMitteilungImagesWorkspace
{
    public const string FolderName = "mitteilung-images";
    private const string IndexFileName = "images.json";

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

    public static IReadOnlyList<MitteilungImageEntry> GetImages(string appSubfolder)
    {
        var index = LoadIndex(appSubfolder);
        return index.Images
            .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.FileName))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? TryGetImagePath(string appSubfolder, string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return null;
        }

        var entry = GetImages(appSubfolder)
            .FirstOrDefault(s => string.Equals(s.Id, imageId.Trim(), StringComparison.Ordinal));
        if (entry is null || string.IsNullOrWhiteSpace(entry.FileName))
        {
            return null;
        }

        var path = Path.Combine(GetDirectory(appSubfolder), entry.FileName.Trim());
        return File.Exists(path) ? path : null;
    }

    public static MitteilungImageEntry? TryGetEntry(string appSubfolder, string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return null;
        }

        return GetImages(appSubfolder)
            .FirstOrDefault(s => string.Equals(s.Id, imageId.Trim(), StringComparison.Ordinal));
    }

    public static MitteilungImageEntry AddFromFile(
        string appSubfolder,
        string sourcePath,
        string? displayName = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Bilddatei nicht gefunden.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Nur Bilddateien (PNG, JPG, JPEG, WEBP) können eingefügt werden.");
        }

        var id = Guid.NewGuid().ToString("N");
        var fileName = $"image_{id}{extension.ToLowerInvariant()}";
        var target = Path.Combine(GetDirectory(appSubfolder), fileName);
        File.Copy(sourcePath, target, overwrite: true);

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Bild";
        }

        var entry = new MitteilungImageEntry
        {
            Id = id,
            Name = name,
            FileName = fileName
        };

        var index = LoadIndex(appSubfolder);
        index.Images.Add(entry);
        SaveIndex(appSubfolder, index);
        return entry;
    }

    public static bool TryDelete(string appSubfolder, string imageId)
    {
        var index = LoadIndex(appSubfolder);
        var entry = index.Images.FirstOrDefault(s =>
            string.Equals(s.Id, imageId.Trim(), StringComparison.Ordinal));
        if (entry is null)
        {
            return false;
        }

        index.Images.Remove(entry);
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

    private static ImageIndex LoadIndex(string appSubfolder)
    {
        var path = Path.Combine(GetDirectory(appSubfolder), IndexFileName);
        if (!File.Exists(path))
        {
            return new ImageIndex();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ImageIndex>(json) ?? new ImageIndex();
        }
        catch
        {
            return new ImageIndex();
        }
    }

    private static void SaveIndex(string appSubfolder, ImageIndex index)
    {
        var path = Path.Combine(GetDirectory(appSubfolder), IndexFileName);
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private sealed class ImageIndex
    {
        [JsonPropertyName("images")]
        public List<MitteilungImageEntry> Images { get; set; } = [];
    }
}

public sealed class MitteilungImageEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
}
