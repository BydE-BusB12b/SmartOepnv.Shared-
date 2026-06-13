namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst die Endhaltestellen-Ansage auf (Verkehrsbetrieb Hambloch/Ansagen).
/// </summary>
public static class PlanerEndStopSoundResolver
{
    public const string FileName = "Endhalestelle.wav";

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, FileName, dropboxApiFolderPath);
}
