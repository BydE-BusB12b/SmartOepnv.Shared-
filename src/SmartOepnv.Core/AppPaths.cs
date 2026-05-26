using System.IO;

namespace SmartOepnv.Core;

public static class AppPaths
{
    public static string GetRoamingDataDirectory(string appSubfolder)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart-OEPNV",
            appSubfolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLocalDataDirectory(string appSubfolder)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Smart-OEPNV",
            appSubfolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetWebView2UserDataDirectory(string appSubfolder) =>
        Path.Combine(GetLocalDataDirectory(appSubfolder), "WebView2");
}
