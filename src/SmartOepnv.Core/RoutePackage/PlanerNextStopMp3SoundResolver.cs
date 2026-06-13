namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst „Next Stop.mp3“ auf (Workspace, Dropbox-Ansagenordner).
/// </summary>
public static class PlanerNextStopMp3SoundResolver
{
    public const string FileName = "Next Stop.mp3";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath);
}
