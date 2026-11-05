namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst „Next Stop“ auf (bevorzugt WAV, Fallback MP3 – Workspace/Dropbox-Ansagenordner).
/// </summary>
public static class PlanerNextStopMp3SoundResolver
{
    /// <summary>Bevorzugter Dateiname für neue Zuweisungen und Export.</summary>
    public const string FileName = "Next Stop.wav";

    /// <summary>Ältere Installationen / Dropbox-Dateien.</summary>
    public const string LegacyMp3FileName = "Next Stop.mp3";

    public static IReadOnlyList<string> CandidateFileNames { get; } =
    [
        FileName,
        LegacyMp3FileName
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

    /// <summary>
    /// Zieldateiname beim Zuweisen: WAV bleibt WAV (nicht als .mp3 speichern).
    /// </summary>
    public static string TargetFileNameForSource(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath)?.Trim().ToLowerInvariant() ?? string.Empty;
        return ext switch
        {
            ".wav" => FileName,
            ".mp3" => LegacyMp3FileName,
            ".ogg" => "Next Stop.ogg",
            _ => FileName
        };
    }
}
