using System.Collections.ObjectModel;

using System.IO;

using System.Text.Json;

using System.Text.Json.Nodes;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using System.Windows;

using SmartOepnv.AppShared.Helpers;

using SmartOepnv.AppShared.Models;

using SmartOepnv.AppShared.Views;

using SmartOepnv.Core;

using SmartOepnv.Core.RoutePackage;



namespace SmartOepnv.AppShared.ViewModels;



public partial class AnnouncementsLibraryViewModel : ObservableObject, IEditorAreaViewModel

{

    private readonly List<ManagedStopTemplateItem> _allStops = [];

    private readonly List<ManagedAnnouncementTemplateItem> _allAnnouncements = [];

    private readonly EditorAreaSyncState _sync = new();

    private readonly SearchQueryDebouncer _searchDebouncer;

    private string? _loadedFingerprint;

    private string? _lastAppliedStopTemplatesFingerprint;

    private const int SuccessFeedbackMs = 5000;

    private CancellationTokenSource? _saveButtonFeedbackCts;



    public ObservableCollection<string> Categories { get; } =

    [

        "haltestelle",

        "sonder",

        "sonstiges"

    ];



    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";

    [ObservableProperty] private string searchQuery = string.Empty;

    [ObservableProperty] private ManagedAnnouncementTemplateItem? selectedAnnouncement;

    [ObservableProperty] private string? selectedAnnouncementAudioHint;

    [ObservableProperty] private bool saveJsonButtonIsSuccess;

    public string SelectedEmbeddedSoundFileName =>
        SelectedAnnouncement?.EmbeddedSoundFileName?.Trim() ?? string.Empty;

    [ObservableProperty] private string announcementMergePauseSeconds = "0,5";

    [ObservableProperty] private bool includeGongInAnnouncementMerge;

    [ObservableProperty] private bool includeSondergongInAnnouncementMerge;

    [ObservableProperty] private bool includeNextStopInAnnouncementMerge;

    [ObservableProperty] private bool includeNextStopMp3InAnnouncementMerge;

    [ObservableProperty] private AnnouncementAudioSequenceItem? selectedSequenceItem;



    public ObservableCollection<ManagedAnnouncementTemplateItem> FilteredAnnouncements { get; } = [];

    public bool HasSelectedSequencePauseItem =>
        SelectedSequenceItem?.Kind == AnnouncementSequenceEntryKind.Pause;

    public IReadOnlyList<string> AnnouncementMergePausePresets { get; } = ["0,5", "1", "2", "3"];



    public AnnouncementsLibraryViewModel()

