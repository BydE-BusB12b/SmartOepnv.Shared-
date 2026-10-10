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
        dialog.Title = "Tondatei(en) für neue Ansage(n) wählen – Mehrfachauswahl möglich (Strg+A)";
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

        var preview = new BulkAnnouncementFromAudioDialog(rows, SuggestSimilarStopNamesForBulk);
        if (DialogOwnerHelper.ShowOwnedDialog(preview, owner) != true)
        {
            return;
        }

        ManagedAnnouncementTemplateItem? lastTouched = null;
        var skippedExisting = 0;
        var created = 0;
        var updated = 0;
        foreach (var row in preview.ConfirmedRows)
        {
            if (row.AlreadyExists)
            {
                if (!row.UpdateExisting ||
                    string.IsNullOrWhiteSpace(row.ExistingAnnouncementId))
                {
                    skippedExisting++;
                    continue;
                }

                var existing = _allAnnouncements.FirstOrDefault(a => a.Id == row.ExistingAnnouncementId);
                if (existing is null)
                {
                    skippedExisting++;
                    continue;
                }

                ApplyBulkRowAudioToAnnouncement(existing, row);
                updated++;
                lastTouched = existing;
                continue;
            }

            var speicherName = row.DisplayName.Trim();
            var item = new ManagedAnnouncementTemplateItem
            {
                AnnouncementCode = row.AnnouncementCode,
                DisplayName = speicherName,
                Description = speicherName,
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
            ApplyBulkRowAudioToAnnouncement(item, row);
            created++;
            lastTouched = item;
        }

        ApplyFilter();
        if (lastTouched is not null)
        {
            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == lastTouched.Id);
        }

        MarkDirty();
        StatusMessage = BuildBulkImportStatusMessage(created, updated, skippedExisting);
    }

    private void ApplyBulkRowAudioToAnnouncement(
        ManagedAnnouncementTemplateItem item,
        BulkAnnouncementFromAudioRow row)
    {
        var speicherName = row.DisplayName.Trim();
        item.DisplayName = speicherName;
        item.Description = speicherName;
        item.EmbeddedSoundFileName =
            ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(item.AnnouncementCode, speicherName);
        item.IncludeGong = row.IncludeGong;
        item.IncludeSondergong = row.IncludeSondergong;
        item.IncludeNextStopGerman = row.IncludeNextStopGerman;
        item.IncludeNextStopMp3 = row.IncludeNextStopMp3;
        item.IncludeFollowingStops = row.IncludeFollowingStops;

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
    }

    private static string BuildBulkImportStatusMessage(int created, int updated, int skipped)
    {
        var parts = new List<string>();
        if (created > 0)
        {
            parts.Add($"{created} angelegt");
        }

        if (updated > 0)
        {
            parts.Add($"{updated} aktualisiert");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} übersprungen (bereits vorhanden)");
        }

        var summary = parts.Count > 0 ? string.Join(", ", parts) : "Keine Änderungen";
        return $"{summary} – „Speichern & JSON“ übernehmen.";
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

    /// <summary>Speichername = WAV-Dateiname ohne Endung (unverändert).</summary>
    private static string DeriveDisplayNameFromAudioFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Ansage" : name;
    }

    private void RefreshBulkRowDuplicateCheck(BulkAnnouncementFromAudioRow row)
    {
        var match = FindExistingAnnouncementMatch(row.SourcePath, row.SoundFileName, row.DisplayName);
        if (match is null)
        {
            row.SetExistingMatch(false, null);
            return;
        }

        row.SetExistingMatch(
            exists: true,
            label: match.Label,
            existingAnnouncementId: match.AnnouncementId,
            existingAnnouncementCode: match.AnnouncementCode);
    }

    private sealed record BulkImportMatch(
        string Label,
        string? AnnouncementId,
        string? AnnouncementCode);

    private BulkImportMatch? FindExistingAnnouncementMatch(
        string sourcePath,
        string soundFileName,
        string displayName)
    {
        var pickedKey = NormalizeSoundMatchKey(soundFileName);
        var displayKey = NormalizeSoundMatchKey(displayName);
        var normalizedSourcePath = Path.GetFullPath(sourcePath);

        foreach (var ann in _allAnnouncements)
        {
            if (AnnouncementMatchesBulkImport(ann, normalizedSourcePath, soundFileName, pickedKey, displayName, displayKey))
            {
                return new BulkImportMatch(
                    FormatExistingAnnouncementLabel(ann),
                    ann.Id,
                    ManagedAnnouncementTemplateItem.NormalizeCode(ann.AnnouncementCode));
            }
        }

        foreach (var stop in _allStops)
        {
            if (StopMatchesBulkImport(stop, soundFileName, pickedKey, displayName, displayKey))
            {
                var linked = _allAnnouncements.FirstOrDefault(a => a.StopTemplateId == stop.Id);
                if (linked is not null)
                {
                    return new BulkImportMatch(
                        FormatExistingAnnouncementLabel(linked),
                        linked.Id,
                        ManagedAnnouncementTemplateItem.NormalizeCode(linked.AnnouncementCode));
                }

                var stopName = string.IsNullOrWhiteSpace(stop.StopNameItcs)
                    ? "Haltestelle"
                    : stop.StopNameItcs.Trim();
                return new BulkImportMatch($"Haltestelle – {stopName}", null, null);
            }
        }

        return null;
    }

    /// <summary>
    /// Ähnliche Namen aus Haltestellen- und Ansagenliste (Schreibweisen / Bf↔Bahnhof korrigieren).
    /// </summary>
    private IReadOnlyList<string> SuggestSimilarStopNamesForBulk(string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(q))
        {
            return [];
        }

        var qKey = NormalizeSoundMatchKey(q);
        var qKeys = EnumerateSoundMatchKeys(q).ToHashSet(StringComparer.Ordinal);
        var scored = new List<(string Name, int Score)>();

        void Consider(string? name)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            var score = ScoreStopNameSimilarity(q, qKey, qKeys, trimmed);
            if (score <= 0)
            {
                return;
            }

            scored.Add((trimmed, score));
        }

        foreach (var stop in _allStops)
        {
            Consider(stop.StopNameItcs);
            Consider(stop.StopDisplay);
        }

        foreach (var ann in _allAnnouncements)
        {
            Consider(ann.DisplayName);
        }

        return scored
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(s => s.Name)
            .ToList();
    }

    private static int ScoreStopNameSimilarity(
        string query,
        string queryKey,
        HashSet<string> queryKeys,
        string stopName)
    {
        if (string.Equals(stopName, query, StringComparison.OrdinalIgnoreCase))
        {
            return 10_000;
        }

        var stopKey = NormalizeSoundMatchKey(stopName);
        if (string.IsNullOrEmpty(stopKey))
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(queryKey) &&
            string.Equals(stopKey, queryKey, StringComparison.Ordinal))
        {
            return 9_000;
        }

        foreach (var key in EnumerateSoundMatchKeys(stopName))
        {
            if (queryKeys.Contains(key))
            {
                return 8_500;
            }
        }

        var score = 0;
        if (!string.IsNullOrEmpty(queryKey))
        {
            if (stopKey.StartsWith(queryKey, StringComparison.Ordinal) ||
                queryKey.StartsWith(stopKey, StringComparison.Ordinal))
            {
                score = Math.Max(score, 7000 - Math.Abs(stopKey.Length - queryKey.Length) * 20);
            }
            else if (stopKey.Contains(queryKey, StringComparison.Ordinal) ||
                     queryKey.Contains(stopKey, StringComparison.Ordinal))
            {
                score = Math.Max(score, 5500 - Math.Abs(stopKey.Length - queryKey.Length) * 25);
            }

            var distance = LevenshteinDistance(queryKey, stopKey);
            var maxLen = Math.Max(queryKey.Length, stopKey.Length);
            if (maxLen > 0 && distance <= Math.Max(2, maxLen / 3))
            {
                var levScore = 5000 - distance * 400;
                if (levScore > score)
                {
                    score = levScore;
                }
            }
        }

        if (stopName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(stopName, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 4000);
        }

        return score;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
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
