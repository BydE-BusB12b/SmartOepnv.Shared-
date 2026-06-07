namespace SmartOepnv.AppShared.ViewModels;

/// <summary>
/// Bereiche mit lokalem Editor-Puffer: nur bei Änderungen speichern / neu laden.
/// </summary>
public interface IEditorAreaViewModel
{
    bool HasPendingChanges { get; }

    void CommitChangesIfDirty();

    void RefreshFromEditorIfNeeded();
}
