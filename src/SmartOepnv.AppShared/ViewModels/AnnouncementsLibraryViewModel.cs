using System.Collections.ObjectModel;

using System.IO;

using System.Text.Json;

using System.Text.Json.Nodes;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using System.Windows;

using SmartOepnv.AppShared.Views;

using SmartOepnv.Core;

using SmartOepnv.Core.RoutePackage;



namespace SmartOepnv.AppShared.ViewModels;



public partial class AnnouncementsLibraryViewModel : ObservableObject, IEditorAreaViewModel

{

    private readonly List<ManagedStopTemplateItem> _allStops = [];

    private readonly List<ManagedAnnouncementTemplateItem> _allAnnouncements = [];

    private readonly EditorAreaSyncState _sync = new();

    private string? _loadedFingerprint;



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

    [ObservableProperty] private string announcementMergePauseSeconds = "0,3";



    public ObservableCollection<ManagedAnnouncementTemplateItem> FilteredAnnouncements { get; } = [];



    public AnnouncementsLibraryViewModel()

    {

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



        CommitChanges();

    }



    private string ComputeFingerprint() =>

        JsonSerializer.Serialize(new

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

                a.Category,

                a.EmbeddedSoundFileName,

                a.IncludeInSpecialAnnouncements,

                a.LocalAudioPath

            })

        });



    private void MarkDirty() => _sync.MarkDirty();



    public void CommitChanges()

    {

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            StatusMessage = "Kein Route-Paket geladen.";

            return;

        }



        PruneEmptyDraftStops();

        EnsureStopCodesAndLinks();

        SyncStopMetadataFromAnnouncements();



        var validation = ValidateAll(editor.PackageRoot);

        if (validation is not null)

        {

            StatusMessage = validation;

            return;

        }



        var stopClones = _allStops.Where(s => !s.IsEmptyDraft()).Select(CloneStop).ToList();

        var announcementClones = _allAnnouncements.Select(CloneAnnouncement).ToList();

        editor.ReplaceStopTemplates(stopClones);

        editor.ReplaceAnnouncementTemplates(announcementClones);

        editor.SyncEmbeddedSoundsFromTemplates(announcementClones, AppServices.Workspace);

        AppServices.Routes.ApplyEditorChanges("ansagen");



        foreach (var t in _allAnnouncements)

        {

            t.LocalAudioPath = null;

            AttachWorkspaceAudioPath(t, editor.PackageRoot);

        }



        RefreshAnnouncementListDisplay();

        UpdateSelectedAudioHint();

        StatusMessage = $"{_allAnnouncements.Count} Ansagen gespeichert – bereit für Dropbox.";

        _sync.AfterCommit();

        _loadedFingerprint = ComputeFingerprint();

    }



    partial void OnSearchQueryChanged(string value) => ApplyFilter();



    partial void OnSelectedAnnouncementChanged(ManagedAnnouncementTemplateItem? value)

    {

        if (value is not null)

        {

            value.AnnouncementCode = ManagedAnnouncementTemplateItem.NormalizeCode(value.AnnouncementCode);

        }



        UpdateSelectedAudioHint();

    }



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

            EmbeddedSoundFileName = ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(code, "Ansage"),

            IncludeInSpecialAnnouncements = false

        };

        EnsureStopForAnnouncement(item);

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

        EnsureStopForAnnouncement(copy);

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



        var dialog = new OpenFileDialog

        {

            Title = "Tondatei für Ansage wählen",

            Filter = "Audio (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|Alle Dateien (*.*)|*.*"

        };



        if (dialog.ShowDialog() != true)

        {

            return;

        }



        var ext = Path.GetExtension(dialog.FileName);

        if (string.IsNullOrEmpty(ext))

        {

            ext = ".mp3";

        }



        var code = ManagedAnnouncementTemplateItem.NormalizeCode(SelectedAnnouncement.AnnouncementCode);

        if (code.Length != 4)

        {

            StatusMessage = "Zuerst gültige 4-stellige Ansagen-ID vergeben.";

            return;

        }



        SelectedAnnouncement.LocalAudioPath = dialog.FileName;

        var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);

        SelectedAnnouncement.EmbeddedSoundFileName = $"{code}_{baseName}{ext}";

        UpdateSelectedAudioHint();

        StatusMessage =

            $"Ton gewählt – mit „Speichern & JSON“ wird „{SelectedAnnouncement.EmbeddedSoundFileName}“ eingetragen.";

    }



    [RelayCommand]

    private void MergeEmbeddedSoundsFromList()

    {

        if (SelectedAnnouncement is null)

        {

            StatusMessage = "Bitte zuerst eine Ansage auswählen (oder „Neue Ansage“ anlegen).";

            return;

        }



        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            StatusMessage = "Kein Route-Paket geladen.";

            return;

        }



        if (!AppServices.IsInitialized)

        {

            StatusMessage = "Workspace nicht initialisiert – Route-Paket erneut laden.";

            return;

        }



        var names = TryListEmbeddedSoundNamesForMerge(out var listError);

        if (names is null)

        {

            StatusMessage = listError ?? "Keine Ansagen verfügbar.";

            return;

        }



        if (!TryParseMergePauseSeconds(AnnouncementMergePauseSeconds, out var pauseSeconds, out var pauseError))

        {

            StatusMessage = pauseError ?? "Pause ungültig.";

            return;

        }



        var prefill = SelectedAnnouncement.EmbeddedSoundFileName?.Trim();

        if (string.IsNullOrEmpty(prefill))

        {

            prefill = SelectedAnnouncement.DisplayName?.Trim();

        }



        var owner = ResolveMergeDialogOwner();

        var dialog = new EmbeddedSoundMultiPickerDialog(names, prefill) { Owner = owner };

        if (dialog.ShowDialog() != true || dialog.SelectedFileNames.Count < 2)

        {

            return;

        }



        var sourcePaths = new List<string>();

        foreach (var fileName in dialog.SelectedFileNames)

        {

            var path = EmbeddedSoundPathResolver.TryResolveLocalPath(

                fileName,

                editor.PackageRoot,

                AppServices.Workspace);

            if (path is null)

            {

                StatusMessage = $"Ansage „{fileName}“ konnte nicht geladen werden.";

                return;

            }



            sourcePaths.Add(path);

        }



        var outputFileName = BuildMergedAnnouncementFileName(SelectedAnnouncement);

        var outputPath = Path.Combine(

            PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace),

            outputFileName);



        try

        {

            EmbeddedSoundConcatenator.ConcatenateToWav(

                sourcePaths,

                outputPath,

                TimeSpan.FromSeconds(pauseSeconds));

        }

        catch (Exception ex)

        {

            StatusMessage = $"Zusammenfügen fehlgeschlagen: {ex.Message}";

            return;

        }



        SelectedAnnouncement.LocalAudioPath = outputPath;

        SelectedAnnouncement.EmbeddedSoundFileName = outputFileName;

        UpdateSelectedAudioHint();

        StatusMessage =

            $"{dialog.SelectedFileNames.Count} Schnipsel zu „{outputFileName}“ zusammengefügt" +

            (pauseSeconds > 0 ? $" (Pause {pauseSeconds:0.###} s)" : string.Empty) +

            " – mit „Speichern & JSON“ übernehmen.";

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

            EnsureStopForAnnouncement(item, displayName);

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



            EnsureStopForAnnouncement(ann);

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



    private void UpdateSelectedAudioHint()

    {

        if (SelectedAnnouncement is null || AppServices.Routes.Editor is null)

        {

            SelectedAnnouncementAudioHint = null;

            return;

        }



        if (!string.IsNullOrWhiteSpace(SelectedAnnouncement.LocalAudioPath) &&

            File.Exists(SelectedAnnouncement.LocalAudioPath))

        {

            SelectedAnnouncementAudioHint =

                $"Ton: {Path.GetFileName(SelectedAnnouncement.LocalAudioPath)} (wird beim Speichern eingebettet)";

            return;

        }



        if (TemplateHasAudio(SelectedAnnouncement, AppServices.Routes.Editor.PackageRoot))

        {

            SelectedAnnouncementAudioHint = $"Ton im JSON: {SelectedAnnouncement.EmbeddedSoundFileName}";

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



    private void PruneEmptyDraftStops()

    {

        var removed = _allStops.RemoveAll(s => s.IsEmptyDraft());

        if (removed == 0)

        {

            return;

        }



        _allAnnouncements.RemoveAll(a =>

            !_allStops.Any(s => string.Equals(s.Id, a.StopTemplateId, StringComparison.Ordinal)));



        ApplyFilter();

        if (SelectedAnnouncement is not null &&

            !_allAnnouncements.Any(a => a.Id == SelectedAnnouncement.Id))

        {

            SelectedAnnouncement = FilteredAnnouncements.FirstOrDefault();

        }

    }



    private void RefreshAnnouncementListDisplay()

    {

        var selectedId = SelectedAnnouncement?.Id;

        ApplyFilter();

        var restored = selectedId is null

            ? null

            : FilteredAnnouncements.FirstOrDefault(a => a.Id == selectedId)

              ?? FilteredAnnouncements.FirstOrDefault();

        SelectedAnnouncement = null;

        SelectedAnnouncement = restored;

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



            if (string.IsNullOrWhiteSpace(t.StopTemplateId) ||

                _allStops.All(s => s.Id != t.StopTemplateId))

            {

                return $"Ansage {code}: interne Verknüpfung fehlt – bitte erneut speichern.";

            }



            if (string.IsNullOrWhiteSpace(t.DisplayName))

            {

                return $"Ansage {code}: Bezeichnung fehlt.";

            }



            if (string.IsNullOrWhiteSpace(t.EmbeddedSoundFileName))

            {

                return $"„{t.DisplayName}“ ({code}): Dateiname fehlt – Tondatei wählen.";

            }



            if (!TemplateHasAudio(t, packageRoot))

            {

                return $"„{t.DisplayName}“ ({code}): Keine Tondatei – bitte wählen und speichern.";

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



    private IReadOnlyList<string>? TryListEmbeddedSoundNamesForMerge(out string? error)

    {

        error = null;

        var editor = AppServices.Routes.Editor;

        if (editor is null)

        {

            error = "Kein Route-Paket geladen.";

            return null;

        }



        var extra = editor.AnnouncementTemplates

            .Select(t => t.EmbeddedSoundFileName)

            .Concat(_allAnnouncements.Select(t => t.EmbeddedSoundFileName));

        var names = EmbeddedSoundCatalog.ListAvailable(

            editor.PackageRoot,

            AppServices.IsInitialized ? AppServices.Workspace : null,

            extra);



        if (names.Count == 0)

        {

            error = "Keine eingebetteten Ansagen – zuerst einzelne Schnipsel anlegen und speichern.";

            return null;

        }



        return names;

    }



    private static Window? ResolveMergeDialogOwner()

    {

        var owner = Application.Current?.MainWindow;

        if (owner is not null && !owner.IsLoaded)

        {

            owner = null;

        }



        return owner;

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


