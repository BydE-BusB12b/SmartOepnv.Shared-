namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst die in den Planer-Einstellungen hinterlegte Sondergong-Datei auf.
/// </summary>
public static class PlanerSondergongSoundResolver
{
    public static string? ConfiguredFileName(PlanerAppSettings? settings) =>
        settings?.SondergongFileName?.Trim();

    public static string? TryResolve(
        LocalWorkspaceStore workspace,
        PlanerAppSettings? settings,
        string settingsSubfolder,
        string? dropboxApiFolderPath = null)
    {
        var fileName = ConfiguredFileName(settings);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var fromSettings = PlanerSondergongSoundStore.TryGetLocalFilePath(settingsSubfolder, fileName);
        if (fromSettings is not null)
        {
            return fromSettings;
        }

        return PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, fileName, dropboxApiFolderPath);
    }
}
