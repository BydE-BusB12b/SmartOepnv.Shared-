namespace SmartOepnv.Core.RoutePackage;

/// <summary>Löst Linienansagen (z. B. „S28.wav“) aus dem Ansagen-Ordner auf.</summary>
public static class PlanerLineSoundResolver
{
    public static string BuildFileName(string displayLine)
    {
        var trimmed = displayLine?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : $"{trimmed}.wav";
    }

    public static string? TryResolveFileName(
        LocalWorkspaceStore workspace,
        string displayLine,
        string? dropboxApiFolderPath = null)
    {
        var fileName = BuildFileName(displayLine);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, fileName, dropboxApiFolderPath) is not null
            ? fileName
            : null;
    }
}
