using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Models;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    [RelayCommand]
    private void OpenRawSoundsFolder()    {
        if (!AppServices.IsInitialized)
        {
            StatusMessage = "Workspace nicht initialisiert – zuerst Route-Paket laden.";
            return;
        }

        try
        {
            var folder = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            StatusMessage = $"Ordner geöffnet: {folder}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ordner konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ImportFromRawSoundsFolder()
    {
        if (!AppServices.IsInitialized)
        {
            StatusMessage = "Workspace nicht initialisiert – zuerst Route-Paket laden.";
            return;
        }

        var files = PlanerAnnouncementRawSoundsWorkspace.ListAudioFiles(AppServices.Workspace);
        if (files.Count == 0)
        {
            StatusMessage =
                "Keine Tondateien im Ordner „Ansagen-Rohdateien“ – MP3/WAV/OGG ablegen und erneut versuchen.";
            return;
        }

        var added = 0;
        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            if (IsRawSoundFileAlreadyUsed(path, fileName))
            {
                continue;
            }

            var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(
                _allAnnouncements.Select(a => a.AnnouncementCode));
            var displayName = Path.GetFileNameWithoutExtension(fileName);
            var item = new ManagedAnnouncementTemplateItem
            {
                AnnouncementCode = annCode,
                DisplayName = displayName,
                Category = "haltestelle",
                EmbeddedSoundFileName = string.Empty,
                IncludeInSpecialAnnouncements = false
            };

            _allAnnouncements.Add(item);
            _sequenceByAnnouncementId[item.Id] =
            [
                new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Audio,
                    DisplayName = fileName,
                    SourcePath = path
                }
            ];
            MarkAnnouncementAudioDirty(item.Id);
            added++;
        }

        if (added == 0)
        {
            StatusMessage = "Alle Rohdateien sind bereits in der Kartei – keine neuen Ansagen angelegt.";
            return;
        }

        EnsureStopCodesAndLinks();
        ApplyFilter();
        SelectedAnnouncement = FilteredAnnouncements.LastOrDefault();
        LoadAnnouncementSequenceForSelection();
        MarkDirty();
        StatusMessage =
            $"{added} Ansage(n) aus Rohdateien übernommen – Bezeichnung prüfen und mit „Speichern & JSON“ zusammenfügen.";
    }

    private bool IsRawSoundFileAlreadyUsed(string fullPath, string fileName)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        foreach (var announcement in _allAnnouncements)
        {
            if (!string.IsNullOrWhiteSpace(announcement.LocalAudioPath) &&
                string.Equals(
                    Path.GetFullPath(announcement.LocalAudioPath),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(announcement.DisplayName, baseName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_sequenceByAnnouncementId.TryGetValue(announcement.Id, out var sequence))
            {
                foreach (var entry in sequence.Where(e => e.Kind == AnnouncementSequenceEntryKind.Audio))
                {
                    if (string.IsNullOrWhiteSpace(entry.SourcePath))
                    {
                        continue;
                    }

                    if (string.Equals(
                            Path.GetFullPath(entry.SourcePath),
                            normalizedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (string.Equals(
                            Path.GetFileName(entry.SourcePath),
                            fileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
