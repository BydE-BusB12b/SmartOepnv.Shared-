namespace SmartOepnv.Core.RoutePackage;

/// <summary>Löst „und.wav“ auf (Begrüßungs-Aufzählung „A, B und C“).</summary>
public static class PlanerUndSoundResolver
{
    public const string FileName = "und.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath);
}