    {

        _searchDebouncer = new SearchQueryDebouncer(ApplyFilter);

        if (AppServices.IsInitialized)

        {

            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);

        }

    }



    public bool HasPendingChanges => _sync.HasPendingChanges;



    public void RefreshFromEditorIfNeeded()

    {

        if (!_sync.ShouldRefresh(_allAnnouncements.Count > 0))

        {

            return;

        }



        RefreshFromEditorCore();

    }



    public void RefreshFromEditor() => RefreshFromEditorCore();



    private void RefreshFromEditorCore()

    {

        _allStops.Clear();

        _allAnnouncements.Clear();

        _sequenceByAnnouncementId.Clear();

        _gongByAnnouncementId.Clear();

        _sondergongByAnnouncementId.Clear();

        _nextStopByAnnouncementId.Clear();

        _nextStopMp3ByAnnouncementId.Clear();

        _announcementsNeedingAudioMaterialization.Clear();

        _lastAppliedStopTemplatesFingerprint = null;

        _sequenceLoadedForAnnouncementId = null;

        AnnouncementSequence.Clear();

        FilteredAnnouncements.Clear();

        SelectedAnnouncement = null;

        SelectedAnnouncementAudioHint = null;



        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";

            return;

        }



        foreach (var stop in editor.StopTemplates)

        {

            if (stop.IsEmptyDraft())

            {

                continue;

            }



            _allStops.Add(CloneStop(stop));

        }



        foreach (var template in editor.AnnouncementTemplates)

        {

            var clone = CloneAnnouncement(template);

            AttachWorkspaceAudioPath(clone, editor.PackageRoot);

            _allAnnouncements.Add(clone);

        }



        EnsureStopCodesAndLinks();

        EnsureAnnouncementsFromStopTemplates(editor.PackageRoot);

        ApplyFilter();

        SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();

        UpdateSelectedAudioHint();

        StatusMessage = BuildListStatusMessage();

        _sync.AfterRefresh();

        _loadedFingerprint = ComputeFingerprint();

    }



    public void CommitChangesIfDirty()

    {

        var fingerprint = ComputeFingerprint();

        if (!_sync.ShouldCommit(fingerprint, _loadedFingerprint))

        {

            return;

        }



        if (!CommitChanges())

        {

            return;

        }

    }



    private string ComputeFingerprint()

    {

        PersistCurrentAnnouncementSequence();

        return JsonSerializer.Serialize(new

        {

            Stops = _allStops.Select(s => new

            {

                s.Id,

                s.StopCode,

                s.StopNameItcs,

                s.EmbeddedSoundFileName,

                s.LocalAudioPath

            }),

            Announcements = _allAnnouncements.Select(a => new

            {

                a.Id,

                a.StopTemplateId,

                a.AnnouncementCode,

                a.DisplayName,

                a.Description,

                a.Lines,

                a.Category,

                a.EmbeddedSoundFileName,

                a.IncludeInSpecialAnnouncements,

                a.LocalAudioPath

            }),

            Sequences = _sequenceByAnnouncementId.ToDictionary(

                kv => kv.Key,

                kv => kv.Value.Select(i => new

                {

                    i.Kind,

                    i.DisplayName,

                    i.SourcePath,

                    i.PauseSeconds

                }).ToList()),

            GongFlags = _gongByAnnouncementId,

            SondergongFlags = _sondergongByAnnouncementId,

            NextStopFlags = _nextStopByAnnouncementId,

            NextStopMp3Flags = _nextStopMp3ByAnnouncementId

        });

    }



    private void MarkDirty()
    {
        _sync.MarkDirty();
        if (SaveJsonButtonIsSuccess)
        {
            CancelSaveButtonFeedback();
            SaveJsonButtonIsSuccess = false;
        }
    }

    private async Task ShowSaveSuccessFeedbackAsync()
    {
        _saveButtonFeedbackCts?.Cancel();
        _saveButtonFeedbackCts?.Dispose();
        _saveButtonFeedbackCts = new CancellationTokenSource();
        var token = _saveButtonFeedbackCts.Token;

        try
        {
            await Task.Delay(SuccessFeedbackMs, token).ConfigureAwait(true);
            SaveJsonButtonIsSuccess = false;
        }
        catch (TaskCanceledException)
        {
            // neuer Speichervorgang oder Bearbeitung hat Feedback zurückgesetzt
        }
    }

    private void CancelSaveButtonFeedback()
    {
        _saveButtonFeedbackCts?.Cancel();
        _saveButtonFeedbackCts?.Dispose();
        _saveButtonFeedbackCts = null;
    }

    private void RefreshSelectedAnnouncementDisplay()
    {
        OnPropertyChanged(nameof(SelectedEmbeddedSoundFileName));
    }



    public bool CommitChanges()

    {

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            StatusMessage = "Kein Route-Paket geladen.";

            return false;

        }



        EnsureStopNamesBeforeCommit();

        PruneEmptyDraftStops();

        if (!ApplyAnnouncementSequencesBeforeCommit())

        {

            return false;

        }

        EnsureStopCodesAndLinks();

        SyncStopMetadataFromAnnouncements();

        EnsureAnnouncementDisplayNamesBeforeCommit();

        var validation = ValidateAll(editor.PackageRoot);

        if (validation is not null)

        {

            StatusMessage = validation;

            return false;

        }



        var stopClones = _allStops.Where(s => !s.IsEmptyDraft()).Select(CloneStop).ToList();

        var announcementClones = _allAnnouncements.Select(CloneAnnouncement).ToList();

        editor.ReplaceStopTemplates(stopClones);

        var stopFingerprint = StopTemplateRouteMerger.ComputeApplyFingerprint(stopClones);

        var routeStopsUpdated = 0;

        if (!string.Equals(stopFingerprint, _lastAppliedStopTemplatesFingerprint, StringComparison.Ordinal))

        {

            routeStopsUpdated = StopTemplateRouteMerger.ApplyTemplatesToRouteStops(editor, stopClones);

            _lastAppliedStopTemplatesFingerprint = stopFingerprint;

        }

        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;

        var needsAudio = _announcementsNeedingAudioMaterialization.Count > 0 ||

            editor.NeedsEmbeddedSoundMaterialization(stopClones, workspace) ||

            editor.NeedsEmbeddedSoundMaterialization(announcementClones, workspace);

        if (needsAudio)

        {

            editor.SyncEmbeddedSoundsFromStopTemplates(stopClones, workspace);

            editor.SyncEmbeddedSoundsFromTemplates(announcementClones, workspace);

        }

        editor.ReplaceAnnouncementTemplates(announcementClones);

        // Audio-Rebuild nur wenn ein Ton neu eingebettet werden muss (sonst Sidecar belassen).

        AppServices.Routes.ApplyEditorChanges("ansagen", rebuildEmbeddedMedia: needsAudio);



        foreach (var t in _allAnnouncements)

        {

            t.LocalAudioPath = null;

            AttachWorkspaceAudioPath(t, editor.PackageRoot);

        }



        RefreshAnnouncementListLabels(rebuildFilter: false);

        UpdateSelectedAudioHint();

        RefreshSelectedAnnouncementDisplay();

        StatusMessage = routeStopsUpdated > 0

            ? $"{_allAnnouncements.Count} Ansagen gespeichert – {routeStopsUpdated} Haltestelle(n) in Routen aktualisiert (Ton, Name …)."

            : $"{_allAnnouncements.Count} Ansagen lokal gespeichert – Dropbox-Upload erfolgt beim Beenden.";

        SaveJsonButtonIsSuccess = true;

        _ = ShowSaveSuccessFeedbackAsync();

        _sequenceByAnnouncementId.Clear();

        _gongByAnnouncementId.Clear();

        _sondergongByAnnouncementId.Clear();

        _nextStopByAnnouncementId.Clear();

        _nextStopMp3ByAnnouncementId.Clear();

        _announcementsNeedingAudioMaterialization.Clear();

        _sync.AfterCommit();

        _loadedFingerprint = ComputeFingerprint();

        LoadAnnouncementSequenceForSelection();

        return true;

    }



    partial void OnSearchQueryChanged(string value) => _searchDebouncer.Schedule();

    partial void OnSelectedAnnouncementChanged(ManagedAnnouncementTemplateItem? value)
    {
        PersistCurrentAnnouncementSequence();

        if (value is not null)
        {
            value.AnnouncementCode = ManagedAnnouncementTemplateItem.NormalizeCode(value.AnnouncementCode);
        }

        LoadAnnouncementSequenceForSelection();
        OnPropertyChanged(nameof(PickAudioButtonLabel));
        RefreshSelectedAnnouncementDisplay();
        UpdateSelectedAudioHint();
        ClearEmbeddedSoundCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSequenceItemChanged(AnnouncementAudioSequenceItem? value) =>
        OnPropertyChanged(nameof(HasSelectedSequencePauseItem));






    [RelayCommand]

    private void AddAnnouncement() => AddAnnouncementInternal(promptForAudio: false);



    [RelayCommand]

    private void AddAnnouncementWithAudio() => AddAnnouncementInternal(promptForAudio: true);



    private void AddAnnouncementInternal(bool promptForAudio)

    {

        if (AppServices.Routes.Editor is null)

        {

            StatusMessage = "Kein Route-Paket geladen.";

            return;

        }



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



        if (promptForAudio)

        {

            PickAudioFile();

        }

        else

        {

            StatusMessage = $"Ansage {code} angelegt – Bezeichnung und Ton ergänzen.";

        }

    }



    [RelayCommand]

    private void DuplicateAnnouncement()

    {

        if (SelectedAnnouncement is null)

        {

            StatusMessage = "Bitte zuerst eine Ansage auswählen.";

            return;

        }



        var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(

            _allAnnouncements.Select(a => a.AnnouncementCode));

        var copy = CloneAnnouncement(SelectedAnnouncement);

        copy.Id = Guid.NewGuid().ToString("N");

        copy.AnnouncementCode = annCode;

        copy.LocalAudioPath = null;

        if (!string.IsNullOrWhiteSpace(copy.DisplayName))

        {

            copy.DisplayName += " (Kopie)";

        }



        copy.EmbeddedSoundFileName =

            ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(annCode, copy.DisplayName);

        _allAnnouncements.Add(copy);

        ApplyFilter();

        SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == copy.Id);

        StatusMessage = $"Ansage dupliziert als {annCode}.";

    }



    [RelayCommand]

    private void RemoveAnnouncement()

    {

        if (SelectedAnnouncement is null)

        {

            StatusMessage = "Bitte zuerst eine Ansage auswählen.";

            return;

        }



        var announcementId = SelectedAnnouncement.Id;

        _allAnnouncements.RemoveAll(a => a.Id == announcementId);

        _sequenceByAnnouncementId.Remove(announcementId);

        _gongByAnnouncementId.Remove(announcementId);

        _sondergongByAnnouncementId.Remove(announcementId);

        _nextStopByAnnouncementId.Remove(announcementId);

        _nextStopMp3ByAnnouncementId.Remove(announcementId);

        ApplyFilter();

        SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();

        StatusMessage = "Ansage entfernt – „Speichern & JSON“ übernimmt die Änderung.";

    }



    [RelayCommand]

    private void SaveChanges() => CommitChanges();



    [RelayCommand]

    private void PickAudioFile()

    {

        if (SelectedAnnouncement is null)

        {

            StatusMessage = "Bitte zuerst eine Ansage auswählen (oder „Neue Ansage mit Tondatei“).";

            return;

        }



        var dialog = CreateAudioOpenFileDialog();

        if (dialog.ShowDialog() != true)

        {

            return;

        }



        var code = ManagedAnnouncementTemplateItem.NormalizeCode(SelectedAnnouncement.AnnouncementCode);

        if (code.Length != 4)

        {

            StatusMessage = "Zuerst gültige 4-stellige Ansagen-ID vergeben.";

            return;

        }



        AppendPickedAudioFile(dialog.FileName);

    }



    private bool CanClearEmbeddedSound() =>
        SelectedAnnouncement is not null &&
        (!string.IsNullOrWhiteSpace(SelectedAnnouncement.EmbeddedSoundFileName) ||
         !string.IsNullOrWhiteSpace(SelectedAnnouncement.LocalAudioPath) ||
         AnnouncementSequence.Any(i => i.Kind == AnnouncementSequenceEntryKind.Audio));

    [RelayCommand(CanExecute = nameof(CanClearEmbeddedSound))]
    private void ClearEmbeddedSound()
    {
        if (SelectedAnnouncement is null)
        {
            return;
        }

        PersistCurrentAnnouncementSequence();

        var id = SelectedAnnouncement.Id;
        SelectedAnnouncement.EmbeddedSoundFileName = string.Empty;
        SelectedAnnouncement.LocalAudioPath = null;

        _sequenceByAnnouncementId.Remove(id);
        _gongByAnnouncementId.Remove(id);
        _sondergongByAnnouncementId.Remove(id);
        _nextStopByAnnouncementId.Remove(id);
        _nextStopMp3ByAnnouncementId.Remove(id);
        _announcementsNeedingAudioMaterialization.Remove(id);

        AnnouncementSequence.Clear();
        _sequenceLoadedForAnnouncementId = id;
        IncludeGongInAnnouncementMerge = false;
        IncludeSondergongInAnnouncementMerge = false;
        IncludeNextStopInAnnouncementMerge = false;
        IncludeNextStopMp3InAnnouncementMerge = false;

        SelectedAnnouncement.NotifyDisplayLabelChanged();
        RefreshSelectedAnnouncementDisplay();
        UpdateSelectedAudioHint();
        OnPropertyChanged(nameof(PickAudioButtonLabel));
        StatusMessage = "Tonzuordnung entfernt – „Speichern & JSON“ nicht vergessen.";
        MarkDirty();
        ClearEmbeddedSoundCommand.NotifyCanExecuteChanged();
    }



    [RelayCommand]

    private void PlayAnnouncementPreview()

    {

        if (SelectedAnnouncement is null)

        {

            StatusMessage = "Bitte zuerst eine Ansage auswählen.";

            return;

        }



        if (!TryBuildPreviewPartsForSelection(out var parts, out var buildError))

        {

            StatusMessage = buildError ?? "Vorschau nicht möglich.";

            return;

        }



        var previewDir = Path.Combine(Path.GetTempPath(), "SmartOepnv", "announcement_preview");

        Directory.CreateDirectory(previewDir);

        var previewPath = Path.Combine(previewDir, $"preview_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");



        try

        {

            EmbeddedSoundConcatenator.ConcatenateSequenceToWav(parts, previewPath);

            AnnouncementPreviewPlayer.Play(previewPath);

            StatusMessage = "Vorschau der Sequenz wird abgespielt…";

        }

        catch (Exception ex)

        {

            StatusMessage = $"Vorschau fehlgeschlagen: {ex.Message}";

        }

    }



    [RelayCommand]

    private void ImportFromRoutes()

    {

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            return;

        }



        var added = 0;

        foreach (var route in editor.RouteNames)

        {

            foreach (var stop in editor.GetStops(route))

            {

                if (string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))

                {

                    continue;

                }



                if (_allAnnouncements.Any(a =>

                        string.Equals(a.EmbeddedSoundFileName, stop.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase)))

                {

                    continue;

                }



                var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(

                    _allAnnouncements.Select(a => a.AnnouncementCode));

                var item = new ManagedAnnouncementTemplateItem

                {

                    AnnouncementCode = annCode,

                    DisplayName = stop.Name,

                    Category = "haltestelle",

                    EmbeddedSoundFileName = stop.EmbeddedSoundFileName.Trim()

                };

                EnsureStopForAnnouncement(item, stop.Name, stop.StopDisplay, stop.VrrStopId);

                AttachWorkspaceAudioPath(item, editor.PackageRoot);

                _allAnnouncements.Add(item);

                added++;

            }

        }



        EnsureStopCodesAndLinks();

        ApplyFilter();

        StatusMessage = added > 0

            ? $"{added} Ansage(n) aus Routen übernommen – „Speichern & JSON“ nicht vergessen."

            : "In den Routen sind keine neuen Tondateien für die Kartei.";

    }



    [RelayCommand]

    private void ImportFromEmbeddedSoundsList()

    {

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            return;

        }



        var names = EmbeddedSoundsEditor.ListFileNames(editor.PackageRoot);

        if (names.Count == 0)

        {

            StatusMessage = "Keine embeddedSounds im Paket.";

            return;

        }



        var added = 0;

        foreach (var fileName in names.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))

        {

            if (_allAnnouncements.Any(t =>

                    string.Equals(t.EmbeddedSoundFileName, fileName, StringComparison.OrdinalIgnoreCase)))

            {

                continue;

            }



            var displayName = Path.GetFileNameWithoutExtension(fileName);

            var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(

                _allAnnouncements.Select(a => a.AnnouncementCode));

            var item = new ManagedAnnouncementTemplateItem

            {

                AnnouncementCode = annCode,

                DisplayName = displayName,

                Category = "haltestelle",

                EmbeddedSoundFileName = fileName

            };

            AttachWorkspaceAudioPath(item, editor.PackageRoot);

            _allAnnouncements.Add(item);

            added++;

        }



        EnsureStopCodesAndLinks();

        ApplyFilter();

        StatusMessage = $"{added} Ansage(n) aus embeddedSounds übernommen – speichern nicht vergessen.";

    }



    private ManagedStopTemplateItem EnsureStopForAnnouncement(

        ManagedAnnouncementTemplateItem announcement,

        string? stopName = null,

        string? stopDisplay = null,

        string? vrrStopId = null)

    {

        if (!string.IsNullOrEmpty(announcement.StopTemplateId))

        {

            var existing = _allStops.FirstOrDefault(s => s.Id == announcement.StopTemplateId);

            if (existing is not null)

            {

                return existing;

            }

        }



        var name = stopName?.Trim();

        if (string.IsNullOrWhiteSpace(name))

        {

            name = announcement.DisplayName.Trim();

        }

        if (string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(announcement.EmbeddedSoundFileName))
        {
            name = Path.GetFileNameWithoutExtension(announcement.EmbeddedSoundFileName.Trim());
        }

        if (string.IsNullOrWhiteSpace(name))

        {

            name = $"Ansage {ManagedAnnouncementTemplateItem.NormalizeCode(announcement.AnnouncementCode)}";

        }



        var stopCode = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));

        var stop = new ManagedStopTemplateItem

        {

            StopCode = stopCode,

            StopNameItcs = name,

            StopDisplay = stopDisplay?.Trim() ?? string.Empty,

            VrrStopId = vrrStopId?.Trim() ?? string.Empty,

            EmbeddedSoundFileName = announcement.EmbeddedSoundFileName.Trim(),

            RadiusMeters = ManagedStopTemplateItem.DefaultRadiusMeters

        };

        _allStops.Add(stop);

        announcement.StopTemplateId = stop.Id;

        return stop;

    }



    private void EnsureStopCodesAndLinks()

    {

        var usedStopCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stop in _allStops)

        {

            if (!PlannerStopCode.IsValid(stop.StopCode))

            {

                stop.StopCode = PlannerStopCode.SuggestNext(usedStopCodes);

            }



            stop.StopCode = PlannerStopCode.Normalize(stop.StopCode);

            usedStopCodes.Add(stop.StopCode);

        }



        foreach (var ann in _allAnnouncements)

        {

            if (!string.IsNullOrEmpty(ann.StopTemplateId) &&

                _allStops.Any(s => s.Id == ann.StopTemplateId))

            {

                continue;

            }



            var matched = _allStops.FirstOrDefault(s =>

                !string.IsNullOrWhiteSpace(s.EmbeddedSoundFileName) &&

                string.Equals(s.EmbeddedSoundFileName, ann.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)

            {

                ann.StopTemplateId = matched.Id;

                continue;

            }



            ann.StopTemplateId = string.Empty;

        }

    }

    /// <summary>
    /// Haltestellenansagen liegen oft nur in managedStopTemplates (Tondatei) – fehlende Kartei-Einträge ergänzen.
    /// </summary>
    private void EnsureAnnouncementsFromStopTemplates(JsonObject packageRoot)
    {
        foreach (var stop in _allStops)
        {
            if (string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))
            {
                continue;
            }

            var fileName = stop.EmbeddedSoundFileName.Trim();
            if (_allAnnouncements.Any(a => a.StopTemplateId == stop.Id))
            {
                continue;
            }

            var byFile = _allAnnouncements.FirstOrDefault(a =>
                string.Equals(a.EmbeddedSoundFileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (byFile is not null)
            {
                if (string.IsNullOrEmpty(byFile.StopTemplateId))
                {
                    byFile.StopTemplateId = stop.Id;
                }

                continue;
            }

            var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(
                _allAnnouncements.Select(a => a.AnnouncementCode));
            var displayName = string.IsNullOrWhiteSpace(stop.StopNameItcs)
                ? Path.GetFileNameWithoutExtension(fileName)
                : stop.StopNameItcs.Trim();

            var item = new ManagedAnnouncementTemplateItem
            {
                StopTemplateId = stop.Id,
                AnnouncementCode = annCode,
                DisplayName = displayName,
                Description = stop.DirectionDescription.Trim(),
                Category = "haltestelle",
                EmbeddedSoundFileName = fileName,
                IncludeInSpecialAnnouncements = false
            };
            AttachWorkspaceAudioPath(item, packageRoot);
            _allAnnouncements.Add(item);
        }
    }



    private void SyncStopMetadataFromAnnouncements()

    {

        foreach (var ann in _allAnnouncements)

        {

            var stop = _allStops.FirstOrDefault(s => s.Id == ann.StopTemplateId);

            if (stop is null)

            {

                continue;

            }



            if (string.IsNullOrWhiteSpace(ann.DisplayName))

            {

                if (!string.IsNullOrWhiteSpace(stop.StopNameItcs))

                {

                    ann.DisplayName = stop.StopNameItcs.Trim();

                }

                else

                {

                    ann.DisplayName =

                        $"Ansage {ManagedAnnouncementTemplateItem.NormalizeCode(ann.AnnouncementCode)}";

                }

            }



            if (!string.IsNullOrWhiteSpace(ann.DisplayName))

            {

                stop.StopNameItcs = ann.DisplayName.Trim();

            }



            if (!string.IsNullOrWhiteSpace(ann.EmbeddedSoundFileName))

            {

                stop.EmbeddedSoundFileName = ann.EmbeddedSoundFileName.Trim();

            }

        }

    }



    private void AttachWorkspaceAudioPath(ManagedAnnouncementTemplateItem item, JsonObject packageRoot)

    {

        if (!AppServices.IsInitialized || string.IsNullOrWhiteSpace(item.EmbeddedSoundFileName))

        {

            return;

        }



        var ws = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(

            AppServices.Workspace,

            item.EmbeddedSoundFileName);

        if (ws is not null)

        {

            item.LocalAudioPath = ws;

            return;

        }



        if (PlanerEmbeddedSoundsWorkspace.HasSoundInPackage(packageRoot, item.EmbeddedSoundFileName))

        {

            item.LocalAudioPath = null;

        }

    }



    /// <summary>Kopiert gewählte Tondatei in den Workspace und bettet sie vorab ins JSON ein.</summary>

    private bool StageAnnouncementAudio(ManagedAnnouncementTemplateItem item, string sourcePath)

    {

        if (!File.Exists(sourcePath))

        {

            StatusMessage = "Tondatei nicht gefunden.";

            return false;

        }



        try

        {

            if (AppServices.IsInitialized)

            {

                var target = Path.Combine(

                    PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace),

                    item.EmbeddedSoundFileName.Trim());

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                var sourceFullPath = Path.GetFullPath(sourcePath);
                var targetFullPath = Path.GetFullPath(target);
                if (!string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, target, overwrite: true);
                }

                item.LocalAudioPath = target;

            }

            else

            {

                item.LocalAudioPath = sourcePath;

            }



            var editor = AppServices.Routes.Editor;

            if (editor is not null)

            {

                EmbeddedSoundsEditor.UpsertFromFile(

                    editor.PackageRoot,

                    item.EmbeddedSoundFileName.Trim(),

                    item.LocalAudioPath);

            }



            return true;

        }

        catch (Exception ex)

        {

            StatusMessage = $"Tondatei konnte nicht übernommen werden: {ex.Message}";

            return false;

        }

    }



    private void UpdateSelectedAudioHint()

    {

        if (SelectedAnnouncement is null || AppServices.Routes.Editor is null)

        {

            SelectedAnnouncementAudioHint = null;

            RefreshSelectedAnnouncementDisplay();

            return;

        }



        RefreshSelectedAnnouncementDisplay();



        if (TemplateHasAudio(SelectedAnnouncement, AppServices.Routes.Editor.PackageRoot))

        {

            var inJson = PlanerEmbeddedSoundsWorkspace.HasSoundInPackage(

                AppServices.Routes.Editor.PackageRoot,

                SelectedAnnouncement.EmbeddedSoundFileName);

            SelectedAnnouncementAudioHint = inJson

                ? $"Ton im JSON: {SelectedAnnouncement.EmbeddedSoundFileName}"

                : $"Ton bereit: {SelectedAnnouncement.EmbeddedSoundFileName} – bitte „Speichern & JSON“.";

            return;

        }



        if (!string.IsNullOrWhiteSpace(SelectedAnnouncement.LocalAudioPath) &&

            File.Exists(SelectedAnnouncement.LocalAudioPath))

        {

            SelectedAnnouncementAudioHint =

                $"Ton: {Path.GetFileName(SelectedAnnouncement.LocalAudioPath)} – bitte „Speichern & JSON“.";

            return;

        }



        if (AnnouncementSequence.Count > 0 ||
            IncludeGongInAnnouncementMerge ||
            IncludeSondergongInAnnouncementMerge ||
            IncludeNextStopInAnnouncementMerge ||
            IncludeNextStopMp3InAnnouncementMerge)
        {

            var audioCount = AnnouncementSequence.Count(i => i.Kind == AnnouncementSequenceEntryKind.Audio);

            var pauseCount = AnnouncementSequence.Count(i => i.Kind == AnnouncementSequenceEntryKind.Pause);

            var prefixPart = BuildPrefixSequenceLabel(

                IncludeGongInAnnouncementMerge,

                IncludeSondergongInAnnouncementMerge,

                IncludeNextStopInAnnouncementMerge,

                IncludeNextStopMp3InAnnouncementMerge);

            SelectedAnnouncementAudioHint =

                $"{prefixPart}{audioCount} Tondatei(en), {pauseCount} Pause(n) in der Sequenz – „Speichern & JSON“ erzeugt die fertige Ansage.";

            return;

        }



        SelectedAnnouncementAudioHint = "Keine Tondatei – bitte „Tondatei wählen“ und speichern.";

    }



    private static bool TemplateHasAudio(ManagedAnnouncementTemplateItem t, JsonObject packageRoot)

    {

        if (!string.IsNullOrWhiteSpace(t.LocalAudioPath) && File.Exists(t.LocalAudioPath))

        {

            return true;

        }



        if (AppServices.IsInitialized &&

            !string.IsNullOrWhiteSpace(t.EmbeddedSoundFileName) &&

            PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(AppServices.Workspace, t.EmbeddedSoundFileName) is not null)

        {

            return true;

        }



        return !string.IsNullOrWhiteSpace(t.EmbeddedSoundFileName) &&

               PlanerEmbeddedSoundsWorkspace.HasSoundInPackage(packageRoot, t.EmbeddedSoundFileName);

    }



    private string BuildListStatusMessage()

    {

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            return StatusMessage;

        }



        var root = editor.PackageRoot;

        var withAudio = _allAnnouncements.Count(a => TemplateHasAudio(a, root));

        return $"{_allAnnouncements.Count} Ansagen ({withAudio} mit Ton) – Speichern schreibt managedAnnouncementTemplates.";

    }



    private void EnsureAnnouncementDisplayNamesBeforeCommit()
    {
        PersistCurrentAnnouncementSequence();

        foreach (var ann in _allAnnouncements)
        {
            if (!string.IsNullOrWhiteSpace(ann.DisplayName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ann.EmbeddedSoundFileName))
            {
                ann.DisplayName = Path.GetFileNameWithoutExtension(ann.EmbeddedSoundFileName.Trim());
                continue;
            }

            if (_sequenceByAnnouncementId.TryGetValue(ann.Id, out var sequence))
            {
                var firstAudio = sequence.FirstOrDefault(i => i.Kind == AnnouncementSequenceEntryKind.Audio);
                if (firstAudio is not null && !string.IsNullOrWhiteSpace(firstAudio.DisplayName))
                {
                    ann.DisplayName = Path.GetFileNameWithoutExtension(firstAudio.DisplayName.Trim());
                }
            }
        }
    }

    private void EnsureStopNamesBeforeCommit()
    {
        foreach (var stop in _allStops)
        {
            if (!string.IsNullOrWhiteSpace(stop.StopNameItcs))
            {
                continue;
            }

            var linkedAnn = _allAnnouncements.FirstOrDefault(a => a.StopTemplateId == stop.Id);
            if (linkedAnn is not null && !string.IsNullOrWhiteSpace(linkedAnn.DisplayName))
            {
                stop.StopNameItcs = linkedAnn.DisplayName.Trim();
                continue;
            }

            if (PlannerStopCode.IsValid(stop.StopCode))
            {
                stop.StopNameItcs = $"Haltestelle {PlannerStopCode.Normalize(stop.StopCode)}";
            }
        }
    }

    private void PruneEmptyDraftStops()

    {

        var removed = _allStops.RemoveAll(s => s.IsEmptyDraft());

        if (removed == 0)

        {

            return;

        }



        _allAnnouncements.RemoveAll(a =>

            !string.IsNullOrEmpty(a.StopTemplateId) &&

            !_allStops.Any(s => string.Equals(s.Id, a.StopTemplateId, StringComparison.Ordinal)));



        ApplyFilter();

        if (SelectedAnnouncement is not null &&

            !_allAnnouncements.Any(a => a.Id == SelectedAnnouncement.Id))

        {

            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();

        }

    }



    /// <summary>ListBox zeigt DisplayLabel – nach Speichern Labels aktualisieren ohne Listen-Reset.</summary>
    private void RefreshAnnouncementListLabels(bool rebuildFilter = true)
    {
        var selectedId = SelectedAnnouncement?.Id;
        if (rebuildFilter)
        {
            ApplyFilter();
        }
        else
        {
            foreach (var announcement in FilteredAnnouncements)
            {
                announcement.NotifyDisplayLabelChanged();
            }
        }

        if (selectedId is null || SelectedAnnouncement?.Id == selectedId)
        {
            return;
        }

        SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault(a => a.Id == selectedId)
                               ?? FilteredAnnouncements.FirstOrDefault();
    }

    private void ApplyFilter()

    {

        var q = SearchQuery.Trim();

        FilteredAnnouncements.Clear();

        IEnumerable<ManagedAnnouncementTemplateItem> source = _allAnnouncements

            .OrderBy(a => a.AnnouncementCode, StringComparer.OrdinalIgnoreCase)

            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);



        if (!string.IsNullOrEmpty(q))

        {

            source = source.Where(a =>

                (a.AnnouncementCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||

                (a.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||

                (a.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||

                (a.Lines?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||

                (a.EmbeddedSoundFileName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        }



        foreach (var ann in source)

        {

            FilteredAnnouncements.Add(ann);

        }



        if (SelectedAnnouncement is not null &&

            !FilteredAnnouncements.Any(a => a.Id == SelectedAnnouncement.Id))

        {

            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();

        }

    }



    private string? ValidateAll(JsonObject packageRoot)

    {

        var annCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in _allAnnouncements)

        {

            var code = ManagedAnnouncementTemplateItem.NormalizeCode(t.AnnouncementCode);

            if (!ManagedAnnouncementTemplateItem.IsValidCode(code))

            {

                return $"„{t.DisplayName}“: Ansagen-ID muss genau 4 Ziffern haben.";

            }



            if (!annCodes.Add(code))

            {

                return $"Doppelte Ansagen-ID {code}.";

            }



            t.AnnouncementCode = code;



            if (string.IsNullOrWhiteSpace(t.DisplayName))

            {

                return $"Ansage {code}: Bezeichnung fehlt.";

            }



            if (t.IncludeInSpecialAnnouncements)

            {

                if (string.IsNullOrWhiteSpace(t.EmbeddedSoundFileName))

                {

                    return $"„{t.DisplayName}“ ({code}): Sonderansage ohne Dateiname – „Tondatei wählen“.";

                }



                if (!TemplateHasAudio(t, packageRoot))

                {

                    return $"„{t.DisplayName}“ ({code}): Sonderansage ohne Ton – „Tondatei wählen“, dann „Speichern & JSON“.";

                }

            }

        }



        var stopCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stop in _allStops.Where(s => !s.IsEmptyDraft()))

        {

            var code = PlannerStopCode.Normalize(stop.StopCode);

            if (!PlannerStopCode.IsValid(code))

            {

                stop.StopCode = PlannerStopCode.SuggestNext(stopCodes);

                code = stop.StopCode;

            }



            if (!stopCodes.Add(code))

            {

                stop.StopCode = PlannerStopCode.SuggestNext(stopCodes);

                stopCodes.Add(stop.StopCode);

            }

            else

            {

                stop.StopCode = code;

            }



            if (string.IsNullOrWhiteSpace(stop.StopNameItcs))

            {

                var linked = _allAnnouncements.FirstOrDefault(a => a.StopTemplateId == stop.Id);

                stop.StopNameItcs = linked?.DisplayName.Trim() ?? $"Ansage {stop.StopCode}";

            }

        }



        return null;

    }



    private static bool TryParseMergePauseSeconds(string raw, out double seconds, out string? error)

    {

        error = null;

        seconds = 0;

        var trimmed = raw.Trim();

        if (string.IsNullOrEmpty(trimmed))

        {

            return true;

        }



        trimmed = trimmed.Replace(',', '.');

        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Float,

                System.Globalization.CultureInfo.InvariantCulture, out seconds) ||

            seconds < 0 ||

            seconds > 30)

        {

            error = "Pause: Zahl von 0 bis 30 Sekunden, z. B. 0,3";

            return false;

        }



        return true;

    }



    private static string BuildMergedAnnouncementFileName(ManagedAnnouncementTemplateItem item)

    {

        var code = ManagedAnnouncementTemplateItem.NormalizeCode(item.AnnouncementCode);

        if (code.Length != 4)

        {

            code = "0000";

        }



        var safeName = string.Concat(

            (item.DisplayName ?? "ansage").Trim()

                .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_'));

        if (string.IsNullOrEmpty(safeName))

        {

            safeName = "ANSAGE";

        }



        if (safeName.Length > 20)

        {

            safeName = safeName[..20];

        }



        return $"{code}_{safeName}_zusammen.wav";

    }



    private static ManagedAnnouncementTemplateItem CloneAnnouncement(ManagedAnnouncementTemplateItem source) => new()

    {

        Id = source.Id,

        StopTemplateId = source.StopTemplateId,

        AnnouncementCode = source.AnnouncementCode,

        DisplayName = source.DisplayName,

        Description = source.Description,

        Lines = source.Lines,

        Category = source.Category,

        EmbeddedSoundFileName = source.EmbeddedSoundFileName,

        IncludeInSpecialAnnouncements = source.IncludeInSpecialAnnouncements,

        LocalAudioPath = source.LocalAudioPath

    };



    private static ManagedStopTemplateItem CloneStop(ManagedStopTemplateItem source) => new()

    {

        Id = source.Id,

        StopCode = source.StopCode,

        StopNameItcs = source.StopNameItcs,

        StopDisplay = source.StopDisplay,

        VrrStopId = source.VrrStopId,

        DirectionDescription = source.DirectionDescription,

        AnnouncementLat = source.AnnouncementLat,

        AnnouncementLng = source.AnnouncementLng,

        StopLat = source.StopLat,

        StopLng = source.StopLng,

        RadiusMeters = source.RadiusMeters,

        ExternalSoundUri = source.ExternalSoundUri,

        EmbeddedSoundFileName = source.EmbeddedSoundFileName

    };

}


