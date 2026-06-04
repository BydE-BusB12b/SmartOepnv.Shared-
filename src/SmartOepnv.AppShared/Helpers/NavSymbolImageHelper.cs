using System.IO;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.Helpers;

public static class NavSymbolImageHelper
{
    public static string? GetImageFullPath(string? symbolType)
    {
        var file = NavSymbolImageMap.GetFileName(symbolType);
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "navi_grafiken", file);
        return File.Exists(path) ? path : null;
    }

    public static Uri? GetImageUri(string? symbolType)
    {
        var path = GetImageFullPath(symbolType);
        return path is null ? null : new Uri(path, UriKind.Absolute);
    }
}
