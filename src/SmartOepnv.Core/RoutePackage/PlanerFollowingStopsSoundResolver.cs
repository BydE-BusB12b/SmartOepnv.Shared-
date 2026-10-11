namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst „Folgende Halte“ auf (Workspace/Dropbox-Ansagenordner).
/// </summary>
public static class PlanerFollowingStopsSoundResolver
{
    public const string FileName = "Folgende Halte.wav";

    public static IReadOnlyList<string> CandidateFileNames { get; } =
    [
        FileName,
        "Folgende Halte.mp3"
    ];

    public static string? TryResolve(LocalWorkspaceStore workspace, string? dropboxApiFolderPath = null)
    {
        foreach (var name in CandidateFileNames)
        {
            var path = PlanerHamblochAnsagenSoundResolver.TryResolve(
                workspace,
                name,
                dropboxApiFolderPath);
            if (path is not null)
            {
                return path;
            }
        }

        return null;
    }

    public static string TargetFileNameForSource(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath)?.Trim().ToLowerInvariant() ?? string.Empty;
        return ext switch
        {
            ".wav" => FileName,
            ".mp3" => "Folgende Halte.mp3",
            ".ogg" => "Folgende Halte.ogg",
            _ => FileName
        };
    }
}
