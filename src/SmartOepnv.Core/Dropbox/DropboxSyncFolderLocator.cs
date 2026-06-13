namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Findet den lokal synchronisierten Dropbox-Ordner unter Windows
/// (z. B. C:\Users\…\Dropbox\App\Smart ÖPNV).
/// </summary>
public static class DropboxSyncFolderLocator
{
    public static string? TryResolveSmartOepnvFolder(string? configuredApiFolderPath = null)
    {
        var relative = NormalizeRelativeFolder(configuredApiFolderPath);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();

        foreach (var dropboxRoot in GetDropboxRoots(home))
        {
            candidates.Add(CombineDropboxRelative(dropboxRoot, relative));
            if (relative.StartsWith("App/", StringComparison.OrdinalIgnoreCase))
            {
                var appsVariant = "Apps/" + relative["App/".Length..];
                candidates.Add(CombineDropboxRelative(dropboxRoot, appsVariant));
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> FindZeitwirtschaftJsonFiles(string? configuredApiFolderPath = null)
    {
        var folder = TryResolveSmartOepnvFolder(configuredApiFolderPath);
        if (folder is null)
        {
            return Array.Empty<string>();
        }

        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "zeitwirtschaft_*.json")
            : Array.Empty<string>();
    }

    public static string? FindMaengelkarteJsonFile(string? configuredApiFolderPath = null)
    {
        var folder = TryResolveSmartOepnvFolder(configuredApiFolderPath);
        if (folder is null)
        {
            return null;
        }

        var path = Path.Combine(folder, DropboxConstants.MaengelkarteFileName);
        return File.Exists(path) ? path : null;
    }

    private static string NormalizeRelativeFolder(string? configuredApiFolderPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredApiFolderPath)
            ? DropboxConstants.DefaultFolderPath
            : configuredApiFolderPath.Trim();
        return path.Trim('/').Replace('\\', '/');
    }

    private static IEnumerable<string> GetDropboxRoots(string home)
    {
        yield return Path.Combine(home, "Dropbox");
        yield return Path.Combine(home, "Dropbox (Personal)");
        yield return Path.Combine(home, "Dropbox (Business)");
        yield return Path.Combine(home, "Dropbox (Team)");
    }

    private static string CombineDropboxRelative(string dropboxRoot, string relativeUnixPath)
    {
        var parts = relativeUnixPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { dropboxRoot }.Concat(parts).ToArray());
    }
}
