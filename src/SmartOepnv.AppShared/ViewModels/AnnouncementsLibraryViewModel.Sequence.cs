using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Models;
using SmartOepnv.Core;
using SmartOepnv.Core.Betrieb;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    private readonly Dictionary<string, List<AnnouncementAudioSequenceItem>> _sequenceByAnnouncementId = new();
    private readonly Dictionary<string, bool> _gongByAnnouncementId = new();
    private readonly Dictionary<string, bool> _sondergongByAnnouncementId = new();
    private readonly Dictionary<string, bool> _nextStopByAnnouncementId = new();
    private readonly Dictionary<string, bool> _nextStopMp3ByAnnouncementId = new();
    private readonly Dictionary<string, bool> _followingStopsByAnnouncementId = new();
    private string? _sequenceLoadedForAnnouncementId;

    private const double StandardPrefixLinkPauseSeconds = 1.0;

    public ObservableCollection<AnnouncementAudioSequenceItem> AnnouncementSequence { get; } = [];

    public string PickAudioButtonLabel =>
        AnnouncementSequence.Any(i => i.Kind == AnnouncementSequenceEntryKind.Audio)
            ? "Weitere Tondatei hinzufügen…"
            : "Tondatei wählen…";

    partial void OnIncludeGongInAnnouncementMergeChanged(bool value)
    {
        if (SelectedAnnouncement is not null)
        {
            _gongByAnnouncementId[SelectedAnnouncement.Id] = value;
            MarkAnnouncementAudioDirty(SelectedAnnouncement.Id);
        }

        MarkDirty();
        UpdateSelectedAudioHint();
    }

    partial void OnIncludeSondergongInAnnouncementMergeChanged(bool value)
    {
        if (SelectedAnnouncement is not null)
        {
            _sondergongByAnnouncementId[SelectedAnnouncement.Id] = value;
            MarkAnnouncementAudioDirty(SelectedAnnouncement.Id);
        }

        MarkDirty();
        UpdateSelectedAudioHint();
    }

    partial void OnIncludeNextStopInAnnouncementMergeChanged(bool value)
    {
        if (SelectedAnnouncement is not null)
        {
            _nextStopByAnnouncementId[SelectedAnnouncement.Id] = value;
            MarkAnnouncementAudioDirty(SelectedAnnouncement.Id);
        }

        MarkDirty();
        UpdateSelectedAudioHint();
    }

    partial void OnIncludeNextStopMp3InAnnouncementMergeChanged(bool value)
    {
        if (SelectedAnnouncement is not null)
        {
            _nextStopMp3ByAnnouncementId[SelectedAnnouncement.Id] = value;
            MarkAnnouncementAudioDirty(SelectedAnnouncement.Id);
        }

        MarkDirty();
        UpdateSelectedAudioHint();
    }

    partial void OnIncludeFollowingStopsInAnnouncementMergeChanged(bool value)
    {
        if (SelectedAnnouncement is not null)
        {
            _followingStopsByAnnouncementId[SelectedAnnouncement.Id] = value;
            MarkAnnouncementAudioDirty(SelectedAnnouncement.Id);
        }

        MarkDirty();
        UpdateSelectedAudioHint();
    }

    private void PersistCurrentAnnouncementSequence()
    {
        if (string.IsNullOrWhiteSpace(_sequenceLoadedForAnnouncementId))
        {
            return;
        }

        _sequenceByAnnouncementId[_sequenceLoadedForAnnouncementId] =
            AnnouncementSequence.Select(i => i.Clone()).ToList();
        _gongByAnnouncementId[_sequenceLoadedForAnnouncementId] = IncludeGongInAnnouncementMerge;
        _sondergongByAnnouncementId[_sequenceLoadedForAnnouncementId] = IncludeSondergongInAnnouncementMerge;
        _nextStopByAnnouncementId[_sequenceLoadedForAnnouncementId] = IncludeNextStopInAnnouncementMerge;
        _nextStopMp3ByAnnouncementId[_sequenceLoadedForAnnouncementId] = IncludeNextStopMp3InAnnouncementMerge;
        _followingStopsByAnnouncementId[_sequenceLoadedForAnnouncementId] =
            IncludeFollowingStopsInAnnouncementMerge;
    }

    private void LoadAnnouncementSequenceForSelection()
    {
        AnnouncementSequence.Clear();
        SelectedSequenceItem = null;
        _sequenceLoadedForAnnouncementId = SelectedAnnouncement?.Id;

        if (SelectedAnnouncement is null)
        {
            IncludeGongInAnnouncementMerge = false;
            IncludeSondergongInAnnouncementMerge = false;
            IncludeNextStopInAnnouncementMerge = false;
            IncludeNextStopMp3InAnnouncementMerge = false;
            IncludeFollowingStopsInAnnouncementMerge = false;
            return;
        }

        if (_sequenceByAnnouncementId.TryGetValue(SelectedAnnouncement.Id, out var stored))
        {
            foreach (var item in stored)
            {
                AnnouncementSequence.Add(item.Clone());
            }

            SelectedSequenceItem = AnnouncementSequence.Count > 0 ? AnnouncementSequence[0] : null;
        }
        else
        {
            TryBootstrapSequenceFromAnnouncement(SelectedAnnouncement);
        }

        IncludeGongInAnnouncementMerge =
            _gongByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        IncludeSondergongInAnnouncementMerge =
            _sondergongByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        IncludeNextStopInAnnouncementMerge =
            _nextStopByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        IncludeNextStopMp3InAnnouncementMerge =
            _nextStopMp3ByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        IncludeFollowingStopsInAnnouncementMerge =
            _followingStopsByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
    }

    private static string BuildPrefixSequenceLabel(
        bool includeGong,
        bool includeSondergong,
        bool includeNextStopGerman,
        bool includeNextStopMp3,
        bool includeFollowingStops)
    {
        var segments = new List<string>();
        if (includeGong)
        {
            segments.Add("Gong");
        }

        if (includeGong && includeSondergong)
        {
            segments.Add("1 s");
        }

        if (includeSondergong)
        {
            segments.Add("Sondergong");
        }

        var lastWasChime = includeGong || includeSondergong;
        if (lastWasChime && includeNextStopGerman)
        {
            segments.Add("1 s");
        }

        if (includeNextStopGerman)
        {
            segments.Add("Nächste Haltestelle");
        }

        if (includeNextStopGerman && includeNextStopMp3)
        {
            segments.Add("1 s");
        }

        if (includeNextStopMp3)
        {
            segments.Add("Next Stop");
        }

        var hadPriorPrefix =
            includeGong || includeSondergong || includeNextStopGerman || includeNextStopMp3;
        if (hadPriorPrefix && includeFollowingStops)
        {
            segments.Add("1 s");
        }

        if (includeFollowingStops)
        {
            segments.Add("Folgende Halte");
        }

        return segments.Count == 0 ? string.Empty : string.Join(" + ", segments) + " + ";
    }

    private void TryBootstrapSequenceFromAnnouncement(ManagedAnnouncementTemplateItem announcement)
    {
        if (!AnnouncementSequenceExport.IsLegacyMergedFileName(announcement.EmbeddedSoundFileName) &&
            (announcement.AnnouncementSequence.Count > 0 ||
             announcement.IncludeGong ||
             announcement.IncludeSondergong ||
             announcement.IncludeNextStopGerman ||
             announcement.IncludeNextStopMp3 ||
             announcement.IncludeFollowingStops))
        {
            _gongByAnnouncementId[announcement.Id] = announcement.IncludeGong;
            _sondergongByAnnouncementId[announcement.Id] = announcement.IncludeSondergong;
            _nextStopByAnnouncementId[announcement.Id] = announcement.IncludeNextStopGerman;
            _nextStopMp3ByAnnouncementId[announcement.Id] = announcement.IncludeNextStopMp3;
            _followingStopsByAnnouncementId[announcement.Id] = announcement.IncludeFollowingStops;

            var items = announcement.AnnouncementSequence.Select(entry => entry.Kind switch
            {
                AnnouncementExportEntryKind.Pause => new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Pause,
                    PauseSeconds = entry.PauseSeconds
                },
                AnnouncementExportEntryKind.Line => new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Line
                },
                AnnouncementExportEntryKind.Nach => new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Nach
                },
                AnnouncementExportEntryKind.RouteEndDestination => new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.RouteEndDestination
                },
                _ => new AnnouncementAudioSequenceItem
                {
                    Kind = AnnouncementSequenceEntryKind.Audio,
                    DisplayName = entry.FileName,
                    SourcePath = ResolveSequenceAudioPath(announcement, entry.FileName)
                }
            }).ToList();

            _sequenceByAnnouncementId[announcement.Id] = items;
            foreach (var item in items)
            {
                AnnouncementSequence.Add(item.Clone());
            }

            SelectedSequenceItem = AnnouncementSequence.Count > 0 ? AnnouncementSequence[0] : null;
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        string? path = null;
        if (!string.IsNullOrWhiteSpace(announcement.LocalAudioPath) &&
            File.Exists(announcement.LocalAudioPath))
        {
            path = announcement.LocalAudioPath;
        }
        else if (AppServices.IsInitialized)
        {
            path = PlanerAnnouncementRawSoundsWorkspace.ResolveAudioPath(
                AppServices.Workspace,
                announcement.LocalAudioPath,
                announcement.EmbeddedSoundFileName);
        }

        if (path is null && !string.IsNullOrWhiteSpace(announcement.EmbeddedSoundFileName))
        {
            if (AppServices.IsInitialized)
            {
                path = PlanerAnnouncementRawSoundsWorkspace.TryGetLocalFilePath(
                    AppServices.Workspace,
                    announcement.EmbeddedSoundFileName);
            }

            path ??= EmbeddedSoundPathResolver.TryResolveLocalPath(
                announcement.EmbeddedSoundFileName,
                editor.PackageRoot,
                AppServices.IsInitialized ? AppServices.Workspace : null);
        }

        if (path is null)
        {
            return;
        }

        var bootstrapped = new AnnouncementAudioSequenceItem
        {
            Kind = AnnouncementSequenceEntryKind.Audio,
            DisplayName = !string.IsNullOrWhiteSpace(announcement.EmbeddedSoundFileName)
                ? announcement.EmbeddedSoundFileName.Trim()
                : Path.GetFileName(path),
            SourcePath = path
        };
        // In Dictionary legen, sonst wirkt „Löschen“ nur in der UI und Bootstrap stellt den Ton wieder her.
        _sequenceByAnnouncementId[announcement.Id] = [bootstrapped.Clone()];
        AnnouncementSequence.Add(bootstrapped);
        SelectedSequenceItem = bootstrapped;
    }

    private string? ResolveSequenceAudioPath(ManagedAnnouncementTemplateItem announcement, string fileName)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return null;
        }

        if (AppServices.IsInitialized)
        {
            var workspacePath = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(
                AppServices.Workspace,
                fileName);
            if (workspacePath is not null)
            {
                return workspacePath;
            }

            workspacePath = PlanerAnnouncementRawSoundsWorkspace.TryGetLocalFilePath(
                AppServices.Workspace,
                fileName);
            if (workspacePath is not null)
            {
                return workspacePath;
            }
        }

        return EmbeddedSoundPathResolver.TryResolveLocalPath(
            fileName,
            editor.PackageRoot,
            AppServices.IsInitialized ? AppServices.Workspace : null);
    }

    private readonly HashSet<string> _announcementsNeedingAudioMaterialization = new(StringComparer.Ordinal);

    private void MarkAnnouncementAudioDirty(string? announcementId)
    {
        if (!string.IsNullOrWhiteSpace(announcementId))
        {
            _announcementsNeedingAudioMaterialization.Add(announcementId);
        }
    }

    private void NotifySequenceChanged()
    {
        MarkAnnouncementAudioDirty(SelectedAnnouncement?.Id);
        OnPropertyChanged(nameof(PickAudioButtonLabel));
        MarkDirty();
        UpdateSelectedAudioHint();
        ClearEmbeddedSoundCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddPauseToSequence()
    {
        if (SelectedAnnouncement is null)
        {
            StatusMessage = "Bitte zuerst eine Ansage auswählen.";
            return;
        }

        if (!TryParseMergePauseSeconds(AnnouncementMergePauseSeconds, out var pauseSeconds, out var pauseError))
        {
            StatusMessage = pauseError ?? "Pause ungültig.";
            return;
        }

        var pause = new AnnouncementAudioSequenceItem
        {
            Kind = AnnouncementSequenceEntryKind.Pause,
            PauseSeconds = pauseSeconds
        };

        InsertSequenceItem(pause);
        SelectedSequenceItem = pause;
        StatusMessage = $"Pause {pauseSeconds:0.###} s eingefügt.";
        NotifySequenceChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddGreetingLineDestinationPackage))]
    private void AddGreetingLineDestinationPackage()
    {
        if (SelectedAnnouncement is null)
        {
            StatusMessage = "Bitte zuerst eine Ansage auswählen.";
            return;
        }

        var packageItems = new[]
        {
            new AnnouncementAudioSequenceItem { Kind = AnnouncementSequenceEntryKind.Line },
            new AnnouncementAudioSequenceItem { Kind = AnnouncementSequenceEntryKind.Nach },
            new AnnouncementAudioSequenceItem { Kind = AnnouncementSequenceEntryKind.RouteEndDestination }
        };

        foreach (var item in packageItems)
        {
            InsertSequenceItem(item);
        }

        SelectedSequenceItem = packageItems[^1];
        StatusMessage = "Paket Linie → Nach → Endhaltestelle (Ziel) eingefügt.";
        NotifySequenceChanged();
    }

    private bool CanAddGreetingLineDestinationPackage() =>
        SelectedAnnouncement is not null &&
        StartStopGreetingResolver.MatchesPart1Template(SelectedAnnouncement);

    [RelayCommand]
    private void MoveSequenceItemUp()
    {
        MoveSelectedSequenceItem(-1);
    }

    [RelayCommand]
    private void MoveSequenceItemDown()
    {
        MoveSelectedSequenceItem(1);
    }

    private void MoveSelectedSequenceItem(int delta)
    {
        if (SelectedSequenceItem is null)
        {
            return;
        }

        var index = AnnouncementSequence.IndexOf(SelectedSequenceItem);
        var newIndex = index + delta;
        if (index < 0 || newIndex < 0 || newIndex >= AnnouncementSequence.Count)
        {
            return;
        }

        AnnouncementSequence.Move(index, newIndex);
        NotifySequenceChanged();
    }

    [RelayCommand]
    private void RemoveSequenceItem()
    {
        if (SelectedAnnouncement is null)
        {
            StatusMessage = "Bitte zuerst eine Ansage auswählen.";
            return;
        }

        // Einziger Eintrag / Fokusverlust der ListBox: trotzdem löschen.
        var target = SelectedSequenceItem;
        if (target is null && AnnouncementSequence.Count == 1)
        {
            target = AnnouncementSequence[0];
        }

        if (target is null)
        {
            StatusMessage = AnnouncementSequence.Count == 0
                ? "Sequenz ist bereits leer."
                : "Bitte zuerst einen Eintrag in der Sequenz wählen.";
            return;
        }

        var index = AnnouncementSequence.IndexOf(target);
        if (index < 0)
        {
            StatusMessage = "Bitte zuerst einen Eintrag in der Sequenz wählen.";
            return;
        }

        AnnouncementSequence.RemoveAt(index);
        SelectedSequenceItem = AnnouncementSequence.Count == 0
            ? null
            : AnnouncementSequence[Math.Min(index, AnnouncementSequence.Count - 1)];

        // Letzter Ton: gleicher Effekt wie ✕ – sonst erscheint der Eintrag sofort wieder
        // (Bootstrap aus EmbeddedSoundFileName).
        if (!AnnouncementSequence.Any(i => i.Kind == AnnouncementSequenceEntryKind.Audio))
        {
            ClearEmbeddedSound();
            StatusMessage = "Tonsequenz geleert – „Speichern & JSON“ übernimmt die Änderung.";
            return;
        }

        PersistCurrentAnnouncementSequence();
        NotifySequenceChanged();
        ClearEmbeddedSoundCommand.NotifyCanExecuteChanged();
        StatusMessage = "Eintrag aus der Sequenz entfernt.";
    }

    [RelayCommand]
    private void ReplaceSequenceItem()
    {
        if (SelectedSequenceItem is null)
        {
            StatusMessage = "Bitte zuerst einen Eintrag in der Liste wählen.";
            return;
        }

        if (SelectedSequenceItem.Kind == AnnouncementSequenceEntryKind.Pause)
        {
            StatusMessage = "Pause unten in Sekunden anpassen.";
            return;
        }

        var dialog = CreateAudioOpenFileDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedSequenceItem.DisplayName = Path.GetFileName(dialog.FileName);
        SelectedSequenceItem.SourcePath = dialog.FileName;
        NotifySequenceChanged();
        StatusMessage = $"Tondatei ersetzt: {SelectedSequenceItem.DisplayName}";
    }

    private void InsertSequenceItem(AnnouncementAudioSequenceItem item)
    {
        if (SelectedSequenceItem is null)
        {
            AnnouncementSequence.Add(item);
            return;
        }

        var index = AnnouncementSequence.IndexOf(SelectedSequenceItem);
        AnnouncementSequence.Insert(index + 1, item);
    }

    private OpenFileDialog CreateAudioOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Tondatei für Ansage wählen",
            Filter = "Audio (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|Alle Dateien (*.*)|*.*"
        };

        try
        {
            var exportDir = DropboxSyncFolderLocator.TryResolveHamblochExportFolder();
            if (!string.IsNullOrEmpty(exportDir))
            {
                dialog.InitialDirectory = exportDir;
            }
            else if (AppServices.IsInitialized)
            {
                dialog.InitialDirectory =
                    PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            }
        }
        catch
        {
            // Dateidialog ohne Startordner
        }

        return dialog;
    }

    private bool TryParseDefaultPauseSeconds(out double seconds, out string? error) =>
        TryParseMergePauseSeconds(AnnouncementMergePauseSeconds, out seconds, out error);

    private void AppendPickedAudioFile(string filePath)
    {
        if (SelectedAnnouncement is null)
        {
            return;
        }

        if (AnnouncementSequence.Any(i => i.Kind == AnnouncementSequenceEntryKind.Audio))
        {
            if (!TryParseDefaultPauseSeconds(out var pauseSeconds, out var pauseError))
            {
                StatusMessage = pauseError ?? "Pause ungültig.";
                return;
            }

            AnnouncementSequence.Add(new AnnouncementAudioSequenceItem
            {
                Kind = AnnouncementSequenceEntryKind.Pause,
                PauseSeconds = pauseSeconds
            });
        }

        var item = new AnnouncementAudioSequenceItem
        {
            Kind = AnnouncementSequenceEntryKind.Audio,
            DisplayName = Path.GetFileName(filePath),
            SourcePath = filePath
        };
        TryCopyStandardSoundToRawWorkspace(filePath);
        AnnouncementSequence.Add(item);
        SelectedSequenceItem = item;
        NotifySequenceChanged();

        StatusMessage = AnnouncementSequence.Count(i => i.Kind == AnnouncementSequenceEntryKind.Audio) == 1
            ? $"Tondatei „{item.DisplayName}“ hinzugefügt – mit „Speichern & JSON“ übernehmen."
            : $"{item.DisplayName} hinzugefügt – Reihenfolge in der Liste prüfen.";
    }

    private bool ApplyAnnouncementSequencesBeforeCommit()
    {
        AnnouncementPreviewPlayer.Stop();
        PersistCurrentAnnouncementSequence();

        foreach (var announcement in _allAnnouncements)
        {
            var needsMaterialization = _announcementsNeedingAudioMaterialization.Contains(announcement.Id);
            if (!needsMaterialization)
            {
                continue;
            }

            _sequenceByAnnouncementId.TryGetValue(announcement.Id, out var sequence);
            sequence ??= [];

            announcement.IncludeGong = _gongByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            announcement.IncludeSondergong =
                _sondergongByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            announcement.IncludeNextStopGerman =
                _nextStopByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            announcement.IncludeNextStopMp3 =
                _nextStopMp3ByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            announcement.IncludeFollowingStops =
                _followingStopsByAnnouncementId.GetValueOrDefault(announcement.Id, false);

            if (sequence.Count == 0 && !announcement.IncludeGong && !announcement.IncludeSondergong &&
                !announcement.IncludeNextStopGerman && !announcement.IncludeNextStopMp3 &&
                !announcement.IncludeFollowingStops)
            {
                announcement.AnnouncementSequence.Clear();
                // Sonst bleibt EmbeddedSound stehen → Bootstrap zeigt den Ton nach Speichern wieder.
                announcement.EmbeddedSoundFileName = string.Empty;
                announcement.LocalAudioPath = null;
                continue;
            }

            if (!TryExportSequenceMetadata(announcement, sequence, out var exportError))
            {
                StatusMessage = exportError ?? "Sequenz konnte nicht exportiert werden.";
                return false;
            }
        }

        return true;
    }

    private bool TryExportSequenceMetadata(
        ManagedAnnouncementTemplateItem announcement,
        IReadOnlyList<AnnouncementAudioSequenceItem> sequence,
        out string? error)
    {
        error = null;
        announcement.AnnouncementSequence.Clear();

        string? primaryFileName = null;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var audioIndex = 0;

        foreach (var item in sequence)
        {
            switch (item.Kind)
            {
                case AnnouncementSequenceEntryKind.Pause:
                    announcement.AnnouncementSequence.Add(new AnnouncementSequenceEntry
                    {
                        Kind = AnnouncementExportEntryKind.Pause,
                        PauseSeconds = item.PauseSeconds
                    });
                    break;
                case AnnouncementSequenceEntryKind.Line:
                    announcement.AnnouncementSequence.Add(new AnnouncementSequenceEntry
                    {
                        Kind = AnnouncementExportEntryKind.Line
                    });
                    break;
                case AnnouncementSequenceEntryKind.Nach:
                    announcement.AnnouncementSequence.Add(new AnnouncementSequenceEntry
                    {
                        Kind = AnnouncementExportEntryKind.Nach
                    });
                    break;
                case AnnouncementSequenceEntryKind.RouteEndDestination:
                    announcement.AnnouncementSequence.Add(new AnnouncementSequenceEntry
                    {
                        Kind = AnnouncementExportEntryKind.RouteEndDestination
                    });
                    break;
                case AnnouncementSequenceEntryKind.Audio:
                    var audioPath = AppServices.IsInitialized
                        ? PlanerAnnouncementRawSoundsWorkspace.ResolveAudioPath(
                            AppServices.Workspace,
                            item.SourcePath,
                            item.DisplayName)
                        : item.SourcePath;
                    if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
                    {
                        error = $"Tondatei „{item.DisplayName}“ nicht gefunden.";
                        return false;
                    }

                    item.SourcePath = audioPath;
                    var fileName = BuildSequenceAudioFileName(announcement, item, audioIndex, usedNames);
                    audioIndex++;
                    usedNames.Add(fileName);

                    announcement.AnnouncementSequence.Add(new AnnouncementSequenceEntry
                    {
                        Kind = AnnouncementExportEntryKind.Audio,
                        FileName = fileName
                    });

                    if (!StageSequenceAudioFile(announcement, fileName, audioPath, out error))
                    {
                        return false;
                    }

                    primaryFileName ??= fileName;
                    break;
            }
        }

        if (!TryStageStandardAnnouncementSounds(announcement, out error))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(primaryFileName))
        {
            announcement.EmbeddedSoundFileName = primaryFileName;
            // LocalAudioPath zeigt sonst auf den zuletzt gestagten Clip (z. B. Final Stop)
            // und SyncEmbeddedSoundsFromTemplates überschreibt damit die Hauptdatei (Fahrtende).
            announcement.LocalAudioPath = AppServices.IsInitialized
                ? PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(
                    AppServices.Workspace,
                    primaryFileName)
                : null;
        }
        else
        {
            announcement.EmbeddedSoundFileName = string.Empty;
            announcement.LocalAudioPath = null;
        }

        if (!announcement.IncludeSondergong)
        {
            announcement.SondergongFileName = string.Empty;
        }

        return true;
    }

    private static string BuildSequenceAudioFileName(
        ManagedAnnouncementTemplateItem announcement,
        AnnouncementAudioSequenceItem item,
        int audioIndex,
        ISet<string> usedNames)
    {
        var ext = Path.GetExtension(item.DisplayName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = Path.GetExtension(item.SourcePath);
        }

        if (string.IsNullOrEmpty(ext))
        {
            ext = ".wav";
        }

        var baseName = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(item.DisplayName)
                ? Path.GetFileName(item.SourcePath ?? "ansage")
                : item.DisplayName);
        // Code-Präfix der Kartei (z. B. 0091_) – Suffix _2 nur bei echtem Namenskollision,
        // nicht bei jedem weiteren Sequenz-Baustein (sonst „Final Stop“ → „Final Stop_2“).
        var fileName = BuildEmbeddedFileNameForAnnouncement(announcement, baseName, ext);
        var attempt = Math.Max(2, audioIndex + 1);
        while (usedNames.Contains(fileName))
        {
            fileName = BuildEmbeddedFileNameForAnnouncement(announcement, $"{baseName}_{attempt}", ext);
            attempt++;
        }

        return fileName;
    }

    private bool StageSequenceAudioFile(
        ManagedAnnouncementTemplateItem announcement,
        string fileName,
        string sourcePath,
        out string? error)
    {
        var previousName = announcement.EmbeddedSoundFileName;
        var previousLocalPath = announcement.LocalAudioPath;
        announcement.EmbeddedSoundFileName = fileName;
        var ok = StageAnnouncementAudio(announcement, sourcePath);
        // LocalAudioPath wiederherstellen – StageAnnouncementAudio setzt ihn auf diesen Clip;
        // sonst überschreibt der letzte Clip später die Hauptdatei beim Speichern.
        announcement.EmbeddedSoundFileName = previousName;
        announcement.LocalAudioPath = previousLocalPath;
        if (!ok)
        {
            error = StatusMessage;
            return false;
        }

        error = null;
        return true;
    }

    private bool TryStageStandardAnnouncementSounds(
        ManagedAnnouncementTemplateItem announcement,
        out string? error)
    {
        error = null;
        if (!AppServices.IsInitialized)
        {
            if (announcement.IncludeGong || announcement.IncludeSondergong ||
                announcement.IncludeNextStopGerman || announcement.IncludeNextStopMp3 ||
                announcement.IncludeFollowingStops)
            {
                error = "Workspace nicht initialisiert – Standard-Ansagen können nicht eingebettet werden.";
                return false;
            }

            return true;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            error = "Kein Route-Paket geladen.";
            return false;
        }

        if (announcement.IncludeGong &&
            !TryStageStandardSound(editor, PlanerGongSoundResolver.TryResolve(AppServices.Workspace, ActiveDropboxFolderPath),
                PlanerGongSoundResolver.GongFileName, out error))
        {
            return false;
        }

        if (announcement.IncludeSondergong)
        {
            var settings = AppServices.PlanerAppSettings?.Load();
            var fileName = PlanerSondergongSoundResolver.ConfiguredFileName(settings);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Sondergong aktiv, aber keine Datei unter Einstellungen hinterlegt.";
                return false;
            }

            announcement.SondergongFileName = fileName.Trim();

            if (!TryStageStandardSound(
                    editor,
                    PlanerSondergongSoundResolver.TryResolve(
                        AppServices.Workspace,
                        settings,
                        AppServices.SettingsSubfolder,
                        ActiveDropboxFolderPath),
                    fileName,
                    out error))
            {
                return false;
            }
        }

        if (announcement.IncludeNextStopGerman &&
            !TryStageStandardSound(editor, PlanerNextStopSoundResolver.TryResolve(AppServices.Workspace, ActiveDropboxFolderPath),
                PlanerNextStopSoundResolver.FileName, out error))
        {
            return false;
        }

        if (announcement.IncludeNextStopMp3)
        {
            var nextStopPath = PlanerNextStopMp3SoundResolver.TryResolve(
                AppServices.Workspace,
                ActiveDropboxFolderPath);
            var nextStopFileName = nextStopPath is not null
                ? Path.GetFileName(nextStopPath)
                : PlanerNextStopMp3SoundResolver.FileName;
            if (!TryStageStandardSound(editor, nextStopPath, nextStopFileName, out error))
            {
                return false;
            }
        }

        if (announcement.IncludeFollowingStops)
        {
            var followingPath = PlanerFollowingStopsSoundResolver.TryResolve(
                AppServices.Workspace,
                ActiveDropboxFolderPath);
            var followingFileName = followingPath is not null
                ? Path.GetFileName(followingPath)
                : PlanerFollowingStopsSoundResolver.FileName;
            if (!TryStageStandardSound(editor, followingPath, followingFileName, out error))
            {
                return false;
            }
        }

        if (announcement.AnnouncementSequence.Any(e => e.Kind == AnnouncementExportEntryKind.Nach) &&
            !TryStageStandardSound(editor, PlanerNachSoundResolver.TryResolve(AppServices.Workspace, ActiveDropboxFolderPath),
                PlanerNachSoundResolver.FileName, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryStageStandardSound(
        EditableRoutePackage editor,
        string? sourcePath,
        string fileName,
        out string? error)
    {
        error = null;
        if (sourcePath is null)
        {
                error = string.Equals(fileName, PlanerNachSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, PlanerUndSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, PlanerBusDrivesViaSoundResolver.FileName, StringComparison.OrdinalIgnoreCase)
                    ? $"„{fileName}“ nicht gefunden. " +
                      "Unter Ansagen → Spezialbausteine zuweisen, oder in „Ansagen-Rohdateien“ ablegen."
                    : $"„{fileName}“ nicht gefunden – bitte unter Ansagen → Spezialbausteine zuweisen " +
                      "oder in Dropbox/Verkehrsbetrieb Hambloch/Ansagen ablegen.";
                return false;
        }

        if (!AppServices.IsInitialized)
        {
            error = "Workspace nicht initialisiert.";
            return false;
        }

        try
        {
            AnnouncementPreviewPlayer.Stop();

            var target = Path.Combine(
                PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace),
                fileName.Trim());
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var sourceFullPath = Path.GetFullPath(sourcePath);
            var targetFullPath = Path.GetFullPath(target);
            if (!string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, target, overwrite: true);
            }

            EmbeddedSoundsEditor.UpsertFromFile(editor.PackageRoot, fileName.Trim(), target);
        }
        catch (Exception ex)
        {
            error = $"Standard-Ansage „{fileName}“ konnte nicht eingebettet werden: {ex.Message}";
            return false;
        }

        return true;
    }

    private bool TryMaterializeAnnouncementAudio(
        ManagedAnnouncementTemplateItem announcement,
        IReadOnlyList<EmbeddedSoundSequencePart> parts,
        out string? error)
    {
        error = null;
        var audioParts = parts.Where(p => p.Kind == EmbeddedSoundSequencePartKind.Audio).ToList();
        if (audioParts.Count == 0)
        {
            error = $"Ansage {announcement.AnnouncementCode}: Keine Tondatei in der Sequenz.";
            return false;
        }

        if (audioParts.Count == 1 && parts.Count == 1)
        {
            var singlePath = audioParts[0].AudioPath!;
            var ext = Path.GetExtension(singlePath);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".wav";
            }

            announcement.EmbeddedSoundFileName = BuildEmbeddedFileNameForAnnouncement(
                announcement,
                Path.GetFileNameWithoutExtension(singlePath),
                ext);
            if (!StageAnnouncementAudio(announcement, singlePath))
            {
                error = StatusMessage;
                return false;
            }

            return true;
        }

        if (!AppServices.IsInitialized)
        {
            error = "Workspace nicht initialisiert.";
            return false;
        }

        var outputFileName = BuildMergedAnnouncementFileName(announcement);
        var outputPath = Path.Combine(
            PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace),
            outputFileName);

        try
        {
            EmbeddedSoundConcatenator.ConcatenateSequenceToWav(parts, outputPath);
        }
        catch (Exception ex)
        {
            error = $"Zusammenfügen fehlgeschlagen: {ex.Message}";
            return false;
        }

        announcement.EmbeddedSoundFileName = outputFileName;
        if (!StageAnnouncementAudio(announcement, outputPath))
        {
            error = StatusMessage;
            return false;
        }

        return true;
    }

    private bool TryBuildSequenceParts(
        IReadOnlyList<AnnouncementAudioSequenceItem> sequence,
        bool includeGong,
        bool includeSondergong,
        bool includeNextStopGerman,
        bool includeNextStopMp3,
        bool includeFollowingStops,
        out List<EmbeddedSoundSequencePart> parts,
        out string? error)
    {
        parts = [];
        error = null;

        if (includeGong || includeSondergong || includeNextStopGerman || includeNextStopMp3 ||
            includeFollowingStops)
        {
            if (!AppServices.IsInitialized)
            {
                error = "Workspace nicht initialisiert – Standard-Ansagen können nicht geladen werden.";
                return false;
            }
        }

        if (includeGong)
        {
            if (!TryAddStandardAudio(
                    PlanerGongSoundResolver.TryResolve(AppServices.Workspace, ActiveDropboxFolderPath),
                    PlanerGongSoundResolver.GongFileName,
                    parts,
                    out error))
            {
                return false;
            }
        }

        if (includeGong && includeSondergong)
        {
            AddStandardPause(parts);
        }

        if (includeSondergong)
        {
            var settings = AppServices.PlanerAppSettings?.Load();
            var fileName = PlanerSondergongSoundResolver.ConfiguredFileName(settings);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Sondergong aktiv, aber keine Datei unter Einstellungen hinterlegt.";
                return false;
            }

            if (!TryAddStandardAudio(
                    PlanerSondergongSoundResolver.TryResolve(
                        AppServices.Workspace,
                        settings,
                        AppServices.SettingsSubfolder,
                        ActiveDropboxFolderPath),
                    fileName,
                    parts,
                    out error))
            {
                return false;
            }
        }

        var lastWasChime = includeGong || includeSondergong;
        if (lastWasChime && includeNextStopGerman)
        {
            AddStandardPause(parts);
        }

        if (includeNextStopGerman)
        {
            if (!TryAddStandardAudio(
                    PlanerNextStopSoundResolver.TryResolve(AppServices.Workspace, ActiveDropboxFolderPath),
                    PlanerNextStopSoundResolver.FileName,
                    parts,
                    out error))
            {
                return false;
            }
        }

        if (includeNextStopGerman && includeNextStopMp3)
        {
            AddStandardPause(parts);
        }

        if (includeNextStopMp3)
        {
            var nextStopPath = PlanerNextStopMp3SoundResolver.TryResolve(
                AppServices.Workspace,
                ActiveDropboxFolderPath);
            var nextStopLabel = nextStopPath is not null
                ? Path.GetFileName(nextStopPath)
                : PlanerNextStopMp3SoundResolver.FileName;
            if (!TryAddStandardAudio(nextStopPath, nextStopLabel, parts, out error))
            {
                return false;
            }
        }

        var hadPriorPrefix =
            includeGong || includeSondergong || includeNextStopGerman || includeNextStopMp3;
        if (hadPriorPrefix && includeFollowingStops)
        {
            AddStandardPause(parts);
        }

        if (includeFollowingStops)
        {
            var followingPath = PlanerFollowingStopsSoundResolver.TryResolve(
                AppServices.Workspace,
                ActiveDropboxFolderPath);
            var followingLabel = followingPath is not null
                ? Path.GetFileName(followingPath)
                : PlanerFollowingStopsSoundResolver.FileName;
            if (!TryAddStandardAudio(followingPath, followingLabel, parts, out error))
            {
                return false;
            }
        }

        foreach (var item in sequence)
        {
            switch (item.Kind)
            {
                case AnnouncementSequenceEntryKind.Audio:
                    var audioPath = AppServices.IsInitialized
                        ? PlanerAnnouncementRawSoundsWorkspace.ResolveAudioPath(
                            AppServices.Workspace,
                            item.SourcePath,
                            item.DisplayName)
                        : item.SourcePath;
                    if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
                    {
                        error = $"Tondatei „{item.DisplayName}“ nicht gefunden.";
                        return false;
                    }

                    item.SourcePath = audioPath;
                    parts.Add(new EmbeddedSoundSequencePart
                    {
                        Kind = EmbeddedSoundSequencePartKind.Audio,
                        AudioPath = audioPath
                    });
                    break;
                case AnnouncementSequenceEntryKind.Pause:
                    parts.Add(new EmbeddedSoundSequencePart
                    {
                        Kind = EmbeddedSoundSequencePartKind.Pause,
                        Pause = TimeSpan.FromSeconds(item.PauseSeconds)
                    });
                    break;
                case AnnouncementSequenceEntryKind.Line:
                case AnnouncementSequenceEntryKind.Nach:
                case AnnouncementSequenceEntryKind.RouteEndDestination:
                    break;
            }
        }

        return true;
    }

    private static void AddStandardPause(List<EmbeddedSoundSequencePart> parts)
    {
        parts.Add(new EmbeddedSoundSequencePart
        {
            Kind = EmbeddedSoundSequencePartKind.Pause,
            Pause = TimeSpan.FromSeconds(StandardPrefixLinkPauseSeconds)
        });
    }

    private static bool TryAddStandardAudio(
        string? path,
        string fileName,
        List<EmbeddedSoundSequencePart> parts,
        out string? error)
    {
        error = null;
        if (path is null)
        {
            error =
                $"„{fileName}“ nicht gefunden – bitte unter Dropbox/Verkehrsbetrieb Hambloch/Ansagen ablegen.";
            return false;
        }

        parts.Add(new EmbeddedSoundSequencePart
        {
            Kind = EmbeddedSoundSequencePartKind.Audio,
            AudioPath = path
        });
        return true;
    }

    private static string BuildEmbeddedFileNameForAnnouncement(
        ManagedAnnouncementTemplateItem announcement,
        string baseName,
        string ext)
    {
        var code = ManagedAnnouncementTemplateItem.NormalizeCode(announcement.AnnouncementCode);
        if (code.Length != 4)
        {
            code = "0000";
        }

        var safeBase = NormalizeEmbeddedSoundBaseName(baseName, code);
        return $"{code}_{safeBase}{ext}";
    }

    private static string NormalizeEmbeddedSoundBaseName(string baseName, string code)
    {
        var safeBase = string.IsNullOrWhiteSpace(baseName) ? "ansage" : baseName.Trim().TrimStart('_');
        var prefix = $"{code}_";
        while (safeBase.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            safeBase = safeBase[prefix.Length..].TrimStart('_');
        }

        return string.IsNullOrWhiteSpace(safeBase) ? "ansage" : safeBase;
    }

    private bool TryBuildPreviewPartsForSelection(
        out List<EmbeddedSoundSequencePart> parts,
        out string? error)
    {
        PersistCurrentAnnouncementSequence();
        parts = [];

        if (SelectedAnnouncement is null)
        {
            error = "Bitte zuerst eine Ansage auswählen.";
            return false;
        }

        _sequenceByAnnouncementId.TryGetValue(SelectedAnnouncement.Id, out var sequence);
        sequence ??= [];

        var includeGong = _gongByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        var includeSondergong = _sondergongByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        var includeNextStopGerman = _nextStopByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        var includeNextStopMp3 = _nextStopMp3ByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        var includeFollowingStops =
            _followingStopsByAnnouncementId.GetValueOrDefault(SelectedAnnouncement.Id, false);
        if (sequence.Count == 0 && !includeGong && !includeSondergong && !includeNextStopGerman &&
            !includeNextStopMp3 && !includeFollowingStops)
        {
            error = "Keine Tondateien in der Sequenz – bitte Tondatei hinzufügen.";
            return false;
        }

        return TryBuildSequenceParts(
            sequence,
            includeGong,
            includeSondergong,
            includeNextStopGerman,
            includeNextStopMp3,
            includeFollowingStops,
            out parts,
            out error);
    }

    private static string? ActiveDropboxFolderPath =>
        BetriebProfileStore.GetActiveProfile()?.DropboxFolderPath;

    private static void TryCopyStandardSoundToRawWorkspace(string pickedFilePath)
    {
        if (!AppServices.IsInitialized || !File.Exists(pickedFilePath))
        {
            return;
        }

        var fileName = Path.GetFileName(pickedFilePath).Trim();
        if (string.IsNullOrWhiteSpace(fileName) || !IsKnownStandardAnnouncementFileName(fileName))
        {
            return;
        }

        try
        {
            var target = Path.Combine(
                PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace),
                fileName);
            if (File.Exists(target))
            {
                return;
            }

            File.Copy(pickedFilePath, target);
        }
        catch
        {
            // Rohordner optional – Speichern versucht weitere Pfade
        }
    }

    private static bool IsKnownStandardAnnouncementFileName(string fileName) =>
        string.Equals(fileName, PlanerNachSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerUndSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerGongSoundResolver.GongFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerNextStopSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerNextStopMp3SoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerFollowingStopsSoundResolver.FileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, PlanerBusDrivesViaSoundResolver.FileName, StringComparison.OrdinalIgnoreCase);
}
