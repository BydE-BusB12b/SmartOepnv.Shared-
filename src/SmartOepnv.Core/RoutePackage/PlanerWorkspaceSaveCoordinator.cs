namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verhindert doppeltes Speichern/Backup/Dropbox-Upload beim Abmelden und Beenden.
/// </summary>
public static class PlanerWorkspaceSaveCoordinator
{
    private static long _lastLocalSaveTickMs;
    private static long _lastDropboxExportTickMs;

    public static void MarkLocalSaved() =>
        _lastLocalSaveTickMs = Environment.TickCount64;

    public static void MarkDropboxExported() =>
        _lastDropboxExportTickMs = Environment.TickCount64;

    public static bool WasLocalSavedRecently(int withinMs = 120_000) =>
        _lastLocalSaveTickMs > 0 &&
        Environment.TickCount64 - _lastLocalSaveTickMs < withinMs;

    public static bool WasDropboxExportedRecently(int withinMs = 120_000) =>
        _lastDropboxExportTickMs > 0 &&
        Environment.TickCount64 - _lastDropboxExportTickMs < withinMs;

    public static void Reset()
    {
        _lastLocalSaveTickMs = 0;
        _lastDropboxExportTickMs = 0;
    }
}
