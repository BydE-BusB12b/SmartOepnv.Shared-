using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Eine markierte Pixelgrafik, die später wieder auf eine Anzeige gesetzt werden kann.
/// </summary>
public sealed record MobitecSavedGraphic(
    string Name,
    int Width,
    int Height,
    IReadOnlyList<(int X, int Y)> Points);

/// <summary>
/// Ablage der Grafiken unter dem aktiven Betrieb, Ordner <c>grafiken</c>.
/// </summary>
public static class MobitecGraphicLibrary
{
    public const int MaxNameLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Folder =>
        Path.Combine(AppPaths.GetRoamingDataDirectory("Planer"), "grafiken");

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength || trimmed.EndsWith('.') || trimmed.EndsWith(' '))
        {
            return false;
        }

        return trimmed.All(ch => !char.IsControl(ch) && !InvalidFileNameChars.Contains(ch));
    }

    public static bool Exists(string folder, string name) =>
        IsValidName(name) && File.Exists(PathFor(folder, name.Trim()));

    public static void Save(
        string folder,
        string name,
        int width,
        int height,
        IReadOnlyList<(int X, int Y)> points)
    {
        var trimmed = name.Trim();
        if (!IsValidName(trimmed))
        {
            throw new ArgumentException("Der Grafikname ist nicht als Dateiname verwendbar.", nameof(name));
        }

        if (width is < 1 or > MobitecBitmapFrame.FrontWidth ||
            height is < 1 or > MobitecBitmapFrame.Height ||
            points.Count == 0 ||
            points.Any(point => point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height))
        {
            throw new ArgumentException("Die Grafik liegt außerhalb einer Anzeige.");
        }

        Directory.CreateDirectory(folder);
        var dto = new FileDto
        {
            Name = trimmed,
            Width = width,
            Height = height,
            Points = points
                .Select(point => new PointDto { X = point.X, Y = point.Y })
                .ToList()
        };
        var path = PathFor(folder, trimmed);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(temp, path, overwrite: true);
        MobitecGraphicPng.Write(ImagePath(folder, trimmed), width, height, points);
    }

    /// <summary>Legt die PNG an, falls eine ältere Grafik nur als JSON vorliegt.</summary>
    public static string EnsureImage(string folder, MobitecSavedGraphic graphic)
    {
        var path = ImagePath(folder, graphic.Name);
        if (!File.Exists(path))
        {
            MobitecGraphicPng.Write(path, graphic.Width, graphic.Height, graphic.Points);
        }

        return path;
    }

    public static string ImagePath(string folder, string name) =>
        Path.Combine(folder, name.Trim() + ".png");

    public static IReadOnlyList<MobitecSavedGraphic> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var graphics = new List<MobitecSavedGraphic>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (TryRead(path) is { } graphic)
            {
                graphics.Add(graphic);
            }
        }

        return graphics
            .OrderBy(graphic => graphic.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool Delete(string folder, string name)
    {
        if (!IsValidName(name))
        {
            return false;
        }

        var path = PathFor(folder, name.Trim());
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        var image = ImagePath(folder, name.Trim());
        if (File.Exists(image))
        {
            File.Delete(image);
        }

        return true;
    }

    private static MobitecSavedGraphic? TryRead(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null ||
                !IsValidName(dto.Name) ||
                dto.Width is < 1 or > MobitecBitmapFrame.FrontWidth ||
                dto.Height is < 1 or > MobitecBitmapFrame.Height ||
                dto.Points is not { Count: > 0 } ||
                dto.Points.Any(point => point.X < 0 || point.Y < 0 || point.X >= dto.Width || point.Y >= dto.Height))
            {
                return null;
            }

            return new MobitecSavedGraphic(
                dto.Name.Trim(),
                dto.Width,
                dto.Height,
                dto.Points.Select(point => (point.X, point.Y)).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string PathFor(string folder, string name) =>
        Path.Combine(folder, name.Trim() + ".json");

    private static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private sealed class FileDto
    {
        public string Name { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public List<PointDto> Points { get; set; } = [];
    }

    private sealed class PointDto
    {
        public int X { get; set; }

        public int Y { get; set; }
    }
}
