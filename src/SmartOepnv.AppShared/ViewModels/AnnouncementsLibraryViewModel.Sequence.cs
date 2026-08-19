using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Models;
using SmartOepnv.Core;
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
            return;
        }

        if (_sequenceByAnnouncementId.TryGetValue(SelectedAnnouncement.Id, out var stored))
        {
            foreach (var item in stored)
            {
                AnnouncementSequence.Add(item.Clone());
            }
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
    }

    private static string BuildPrefixSequenceLabel(
        bool includeGong,
        bool includeSondergong,
        bool includeNextStopGerman,
        bool includeNextStopMp3)
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

        return segments.Count == 0 ? string.Empty : string.Join(" + ", segments) + " + ";
    }

    private void TryBootstrapSequenceFromAnnouncement(ManagedAnnouncementTemplateItem announcement)
    {
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

        AnnouncementSequence.Add(new AnnouncementAudioSequenceItem
        {
            Kind = AnnouncementSequenceEntryKind.Audio,
            DisplayName = !string.IsNullOrWhiteSpace(announcement.EmbeddedSoundFileName)
                ? announcement.EmbeddedSoundFileName.Trim()
                : Path.GetFileName(path),
            SourcePath = path
        });
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
        if (SelectedSequenceItem is null)
        {
            return;
        }

        var index = AnnouncementSequence.IndexOf(SelectedSequenceItem);
        if (index < 0)
        {
            return;
        }

        AnnouncementSequence.RemoveAt(index);
        SelectedSequenceItem = AnnouncementSequence.Count == 0
            ? null
            : AnnouncementSequence[Math.Min(index, AnnouncementSequence.Count - 1)];
        NotifySequenceChanged();
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
        AnnouncementSequence.Add(item);
        SelectedSequenceItem = item;
        NotifySequenceChanged();

        StatusMessage = AnnouncementSequence.Count(i => i.Kind == AnnouncementSequenceEntryKind.Audio) == 1
            ? $"Tondatei „{item.DisplayName}“ hinzugefügt – mit „Speichern & JSON“ zusammenfügen."
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

            var includeGong = _gongByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            var includeSondergong = _sondergongByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            var includeNextStopGerman = _nextStopByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            var includeNextStopMp3 = _nextStopMp3ByAnnouncementId.GetValueOrDefault(announcement.Id, false);
            if (sequence.Count == 0 && !includeGong && !includeSondergong && !includeNextStopGerman &&
                !includeNextStopMp3)
            {
                continue;
            }

            if (!TryBuildSequenceParts(
                    sequence,
                    includeGong,
                    includeSondergong,
                    includeNextStopGerman,
                    includeNextStopMp3,
                    out var parts,
                    out var buildError))
            {
                StatusMessage = buildError ?? "Sequenz konnte nicht aufgebaut werden.";
                return false;
            }

            if (parts.Count == 0)
            {
                continue;
            }

            if (!TryMaterializeAnnouncementAudio(announcement, parts, out var materializeError))
            {
                StatusMessage = materializeError ?? "Ansage konnte nicht erzeugt werden.";
                return false;
            }
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
        out List<EmbeddedSoundSequencePart> parts,
        out string? error)
    {
        parts = [];
        error = null;

        if (includeGong || includeSondergong || includeNextStopGerman || includeNextStopMp3)
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
                    PlanerGongSoundResolver.TryResolve(AppServices.Workspace),
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
                        AppServices.SettingsSubfolder),
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
                    PlanerNextStopSoundResolver.TryResolve(AppServices.Workspace),
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
            if (!TryAddStandardAudio(
                    PlanerNextStopMp3SoundResolver.TryResolve(AppServices.Workspace),
                    PlanerNextStopMp3SoundResolver.FileName,
                    parts,
                    out error))
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
        if (sequence.Count == 0 && !includeGong && !includeSondergong && !includeNextStopGerman &&
            !includeNextStopMp3)
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
            out parts,
            out error);
    }
}
