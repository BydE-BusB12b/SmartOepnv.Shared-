namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Persistiert die konfigurierbare Sondergong-Tondatei unter den Planer-App-Einstellungen.
/// </summary>
public static class PlanerSondergongSoundStore
{
    public const string SoundsFolderName = "ansagen_sounds";

    public static string GetSoundsDirectory(string settingsSubfolder)
    {
        var dir = Path.Combine(settingsSubfolder, SoundsFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string? TryGetLocalFilePath(string settingsSubfolder, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(GetSoundsDirectory(settingsSubfolder), fileName.Trim());
        return File.Exists(path) ? path : null;
    }

    /// <summary>Kopiert eine Quelldatei nach ansagen_sounds und liefert den Dateinamen.</summary>
    public static string SaveFromFile(string settingsSubfolder, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Sondergong-Tondatei nicht gefunden.", sourcePath);
        }

        var originalName = Path.GetFileName(sourcePath);
        var safeName = SanitizeFileName(originalName);
        var target = Path.Combine(GetSoundsDirectory(settingsSubfolder), safeName);
        File.Copy(sourcePath, target, overwrite: true);
        return safeName;
    }

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "Sondergong.wav" : trimmed;
    }
}
