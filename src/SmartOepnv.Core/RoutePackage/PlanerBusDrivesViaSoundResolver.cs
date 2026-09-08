namespace SmartOepnv.Core.RoutePackage;

/// <summary>Löst „Dieser Bus fährt über.wav“ auf (Workspace, Dropbox-Ansagenordner).</summary>
public static class PlanerBusDrivesViaSoundResolver
{
    public const string FileName = "Dieser Bus fährt über.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath) is not null
            ? FileName
            : null;
}
