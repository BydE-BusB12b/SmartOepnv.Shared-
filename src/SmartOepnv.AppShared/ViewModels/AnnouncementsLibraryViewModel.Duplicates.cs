using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    [RelayCommand]
    private void CheckDuplicateAnnouncements()
    {
        if (!AppServices.IsInitialized)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        PersistCurrentAnnouncementSequence();

        var clusters = FindDuplicateAnnouncementClusters();
        if (clusters.Count == 0)
        {
            StatusMessage = "Keine doppelten Ansagen gefunden.";
            return;
        }

        var dialogGroups = clusters.Select(BuildDuplicateDialogGroup).ToList();
        var owner = DialogOwnerHelper.ResolveOwner();
        var dialog = new DuplicateAnnouncementsDialog(dialogGroups);
        if (DialogOwnerHelper.ShowOwnedDialog(dialog, owner) != true)
        {
            return;
        }

        ApplyDuplicateResolutions(dialog.Resolutions);
    }

    private List<List<ManagedAnnouncementTemplateItem>> FindDuplicateAnnouncementClusters()
    {
        var items = _allAnnouncements.ToList();
        if (items.Count < 2)
        {
            return [];
        }

        var parent = Enumerable.Range(0, items.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
            {
                parent[b] = a;
            }
        }

        var keyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var key in EnumerateAnnouncementMatchKeys(items[i]))
            {
                if (keyToIndex.TryGetValue(key, out var existing))
                {
                    Union(i, existing);
                }
                else
                {
                    keyToIndex[key] = i;
                }
            }
        }

        return items
            .Select((item, index) => (Item: item, Root: Find(index)))
            .GroupBy(x => x.Root)
            .Select(g => g.Select(x => x.Item)
                .OrderBy(PreferredDuplicatePrimaryRank)
                .ThenBy(a => a.AnnouncementCode, StringComparer.Ordinal)
                .ToList())
            .Where(g => g.Count >= 2)
            .OrderBy(g => g[0].DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateAnnouncementMatchKeys(ManagedAnnouncementTemplateItem ann)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in EnumerateSoundMatchKeys(ann.EmbeddedSoundFileName))
        {
            keys.Add(key);
        }

        foreach (var key in EnumerateSoundMatchKeys(ann.DisplayName))
        {
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>Niedriger = eher als Hauptdatei vorschlagen (_zusammen bewusst nach hinten).</summary>
    private int PreferredDuplicatePrimaryRank(ManagedAnnouncementTemplateItem ann)
    {
        var rank = 0;
        // _zusammen nicht behalten – normale Datei bevorzugen
        if (ann.IsMergedAnnouncement)
        {
            rank += 100;
        }

        if (!_sequenceByAnnouncementId.TryGetValue(ann.Id, out var sequence) || sequence.Count == 0)
        {
            if (ann.AnnouncementSequence.Count == 0)
            {
                rank += 5;
            }
        }

        if (string.IsNullOrWhiteSpace(ann.EmbeddedSoundFileName) &&
            string.IsNullOrWhiteSpace(ann.LocalAudioPath))
        {
            rank += 20;
        }

        return rank;
    }

    private DuplicateAnnouncementsDialog.DuplicateGroup BuildDuplicateDialogGroup(
        List<ManagedAnnouncementTemplateItem> cluster)
    {
        var titleName = cluster
                            .Select(a => a.DisplayName?.Trim())
                            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                        ?? cluster[0].AnnouncementCode;
        var zusammenCount = cluster.Count(a => a.IsMergedAnnouncement);
        var title = zusammenCount > 0
            ? $"{titleName} ({cluster.Count}×, davon {zusammenCount}× _zusammen)"
            : $"{titleName} ({cluster.Count}×)";
        var group = new DuplicateAnnouncementsDialog.DuplicateGroup
        {
            Title = title
        };

        var preferred = cluster
            .OrderBy(PreferredDuplicatePrimaryRank)
            .ThenBy(a => a.AnnouncementCode, StringComparer.Ordinal)
            .First();

        foreach (var ann in cluster)
        {
            var isPrimary = ReferenceEquals(ann, preferred);
            var isZusammen = ann.IsMergedAnnouncement;
            var row = new DuplicateAnnouncementsDialog.EntryRow
            {
                Announcement = ann,
                Label = isZusammen
                    ? $"{ann.DisplayLabel}  ·  _zusammen"
                    : ann.DisplayLabel,
                Detail = BuildDuplicateEntryDetail(ann),
                IsMergedFile = isZusammen,
                Group = group
            };
            row.ForcePrimary(isPrimary);
            // _zusammen und alle Nicht-Hauptdateien zum Löschen vorschlagen
            if (!isPrimary || isZusammen)
            {
                if (!isPrimary)
                {
                    row.MarkForDeletion = true;
                }
            }

            group.Entries.Add(row);
        }

        // Falls die vorgeschlagene Hauptdatei doch _zusammen ist (nur solche in der Gruppe):
        // trotzdem behalten, aber Hinweis steht im Label.
        return group;
    }

    private string BuildDuplicateEntryDetail(ManagedAnnouncementTemplateItem ann)
    {
        var parts = new List<string>();
        if (ann.IsMergedAnnouncement)
        {
            parts.Add("_zusammen – nicht behalten");
        }

        if (!string.IsNullOrWhiteSpace(ann.EmbeddedSoundFileName))
        {
            parts.Add(ann.EmbeddedSoundFileName.Trim());
        }

        if (_sequenceByAnnouncementId.TryGetValue(ann.Id, out var sequence) && sequence.Count > 0)
        {
            parts.Add($"Sequenz: {sequence.Count}");
        }
        else if (ann.AnnouncementSequence.Count > 0)
        {
            parts.Add($"Sequenz: {ann.AnnouncementSequence.Count}");
        }

        var stopRefs = CountStopsReferencingAnnouncement(ann);
        if (stopRefs > 0)
        {
            parts.Add($"{stopRefs} Haltestelle(n)");
        }

        return parts.Count == 0 ? "ohne Ton" : string.Join(" · ", parts);
    }

    private int CountStopsReferencingAnnouncement(ManagedAnnouncementTemplateItem ann)
    {
        var file = ann.EmbeddedSoundFileName?.Trim();
        var keys = EnumerateAnnouncementMatchKeys(ann).ToHashSet(StringComparer.Ordinal);
        var count = 0;
        foreach (var stop in _allStops)
        {
            var stopFile = stop.EmbeddedSoundFileName?.Trim();
            if (string.IsNullOrEmpty(stopFile))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(file) &&
                string.Equals(stopFile, file, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                continue;
            }

            if (keys.Overlaps(EnumerateSoundMatchKeys(stopFile)))
            {
                count++;
            }
        }

        return count;
    }

    private void ApplyDuplicateResolutions(
        IReadOnlyList<(ManagedAnnouncementTemplateItem Primary, IReadOnlyList<ManagedAnnouncementTemplateItem> ToDelete)>
            resolutions)
    {
        if (resolutions.Count == 0)
        {
            StatusMessage = "Keine Löschungen ausgewählt.";
            return;
        }

        var deleted = 0;
        var stopsRemapped = 0;
        var selectedId = SelectedAnnouncement?.Id;

        foreach (var (primary, toDelete) in resolutions)
        {
            if (toDelete.Count == 0)
            {
                continue;
            }

            EnsurePrimaryHasUsableSound(primary, toDelete);
            stopsRemapped += RemapStopsFromDeletedToPrimary(primary, toDelete);

            if (string.IsNullOrWhiteSpace(primary.StopTemplateId))
            {
                var donorLink = toDelete.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.StopTemplateId));
                if (donorLink is not null)
                {
                    primary.StopTemplateId = donorLink.StopTemplateId;
                }
            }

            foreach (var dup in toDelete)
            {
                if (ReferenceEquals(dup, primary) ||
                    string.Equals(dup.Id, primary.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                // Verhindert, dass EnsureAnnouncementsFromStopTemplates die Gelöschten neu anlegt.
                SuppressStopAnnouncementSound(dup.EmbeddedSoundFileName, dup.DisplayName);
                // Kein DetachStopSounds – Ton wurde bereits auf die Hauptdatei umgebogen.
                _allAnnouncements.RemoveAll(a => a.Id == dup.Id);
                _sequenceByAnnouncementId.Remove(dup.Id);
                _gongByAnnouncementId.Remove(dup.Id);
                _sondergongByAnnouncementId.Remove(dup.Id);
                _nextStopByAnnouncementId.Remove(dup.Id);
                _nextStopMp3ByAnnouncementId.Remove(dup.Id);
                _followingStopsByAnnouncementId.Remove(dup.Id);
                deleted++;
            }
        }

        ApplyFilter();
        if (selectedId is not null &&
            _allAnnouncements.Any(a => a.Id == selectedId))
        {
            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == selectedId)
                                   ?? FilteredAnnouncements.FirstOrDefault();
        }
        else
        {
            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();
        }

        MarkDirty();
        StatusMessage = stopsRemapped > 0
            ? $"{deleted} doppelte Ansage(n) entfernt, {stopsRemapped} Haltestelle(n) auf Hauptdatei umgestellt – „Speichern & JSON“ nicht vergessen."
            : $"{deleted} doppelte Ansage(n) entfernt – „Speichern & JSON“ nicht vergessen.";
    }

    private static void EnsurePrimaryHasUsableSound(
        ManagedAnnouncementTemplateItem primary,
        IReadOnlyList<ManagedAnnouncementTemplateItem> toDelete)
    {
        if (!string.IsNullOrWhiteSpace(primary.EmbeddedSoundFileName) ||
            !string.IsNullOrWhiteSpace(primary.LocalAudioPath))
        {
            return;
        }

        var donor = toDelete.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.EmbeddedSoundFileName) ||
            !string.IsNullOrWhiteSpace(d.LocalAudioPath));
        if (donor is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(primary.EmbeddedSoundFileName))
        {
            primary.EmbeddedSoundFileName = donor.EmbeddedSoundFileName;
        }

        if (string.IsNullOrWhiteSpace(primary.LocalAudioPath))
        {
            primary.LocalAudioPath = donor.LocalAudioPath;
        }

        primary.NotifyDisplayLabelChanged();
    }

    private int RemapStopsFromDeletedToPrimary(
        ManagedAnnouncementTemplateItem primary,
        IReadOnlyList<ManagedAnnouncementTemplateItem> toDelete)
    {
        var primaryFile = primary.EmbeddedSoundFileName?.Trim();
        if (string.IsNullOrWhiteSpace(primaryFile))
        {
            return 0;
        }

        var deletedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dup in toDelete)
        {
            var file = dup.EmbeddedSoundFileName?.Trim();
            if (!string.IsNullOrEmpty(file))
            {
                deletedFiles.Add(file);
            }

            foreach (var key in EnumerateAnnouncementMatchKeys(dup))
            {
                deletedKeys.Add(key);
            }
        }

        deletedFiles.Remove(primaryFile);
        foreach (var key in EnumerateAnnouncementMatchKeys(primary))
        {
            deletedKeys.Remove(key);
        }

        var remapped = 0;
        foreach (var stop in _allStops)
        {
            var stopFile = stop.EmbeddedSoundFileName?.Trim();
            if (string.IsNullOrEmpty(stopFile))
            {
                continue;
            }

            var matchExact = deletedFiles.Contains(stopFile);
            var matchKey = !matchExact &&
                           EnumerateSoundMatchKeys(stopFile).Any(deletedKeys.Contains);
            if (!matchExact && !matchKey)
            {
                continue;
            }

            if (string.Equals(stopFile, primaryFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            stop.EmbeddedSoundFileName = primaryFile;
            remapped++;
        }

        return remapped;
    }
}
