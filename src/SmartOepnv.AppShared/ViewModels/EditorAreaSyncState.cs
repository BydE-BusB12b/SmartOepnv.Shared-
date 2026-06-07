using SmartOepnv.Core;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>
/// Verzögertes Neu laden / Speichern: nur bei Revision-Wechsel bzw. lokalen Änderungen.
/// </summary>
internal sealed class EditorAreaSyncState
{
    private int _loadedRevision = -1;
    private bool _hasPendingChanges;

    public bool HasPendingChanges => _hasPendingChanges;

    public void MarkDirty() => _hasPendingChanges = true;

    public bool ShouldRefresh(bool hasLoadedContent) =>
        !_hasPendingChanges &&
        (_loadedRevision != AppServices.Routes.EditorDataRevision || !hasLoadedContent);

    public bool ShouldCommit(string? currentFingerprint, string? loadedFingerprint) =>
        _hasPendingChanges ||
        (currentFingerprint is not null && !string.Equals(currentFingerprint, loadedFingerprint, StringComparison.Ordinal));

    public void AfterRefresh()
    {
        _loadedRevision = AppServices.Routes.EditorDataRevision;
        _hasPendingChanges = false;
    }

    public void AfterCommit()
    {
        _hasPendingChanges = false;
        _loadedRevision = AppServices.Routes.EditorDataRevision;
    }
}
