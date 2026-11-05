namespace SmartOepnv.Core.RoutePackage;

/// <summary>Löst „Nach.wav“ auf (Workspace, Dropbox-Ansagenordner).</summary>
public static class PlanerNachSoundResolver
{
    public const string FileName = "Nach.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath);
}
