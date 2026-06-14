namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verhindert doppeltes Speichern/Backup/Dropbox-Upload beim Abmelden und Beenden.
/// </summary>
public static class PlanerWorkspaceSaveCoordinator
{
    private static long _lastPersistedTickMs;

    public static void MarkPersisted() =>
        _lastPersistedTickMs = Environment.TickCount64;

    public static bool WasPersistedRecently(int withinMs = 120_000) =>
        _lastPersistedTickMs > 0 &&
        Environment.TickCount64 - _lastPersistedTickMs < withinMs;

    public static void Reset() => _lastPersistedTickMs = 0;
}
