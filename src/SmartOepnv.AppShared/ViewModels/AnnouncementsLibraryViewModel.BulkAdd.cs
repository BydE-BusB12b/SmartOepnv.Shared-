using System.IO;
using System.Windows;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    private void AddAnnouncementsWithAudioPicker()
    {
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = DialogOwnerHelper.ResolveOwner();
        var dialog = CreateAudioOpenFileDialog();
        dialog.Title = "Tondatei(en) für neue Ansage(n) wählen";
        dialog.Multiselect = true;
        DialogOwnerHelper.PrepareForModalDialog(owner);
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            DialogOwnerHelper.RestoreAfterModalDialog(owner);
            return;
        }

        var paths = dialog.FileNames
            .Where(p => File.Exists(p) && EmbeddedSoundCatalog.IsAudioFile(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            DialogOwnerHelper.RestoreAfterModalDialog(owner);
            StatusMessage = "Keine gültigen Audiodateien ausgewählt.";
            return;
        }

        if (paths.Length == 1)
        {
            DialogOwnerHelper.RestoreAfterModalDialog(owner);
            CreateAndSelectNewAnnouncement();
            AppendPickedAudioFile(paths[0]);
            return;
        }

        AddMultipleAnnouncementsFromAudio(paths, owner);
    }

    private void AddMultipleAnnouncementsFromAudio(IReadOnlyList<string> filePaths, Window? owner)
    {
        PersistCurrentAnnouncementSequence();

        var existingCodes = _allAnnouncements.Select(a => a.AnnouncementCode);
        var allocatedCodes = ManagedAnnouncementTemplateItem.AllocateSequentialCodes(existingCodes, filePaths.Count);
        if (allocatedCodes.Count != filePaths.Count)
        {
            StatusMessage = "Keine freien Ansagen-IDs mehr (max. 9999).";
            DialogOwnerHelper.RestoreAfterModalDialog(owner);
            return;
        }

        var rows = new List<BulkAnnouncementFromAudioRow>();
        for (var i = 0; i < filePaths.Count; i++)
        {
            var path = filePaths[i];
            var row = new BulkAnnouncementFromAudioRow(
                allocatedCodes[i],
                path,
                DeriveDisplayNameFromAudioFile(path),
                RefreshBulkRowDuplicateCheck);
            RefreshBulkRowDuplicateCheck(row);
            rows.Add(row);
        }

        var preview = new BulkAnnouncementFromAudioDialog(rows);
        if (DialogOwnerHelper.ShowOwnedDialog(preview, owner) != true)
        {
            return;
        }

        ManagedAnnouncementTemplateItem? lastAdded = null;
        var skippedExisting = 0;
        foreach (var row in preview.ConfirmedRows)
        {
            if (row.AlreadyExists)
            {
                skippedExisting++;
                continue;
            }

            var item = new ManagedAnnouncementTemplateItem
            {
                AnnouncementCode = row.AnnouncementCode,
                DisplayName = row.DisplayName.Trim(),
                Category = "haltestelle",
                EmbeddedSoundFileName = row.SaveFileName,
                IncludeInSpecialAnnouncements = false,
                IncludeGong = row.IncludeGong,
                IncludeSondergong = row.IncludeSondergong,
                IncludeNextStopGerman = row.IncludeNextStopGerman,
                IncludeNextStopMp3 = row.IncludeNextStopMp3,
                IncludeFollowingStops = row.IncludeFollowingStops
            };

            _allAnnouncements.Add(item);
            TryCopyStandardSoundToRawWorkspace(row.SourcePath);

            _sequenceByAnnouncementId[item.Id] =
            [
                new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Audio,
                    DisplayName = row.SoundFileName,
                    SourcePath = row.SourcePath
                }
            ];
            _gongByAnnouncementId[item.Id] = row.IncludeGong;
            _sondergongByAnnouncementId[item.Id] = row.IncludeSondergong;
            _nextStopByAnnouncementId[item.Id] = row.IncludeNextStopGerman;
            _nextStopMp3ByAnnouncementId[item.Id] = row.IncludeNextStopMp3;
            _followingStopsByAnnouncementId[item.Id] = row.IncludeFollowingStops;
            _announcementsNeedingAudioMaterialization.Add(item.Id);

            lastAdded = item;
        }

        ApplyFilter();
        if (lastAdded is not null)
        {
            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == lastAdded.Id);
        }

        MarkDirty();
        StatusMessage = skippedExisting > 0
            ? $"{preview.ConfirmedRows.Count - skippedExisting} Ansagen angelegt, {skippedExisting} übersprungen (bereits vorhanden) – „Speichern & JSON“ übernehmen."
            : $"{preview.ConfirmedRows.Count} Ansagen angelegt – „Speichern & JSON“ übernehmen.";
    }

    private ManagedAnnouncementTemplateItem CreateAndSelectNewAnnouncement()
    {
        var code = ManagedAnnouncementTemplateItem.SuggestNextCode(
            _allAnnouncements.Select(a => a.AnnouncementCode));
        var item = new ManagedAnnouncementTemplateItem
        {
            AnnouncementCode = code,
            DisplayName = string.Empty,
            Category = "haltestelle",
            EmbeddedSoundFileName = string.Empty,
            IncludeInSpecialAnnouncements = false
        };

        _allAnnouncements.Add(item);
        ApplyFilter();
        SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == item.Id);
        return item;
    }

    private static string DeriveDisplayNameFromAudioFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Ansage";
        }

        name = name.Replace('_', ' ').Replace('-', ' ');
        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        return name.Trim();
    }

    private void RefreshBulkRowDuplicateCheck(BulkAnnouncementFromAudioRow row)
    {
        var match = FindExistingAnnouncementMatch(row.SourcePath, row.SoundFileName, row.DisplayName);
        row.SetExistingMatch(match is not null, match);
    }

    private string? FindExistingAnnouncementMatch(string sourcePath, string soundFileName, string displayName)
    {
        var pickedKey = NormalizeSoundMatchKey(soundFileName);
        var displayKey = NormalizeSoundMatchKey(displayName);
        var normalizedSourcePath = Path.GetFullPath(sourcePath);

        foreach (var ann in _allAnnouncements)
        {
            if (AnnouncementMatchesBulkImport(ann, normalizedSourcePath, soundFileName, pickedKey, displayName, displayKey))
            {
                return FormatExistingAnnouncementLabel(ann);
            }
        }

        foreach (var stop in _allStops)
        {
            if (StopMatchesBulkImport(stop, soundFileName, pickedKey, displayName, displayKey))
            {
                var linked = _allAnnouncements.FirstOrDefault(a => a.StopTemplateId == stop.Id);
                if (linked is not null)
                {
                    return FormatExistingAnnouncementLabel(linked);
                }

                var stopName = string.IsNullOrWhiteSpace(stop.StopNameItcs)
                    ? "Haltestelle"
                    : stop.StopNameItcs.Trim();
                return $"Haltestelle – {stopName}";
            }
        }

        return null;
    }

    private bool AnnouncementMatchesBulkImport(
        ManagedAnnouncementTemplateItem ann,
        string normalizedSourcePath,
        string soundFileName,
        string pickedKey,
        string displayName,
        string displayKey)
    {
        if (!string.IsNullOrWhiteSpace(ann.EmbeddedSoundFileName))
        {
            var embeddedName = Path.GetFileName(ann.EmbeddedSoundFileName.Trim());
            if (string.Equals(embeddedName, soundFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(pickedKey) &&
                string.Equals(NormalizeSoundMatchKey(embeddedName), pickedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(displayName) &&
            !string.IsNullOrWhiteSpace(ann.DisplayName) &&
            string.Equals(ann.DisplayName.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(displayKey) &&
            !string.IsNullOrWhiteSpace(ann.DisplayName) &&
            string.Equals(NormalizeSoundMatchKey(ann.DisplayName), displayKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ann.LocalAudioPath) &&
            string.Equals(Path.GetFullPath(ann.LocalAudioPath), normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_sequenceByAnnouncementId.TryGetValue(ann.Id, out var sequence))
        {
            return false;
        }

        foreach (var entry in sequence.Where(e => e.Kind == AnnouncementSequenceEntryKind.Audio))
        {
            if (!string.IsNullOrWhiteSpace(entry.SourcePath))
            {
                if (string.Equals(Path.GetFullPath(entry.SourcePath), normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(Path.GetFileName(entry.SourcePath), soundFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.DisplayName) &&
                string.Equals(entry.DisplayName.Trim(), soundFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(pickedKey) &&
                !string.IsNullOrWhiteSpace(entry.DisplayName) &&
                string.Equals(NormalizeSoundMatchKey(entry.DisplayName), pickedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StopMatchesBulkImport(
        ManagedStopTemplateItem stop,
        string soundFileName,
        string pickedKey,
        string displayName,
        string displayKey)
    {
        if (!string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))
        {
            var embeddedName = Path.GetFileName(stop.EmbeddedSoundFileName.Trim());
            if (string.Equals(embeddedName, soundFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(pickedKey) &&
                string.Equals(NormalizeSoundMatchKey(embeddedName), pickedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(displayName) &&
            !string.IsNullOrWhiteSpace(stop.StopNameItcs) &&
            string.Equals(stop.StopNameItcs.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(displayKey) &&
               !string.IsNullOrWhiteSpace(stop.StopNameItcs) &&
               string.Equals(NormalizeSoundMatchKey(stop.StopNameItcs), displayKey, StringComparison.Ordinal);
    }

    private static string FormatExistingAnnouncementLabel(ManagedAnnouncementTemplateItem ann)
    {
        var code = ManagedAnnouncementTemplateItem.NormalizeCode(ann.AnnouncementCode);
        var name = string.IsNullOrWhiteSpace(ann.DisplayName) ? "Ohne Bezeichnung" : ann.DisplayName.Trim();
        return string.IsNullOrEmpty(code) ? name : $"{code} – {name}";
    }
}
