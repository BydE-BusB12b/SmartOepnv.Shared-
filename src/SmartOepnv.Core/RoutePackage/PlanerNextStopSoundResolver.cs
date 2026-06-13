using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst „Nächste Haltestelle.wav“ auf (Workspace, Dropbox-Ansagenordner).
/// </summary>
public static class PlanerNextStopSoundResolver
{
    public const string FileName = "Nächste Haltestelle.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath);
}

/// <summary>
/// Gemeinsame Auflösung für Standard-Ansagen unter Verkehrsbetrieb Hambloch/Ansagen.
/// </summary>
internal static class PlanerHamblochAnsagenSoundResolver
{
    private static readonly string[] DropboxRootFolderNames =
    [
        "Dropbox",
        "Dropbox (Personal)",
        "Dropbox (Business)",
        "Dropbox (Team)"
    ];

    public static string? TryResolve(
        LocalWorkspaceStore workspace,
        string fileName,
        string? dropboxApiFolderPath = null)
    {
        var workspacePath = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, fileName);
        if (workspacePath is not null)
        {
            return workspacePath;
        }

        foreach (var candidate in EnumerateCandidatePaths(fileName, dropboxApiFolderPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            TryCopyToWorkspace(workspace, candidate, fileName);
            return PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, fileName) ?? candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string fileName, string? dropboxApiFolderPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var rootName in DropboxRootFolderNames)
        {
            var dropboxRoot = Path.Combine(home, rootName);
            yield return Path.Combine(dropboxRoot, "Verkehrsbetrieb Hambloch", "Ansagen", fileName);
        }

        if (string.IsNullOrWhiteSpace(dropboxApiFolderPath))
        {
            yield break;
        }

        var syncFolder = DropboxSyncFolderLocator.TryResolveSmartOepnvFolder(dropboxApiFolderPath);
        if (syncFolder is null)
        {
            yield break;
        }

        var dropboxRootFromSync = FindDropboxRootFromPath(syncFolder);
        if (dropboxRootFromSync is not null)
        {
            yield return Path.Combine(dropboxRootFromSync, "Verkehrsbetrieb Hambloch", "Ansagen", fileName);
        }
    }

    private static string? FindDropboxRootFromPath(string syncFolder)
    {
        var current = syncFolder;
        while (!string.IsNullOrEmpty(current))
        {
            var name = Path.GetFileName(current);
            if (DropboxRootFolderNames.Any(root =>
                    string.Equals(name, root, StringComparison.OrdinalIgnoreCase)))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static void TryCopyToWorkspace(LocalWorkspaceStore workspace, string sourcePath, string fileName)
    {
        try
        {
            var target = Path.Combine(PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(workspace), fileName);
            if (File.Exists(target))
            {
                return;
            }

            File.Copy(sourcePath, target);
        }
        catch
        {
            // Quelle bleibt trotzdem nutzbar
        }
    }
}
