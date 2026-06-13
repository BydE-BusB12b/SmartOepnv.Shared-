namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst die Hambloch-Gong-Datei auf (Workspace, Dropbox-Ansagenordner).
/// </summary>
public static class PlanerGongSoundResolver
{
    public const string GongFileName = "Hambloch Gong.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, GongFileName, dropboxApiFolderPath);
}
