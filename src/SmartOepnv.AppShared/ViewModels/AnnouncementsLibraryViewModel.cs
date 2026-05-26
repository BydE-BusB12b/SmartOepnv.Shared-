using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel : ObservableObject
{
    private readonly List<ManagedStopTemplateItem> _allStops = [];
    private readonly List<ManagedAnnouncementTemplateItem> _allAnnouncements = [];

    public ObservableCollection<string> Categories { get; } =
    [
        "haltestelle",
        "sonder",
        "sonstiges"
    ];

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private ManagedStopTemplateItem? selectedStop;
    [ObservableProperty] private ManagedAnnouncementTemplateItem? selectedAnnouncement;
    [ObservableProperty] private string? selectedAnnouncementAudioHint;

    public ObservableCollection<ManagedStopTemplateItem> FilteredStops { get; } = [];
    public ObservableCollection<ManagedAnnouncementTemplateItem> AnnouncementsForSelectedStop { get; } = [];

    public AnnouncementsLibraryViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChanges);
        }
    }

    public void RefreshFromEditor()
    {
        _allStops.Clear();
        _allAnnouncements.Clear();
        FilteredStops.Clear();
        AnnouncementsForSelectedStop.Clear();
        SelectedStop = null;
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
        ApplyFilter();
        SelectedStop = FilteredStops.FirstOrDefault();
        RefreshAnnouncementsForSelectedStop();
        StatusMessage = BuildListStatusMessage();
    }

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
        SyncStopEmbeddedSoundFromAnnouncements();

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

        RefreshStopListDisplay();
        RefreshAnnouncementsForSelectedStop();
        UpdateSelectedAudioHint();
        StatusMessage =
            $"{_allStops.Count} Haltestellen, {_allAnnouncements.Count} Ansagen gespeichert – bereit für Dropbox.";
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedStopChanged(ManagedStopTemplateItem? value)
    {
        if (value is not null)
        {
            value.StopCode = PlannerStopCode.Normalize(value.StopCode);
        }

        RefreshAnnouncementsForSelectedStop();
        SelectedAnnouncement = AnnouncementsForSelectedStop.FirstOrDefault();
    }

    partial void OnSelectedAnnouncementChanged(ManagedAnnouncementTemplateItem? value)
    {
        if (value is not null)
        {
            value.AnnouncementCode = ManagedAnnouncementTemplateItem.NormalizeCode(value.AnnouncementCode);
        }

        UpdateSelectedAudioHint();
    }

    [RelayCommand]
    private void AddStop()
    {
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var code = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));
        var stop = new ManagedStopTemplateItem
        {
            StopCode = code,
            StopNameItcs = string.Empty,
            RadiusMeters = ManagedStopTemplateItem.DefaultRadiusMeters
        };
        _allStops.Add(stop);
        ApplyFilter();
        SelectedStop = FilteredStops.FirstOrDefault(s => s.Id == stop.Id);
        StatusMessage = $"Haltestelle {code} angelegt – Ansage hinzufügen und speichern.";
    }

    [RelayCommand]
    private void AddAnnouncement() => AddAnnouncementInternal(promptForAudio: false);

    [RelayCommand]
    private void AddAnnouncementWithAudio() => AddAnnouncementInternal(promptForAudio: true);

    private void AddAnnouncementInternal(bool promptForAudio)
    {
        if (SelectedStop is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle in der Liste wählen.";
            return;
        }

        var code = ManagedAnnouncementTemplateItem.SuggestNextCode(
            AnnouncementsForStop(SelectedStop.Id).Select(a => a.AnnouncementCode));
        var item = new ManagedAnnouncementTemplateItem
        {
            StopTemplateId = SelectedStop.Id,
            AnnouncementCode = code,
            DisplayName = SelectedStop.StopNameItcs.Trim(),
            Category = "haltestelle",
            EmbeddedSoundFileName = ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(code, SelectedStop.StopNameItcs),
            IncludeInSpecialAnnouncements = false
        };
        _allAnnouncements.Add(item);
        RefreshAnnouncementsForSelectedStop();
        SelectedAnnouncement = AnnouncementsForSelectedStop.FirstOrDefault(a => a.Id == item.Id);

        if (promptForAudio)
        {
            PickAudioFile();
        }
        else
        {
            StatusMessage = $"Ansage {code} für Haltestelle {SelectedStop.StopCode} angelegt.";
        }
    }

    [RelayCommand]
    private void DuplicateStop()
    {
        if (SelectedStop is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        var code = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));
        var copy = CloneStop(SelectedStop);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.StopCode = code;
        if (!string.IsNullOrWhiteSpace(copy.StopNameItcs))
        {
            copy.StopNameItcs += " (Kopie)";
        }

        copy.EmbeddedSoundFileName = string.Empty;
        _allStops.Add(copy);

        foreach (var ann in AnnouncementsForStop(SelectedStop.Id))
        {
            var annCopy = CloneAnnouncement(ann);
            annCopy.Id = Guid.NewGuid().ToString("N");
            annCopy.StopTemplateId = copy.Id;
            var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(
                _allAnnouncements.Select(a => a.AnnouncementCode));
            annCopy.AnnouncementCode = annCode;
            annCopy.LocalAudioPath = null;
            annCopy.EmbeddedSoundFileName =
                ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(annCode, annCopy.DisplayName);
            _allAnnouncements.Add(annCopy);
        }

        ApplyFilter();
        SelectedStop = FilteredStops.FirstOrDefault(s => s.Id == copy.Id);
        StatusMessage = $"Haltestelle dupliziert als {code}.";
    }

    [RelayCommand]
    private void RemoveStop()
    {
        if (SelectedStop is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        var stopId = SelectedStop.Id;
        _allStops.RemoveAll(s => s.Id == stopId);
        _allAnnouncements.RemoveAll(a => a.StopTemplateId == stopId);
        ApplyFilter();
        SelectedStop = FilteredStops.FirstOrDefault();
        StatusMessage = "Haltestelle und zugehörige Ansagen entfernt – „Speichern & JSON“ nicht vergessen.";
    }

    [RelayCommand]
    private void RemoveAnnouncement()
    {
        if (SelectedAnnouncement is null)
        {
            StatusMessage = "Bitte zuerst eine Ansage auswählen.";
            return;
        }

        _allAnnouncements.RemoveAll(a => a.Id == SelectedAnnouncement.Id);
        RefreshAnnouncementsForSelectedStop();
        SelectedAnnouncement = AnnouncementsForSelectedStop.FirstOrDefault();
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
    private void ApplyAnnouncementToStop()
    {
        if (SelectedStop is null || SelectedAnnouncement is null)
        {
            StatusMessage = "Haltestelle und Ansage auswählen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedAnnouncement.EmbeddedSoundFileName))
        {
            StatusMessage = "Kein Dateiname – zuerst Ton wählen.";
            return;
        }

        if (!TemplateHasAudio(SelectedAnnouncement, editor.PackageRoot))
        {
            StatusMessage = "Zuerst Tondatei wählen und speichern.";
            return;
        }

        SelectedStop.EmbeddedSoundFileName = SelectedAnnouncement.EmbeddedSoundFileName.Trim();
        StatusMessage =
            $"Ton „{SelectedStop.EmbeddedSoundFileName}“ der Haltestelle {SelectedStop.StopCode} zugeordnet.";
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

                var managedStop = _allStops.FirstOrDefault(s =>
                    string.Equals(s.StopNameItcs, stop.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.EmbeddedSoundFileName, stop.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase));
                if (managedStop is null)
                {
                    var stopCode = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));
                    managedStop = new ManagedStopTemplateItem
                    {
                        StopCode = stopCode,
                        StopNameItcs = stop.Name,
                        StopDisplay = stop.StopDisplay,
                        VrrStopId = stop.VrrStopId,
                        EmbeddedSoundFileName = stop.EmbeddedSoundFileName.Trim()
                    };
                    _allStops.Add(managedStop);
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
                    StopTemplateId = managedStop.Id,
                    AnnouncementCode = annCode,
                    DisplayName = stop.Name,
                    Category = "haltestelle",
                    EmbeddedSoundFileName = stop.EmbeddedSoundFileName.Trim()
                };
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

            var stopCode = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));
            var stop = new ManagedStopTemplateItem
            {
                StopCode = stopCode,
                StopNameItcs = Path.GetFileNameWithoutExtension(fileName),
                EmbeddedSoundFileName = fileName
            };
            _allStops.Add(stop);

            var annCode = ManagedAnnouncementTemplateItem.SuggestNextCode(
                _allAnnouncements.Select(a => a.AnnouncementCode));
            var item = new ManagedAnnouncementTemplateItem
            {
                StopTemplateId = stop.Id,
                AnnouncementCode = annCode,
                DisplayName = stop.StopNameItcs,
                Category = "haltestelle",
                EmbeddedSoundFileName = fileName
            };
            AttachWorkspaceAudioPath(item, editor.PackageRoot);
            _allAnnouncements.Add(item);
            added++;
        }

        EnsureStopCodesAndLinks();
        ApplyFilter();
        StatusMessage = $"{added} Haltestelle(n) aus embeddedSounds übernommen – speichern nicht vergessen.";
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
            if (!string.IsNullOrEmpty(ann.StopTemplateId))
            {
                continue;
            }

            var matched = _allStops.FirstOrDefault(s =>
                !string.IsNullOrWhiteSpace(s.EmbeddedSoundFileName) &&
                string.Equals(s.EmbeddedSoundFileName, ann.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                ann.StopTemplateId = matched.Id;
            }
        }

        foreach (var ann in _allAnnouncements.Where(a =>
                     string.IsNullOrEmpty(a.StopTemplateId) &&
                     string.Equals(a.Category, "haltestelle", StringComparison.OrdinalIgnoreCase)))
        {
            var stopCode = PlannerStopCode.SuggestNext(_allStops.Select(s => s.StopCode));
            var stop = new ManagedStopTemplateItem
            {
                StopCode = stopCode,
                StopNameItcs = string.IsNullOrWhiteSpace(ann.DisplayName) ? "Haltestelle" : ann.DisplayName.Trim(),
                EmbeddedSoundFileName = ann.EmbeddedSoundFileName
            };
            _allStops.Add(stop);
            ann.StopTemplateId = stop.Id;
        }
    }

    private void SyncStopEmbeddedSoundFromAnnouncements()
    {
        foreach (var stop in _allStops)
        {
            var primary = AnnouncementsForStop(stop.Id)
                .FirstOrDefault(a => TemplateHasAudio(a, AppServices.Routes.Editor?.PackageRoot ?? new JsonObject()));
            if (primary is not null && !string.IsNullOrWhiteSpace(primary.EmbeddedSoundFileName))
            {
                stop.EmbeddedSoundFileName = primary.EmbeddedSoundFileName.Trim();
            }
        }
    }

    private IEnumerable<ManagedAnnouncementTemplateItem> AnnouncementsForStop(string stopId) =>
        _allAnnouncements.Where(a => a.StopTemplateId == stopId);

    private void RefreshAnnouncementsForSelectedStop()
    {
        AnnouncementsForSelectedStop.Clear();
        if (SelectedStop is null)
        {
            SelectedAnnouncement = null;
            SelectedAnnouncementAudioHint = null;
            return;
        }

        foreach (var ann in AnnouncementsForStop(SelectedStop.Id)
                     .OrderBy(a => a.AnnouncementCode, StringComparer.OrdinalIgnoreCase))
        {
            AnnouncementsForSelectedStop.Add(ann);
        }

        if (SelectedAnnouncement is null ||
            !AnnouncementsForSelectedStop.Any(a => a.Id == SelectedAnnouncement.Id))
        {
            SelectedAnnouncement = AnnouncementsForSelectedStop.FirstOrDefault();
        }
        else
        {
            UpdateSelectedAudioHint();
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

    private bool StopHasAudio(ManagedStopTemplateItem stop)
    {
        var root = AppServices.Routes.Editor?.PackageRoot ?? new JsonObject();
        if (AnnouncementsForStop(stop.Id).Any(a => TemplateHasAudio(a, root)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName) &&
               PlanerEmbeddedSoundsWorkspace.HasSoundInPackage(root, stop.EmbeddedSoundFileName);
    }

    private string BuildListStatusMessage()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return StatusMessage;
        }

        var withAudio = _allStops.Count(StopHasAudio);
        var annCount = _allAnnouncements.Count;
        return $"{_allStops.Count} Haltestellen ({withAudio} mit Ton), {annCount} Ansagen – Speichern schreibt managedStopTemplates + managedAnnouncementTemplates.";
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
        if (SelectedStop is not null && !_allStops.Any(s => s.Id == SelectedStop.Id))
        {
            SelectedStop = FilteredStops.FirstOrDefault();
            RefreshAnnouncementsForSelectedStop();
        }
    }

    /// <summary>Haltestellen-Liste nach Speichern neu aufbauen (DisplayLabel / ✓⚠).</summary>
    private void RefreshStopListDisplay()
    {
        var selectedId = SelectedStop?.Id;
        ApplyFilter();
        var restored = selectedId is null
            ? null
            : FilteredStops.FirstOrDefault(s => s.Id == selectedId)
              ?? FilteredStops.FirstOrDefault();
        SelectedStop = null;
        SelectedStop = restored;
    }

    private void ApplyFilter()
    {
        var q = SearchQuery.Trim();
        FilteredStops.Clear();
        IEnumerable<ManagedStopTemplateItem> source = _allStops
            .OrderBy(s => s.StopCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StopNameItcs, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(q))
        {
            source = source.Where(s =>
                (s.StopCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.StopNameItcs?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.DirectionDescription?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.VrrStopId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                AnnouncementsForStop(s.Id).Any(a =>
                    (a.AnnouncementCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (a.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (a.EmbeddedSoundFileName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        foreach (var s in source)
        {
            FilteredStops.Add(s);
        }

        if (SelectedStop is not null && !FilteredStops.Any(s => s.Id == SelectedStop.Id))
        {
            SelectedStop = FilteredStops.FirstOrDefault();
        }
    }

    private string? ValidateAll(JsonObject packageRoot)
    {
        var stopCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stop in _allStops)
        {
            var code = PlannerStopCode.Normalize(stop.StopCode);
            if (!PlannerStopCode.IsValid(code))
            {
                return $"Haltestelle „{stop.StopNameItcs}“: ID muss genau 5 Ziffern haben (z. B. 00042).";
            }

            if (!stopCodes.Add(code))
            {
                return $"Doppelte Haltestellen-ID {code} – jede ID darf nur einmal vorkommen.";
            }

            stop.StopCode = code;

            if (string.IsNullOrWhiteSpace(stop.StopNameItcs))
            {
                return $"Haltestelle {code}: Name fehlt.";
            }
        }

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
                return $"Ansage {code}: Keine Haltestelle zugeordnet.";
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

        return null;
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
