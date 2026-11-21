namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ticket-Logo für Museums-Druckvorlage (workspace/ticketing).</summary>
public static class PlanerTicketLogoWorkspace
{
    public const string FolderName = "ticketing";
    public const string DefaultFileName = "ticket_logo.png";

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

    public static string? TryGetPath(string appSubfolder, string? assetKey)
    {
        if (string.IsNullOrWhiteSpace(assetKey)) return null;
        var name = Path.GetFileName(assetKey.Trim());
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.Combine(GetDirectory(appSubfolder), name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Kopiert eine Bilddatei nach workspace/ticketing und gibt den Dateinamen zurück.</summary>
    public static string SaveFromFile(string appSubfolder, string sourcePath, string? preferredFileName = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Logo-Datei nicht gefunden.", sourcePath);

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            extension = ".png";

        var fileName = string.IsNullOrWhiteSpace(preferredFileName)
            ? DefaultFileName
            : Path.GetFileName(preferredFileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = DefaultFileName;

        // Endung an Quelldatei anpassen, wenn Preferred nur „ticket_logo“ o.ä. ist
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            fileName += extension;
        else if (!string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase) &&
                 AllowedExtensions.Contains(extension))
            fileName = Path.ChangeExtension(fileName, extension);

        var target = Path.Combine(GetDirectory(appSubfolder), fileName);
        File.Copy(sourcePath, target, overwrite: true);
        return fileName;
    }

    public static void SaveBytes(string appSubfolder, string fileName, byte[] bytes)
    {
        if (bytes.Length == 0) return;
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName.Trim());
        var target = Path.Combine(GetDirectory(appSubfolder), name);
        File.WriteAllBytes(target, bytes);
    }

    public static byte[]? TryReadBytes(string appSubfolder, string? assetKey)
    {
        var path = TryGetPath(appSubfolder, assetKey);
        return path is null ? null : File.ReadAllBytes(path);
    }
}
