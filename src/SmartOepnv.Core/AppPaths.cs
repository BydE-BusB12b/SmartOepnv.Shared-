using System.IO;

namespace SmartOepnv.Core;

public static class AppPaths
{
    /// <summary>
    /// Aktiver Planer-Betrieb (<see cref="Betrieb.BetriebProfileStore"/>).
    /// Leitstelle bleibt ohne Betrieb-Scope.
    /// </summary>
    public static string? ActiveBetriebId { get; private set; }

    public static void SetActiveBetrieb(string? betriebId) =>
        ActiveBetriebId = string.IsNullOrWhiteSpace(betriebId) ? null : betriebId.Trim();

    public static string GetRoamingDataDirectory(string appSubfolder)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart-OEPNV",
            appSubfolder);
        dir = AppendBetriebScopeIfPlaner(appSubfolder, dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLocalDataDirectory(string appSubfolder)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Smart-OEPNV",
            appSubfolder);
        dir = AppendBetriebScopeIfPlaner(appSubfolder, dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetWebView2UserDataDirectory(string appSubfolder) =>
        Path.Combine(GetLocalDataDirectory(appSubfolder), "WebView2");

    private static string AppendBetriebScopeIfPlaner(string appSubfolder, string dir)
    {
        if (!string.Equals(appSubfolder, "Planer", StringComparison.OrdinalIgnoreCase))
        {
            return dir;
        }

        if (string.IsNullOrWhiteSpace(ActiveBetriebId))
        {
            return dir;
        }

        return Path.Combine(dir, "betriebe", Betrieb.BetriebProfileStore.SanitizeId(ActiveBetriebId));
    }
}
