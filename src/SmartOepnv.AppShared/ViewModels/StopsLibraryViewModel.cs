using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.AppShared.ViewModels;

public partial class StopsLibraryViewModel : ObservableObject, IEditorAreaViewModel
{
    private const int SuccessFeedbackMs = 5000;

    private readonly List<ManagedStopTemplateItem> _allTemplates = [];
    private readonly EditorAreaSyncState _sync = new();
    private readonly SearchQueryDebouncer _searchDebouncer;
    private string? _loadedFingerprint;
    private CancellationTokenSource? _saveButtonFeedbackCts;

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private ManagedStopTemplateItem? selectedTemplate;
    [ObservableProperty] private string? selectedRouteForInsert;
    [ObservableProperty] private string selectedAnnouncementCoordinates = string.Empty;
    [ObservableProperty] private string selectedStopCoordinates = string.Empty;
    [ObservableProperty] private string selectedVrrStopId = string.Empty;
    [ObservableProperty] private string announcementMergePauseSeconds = "0,3";
    [ObservableProperty] private bool saveButtonIsSuccess;
    [ObservableProperty] private bool removeTemplateButtonIsSuccess;
    [ObservableProperty] private bool insertIntoRouteButtonIsSuccess;

    private bool _syncingCoordinates;

    public ObservableCollection<ManagedStopTemplateItem> FilteredTemplates { get; } = [];
    public ObservableCollection<string> AvailableRoutes { get; } = [];

    public StopsLibraryViewModel()
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
        if (!_sync.ShouldRefresh(_allTemplates.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
    {
        _allTemplates.Clear();
        FilteredTemplates.Clear();
        AvailableRoutes.Clear();
        SelectedTemplate = null;
        SelectedRouteForInsert = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket – „Neue Haltestelle“ oder unter Routen „Route hinzufügen“ legt ein leeres Paket an.";
            return;
        }

        foreach (var route in editor.RouteNames)
        {
            AvailableRoutes.Add(route);
        }

        SelectedRouteForInsert = AvailableRoutes.FirstOrDefault();

        var fromManaged = 0;
        foreach (var template in editor.StopTemplates)
        {
            if (template.IsEmptyDraft())
            {
                continue;
            }

            var clone = Clone(template);
            CoordinateFormatting.NormalizeTemplate(clone);
            _allTemplates.Add(clone);
            fromManaged++;
        }

        var merge = MergeRouteStopsIntoLibrary();
        ApplyFilter();
        SelectedTemplate = FilteredTemplates.FirstOrDefault();
        SyncCoordinateFieldsFromSelected();
        StatusMessage = BuildLibraryStatusMessage(fromManaged, merge);
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
        JsonSerializer.Serialize(_allTemplates.Select(t => new
        {
            t.Id,
            t.StopCode,
            t.StopNameItcs,
            t.StopDisplay,
            t.VrrStopId,
            t.DirectionDescription,
            t.Lines,
            t.AnnouncementLat,
            t.AnnouncementLng,
            t.StopLat,
            t.StopLng,
            t.RadiusMeters,
            t.ExternalSoundUri,
            t.EmbeddedSoundFileName,
            t.LocalAudioPath
        }));

    private string BuildLibraryStatusMessage(int fromManaged, StopTemplateRouteMerger.MergeResult merge)
    {
        var total = _allTemplates.Count;
        if (merge.RouteStopCount == 0)
        {
            return $"{total} Vorlagen – keine Haltestellen in den Routen gefunden.";
        }

        if (merge.Added == 0)
        {
            return
                $"{total} Vorlagen ({fromManaged} aus Verwaltung, {merge.RouteStopCount} Haltestellen in Routen abgeglichen).";
        }

        return
            $"{total} Vorlagen ({fromManaged} Verwaltung, {merge.Added} neu aus Routen). Richtung/VRR ggf. ergänzen – „Speichern“ nicht vergessen.";
    }

    private StopTemplateRouteMerger.MergeResult MergeRouteStopsIntoLibrary(string? onlyRouteName = null)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return new StopTemplateRouteMerger.MergeResult(0, 0, 0);
        }

        foreach (var template in _allTemplates)
        {
            template.StopCode = PlannerStopCode.Normalize(template.StopCode);
        }

        return StopTemplateRouteMerger.MergeAllRouteStops(_allTemplates, editor, onlyRouteName);
    }

    public void CommitChanges()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        EnsureStopNamesBeforeCommit();

        if (!TryApplyCoordinateFields(out var validationError))
        {
            StatusMessage = validationError ?? "Koordinaten ungültig.";
            return;
        }

        PruneEmptyDrafts();

        foreach (var template in _allTemplates)
        {
            template.StopCode = PlannerStopCode.Normalize(template.StopCode);
            CoordinateFormatting.NormalizeTemplate(template);
        }

        var persistable = _allTemplates.Where(t => !t.IsEmptyDraft()).Select(Clone).ToList();
        editor.ReplaceStopTemplates(persistable);
        var routeStopsUpdated = StopTemplateRouteMerger.ApplyTemplatesToRouteStops(editor, persistable);
        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        // Ton aus der Soundbibliothek: nur Dateiname verknüpfen. Neu einbetten nur bei LocalAudioPath
        // oder wenn die Datei noch nicht in embeddedSounds liegt.
        if (editor.NeedsEmbeddedSoundMaterialization(_allTemplates, workspace))
        {
            editor.SyncEmbeddedSoundsFromStopTemplates(_allTemplates, workspace);
        }

        AppServices.Routes.ApplyEditorChanges("haltestellen", rebuildEmbeddedMedia: false);

        foreach (var t in _allTemplates)
        {
            t.LocalAudioPath = null;
        }

        SyncCoordinateFieldsFromSelected();
        RefreshTemplateListLabels(rebuildFilter: false);
        StatusMessage = routeStopsUpdated > 0
            ? $"{_allTemplates.Count} Vorlagen gespeichert – {routeStopsUpdated} Haltestelle(n) in Routen aktualisiert (ID, Name, GPS, Ton …)."
            : $"{_allTemplates.Count} Vorlagen lokal gespeichert – werden mit Routen-Export/Dropbox übertragen.";
        SaveButtonIsSuccess = true;
        RemoveTemplateButtonIsSuccess = false;
        _ = ShowSaveSuccessFeedbackAsync();
        _sync.AfterCommit();
        _loadedFingerprint = ComputeFingerprint();
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
            SaveButtonIsSuccess = false;
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

    private void MarkDirty()
    {
        _sync.MarkDirty();
        if (SaveButtonIsSuccess)
        {
            CancelSaveButtonFeedback();
            SaveButtonIsSuccess = false;
        }

        if (RemoveTemplateButtonIsSuccess)
        {
            RemoveTemplateButtonIsSuccess = false;
        }

        if (InsertIntoRouteButtonIsSuccess)
        {
            InsertIntoRouteButtonIsSuccess = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => _searchDebouncer.Schedule();

    partial void OnSelectedTemplateChanged(ManagedStopTemplateItem? value)
    {
        InsertIntoRouteButtonIsSuccess = false;
        SyncCoordinateFieldsFromSelected();
    }

    partial void OnSelectedRouteForInsertChanged(string? value) => InsertIntoRouteButtonIsSuccess = false;

    partial void OnSelectedAnnouncementCoordinatesChanged(string value)
    {
        if (_syncingCoordinates || SelectedTemplate is null)
        {
            return;
        }

        ApplyCoordinatePair(value, (lat, lon) =>
        {
            SelectedTemplate.AnnouncementLat = lat;
            SelectedTemplate.AnnouncementLng = lon;
        });
        MarkDirty();
    }

    partial void OnSelectedVrrStopIdChanged(string value)
    {
        if (_syncingCoordinates || SelectedTemplate is null)
        {
            return;
        }

        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(SelectedTemplate.VrrStopId, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        SelectedTemplate.VrrStopId = trimmed;
        MarkDirty();
    }

    partial void OnSelectedStopCoordinatesChanged(string value)
    {
        if (_syncingCoordinates || SelectedTemplate is null)
        {
            return;
        }

        ApplyCoordinatePair(value, (lat, lon) =>
        {
            SelectedTemplate.StopLat = lat;
            SelectedTemplate.StopLng = lon;
        });
        MarkDirty();
    }

    private void SyncCoordinateFieldsFromSelected()
    {
        _syncingCoordinates = true;
        if (SelectedTemplate is null)
        {
            SelectedAnnouncementCoordinates = string.Empty;
            SelectedStopCoordinates = string.Empty;
            SelectedVrrStopId = string.Empty;
        }
        else
        {
            SelectedAnnouncementCoordinates = CoordinateFormatting.FormatFromParts(
                SelectedTemplate.AnnouncementLat,
                SelectedTemplate.AnnouncementLng);
            SelectedStopCoordinates = CoordinateFormatting.FormatFromParts(
                SelectedTemplate.StopLat,
                SelectedTemplate.StopLng);
            SelectedVrrStopId = SelectedTemplate.VrrStopId;
        }

        _syncingCoordinates = false;
    }

    private static void ApplyCoordinatePair(string raw, Action<string, string> apply)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            apply(string.Empty, string.Empty);
            return;
        }

        if (CoordinateFormatting.TryParsePair(raw, out var lat, out var lon))
        {
            apply(lat, lon);
        }
    }

    private bool TryApplyCoordinateFields(out string? error)
    {
        error = null;
        if (SelectedTemplate is null)
        {
            return true;
        }

        if (!TryValidateCoordinateField(SelectedAnnouncementCoordinates, "Ansage-GPS", out error))
        {
            return false;
        }

        if (!TryValidateCoordinateField(SelectedStopCoordinates, "Haltestellen-GPS", out error))
        {
            return false;
        }

        ApplyCoordinatePair(SelectedAnnouncementCoordinates, (lat, lon) =>
        {
            SelectedTemplate!.AnnouncementLat = lat;
            SelectedTemplate.AnnouncementLng = lon;
        });
        ApplyCoordinatePair(SelectedStopCoordinates, (lat, lon) =>
        {
            SelectedTemplate!.StopLat = lat;
            SelectedTemplate.StopLng = lon;
        });
        return true;
    }

    private static bool TryValidateCoordinateField(string raw, string label, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (CoordinateFormatting.TryParsePair(raw, out _, out _))
        {
            return true;
        }

        error = $"{label}: Format „Breite, Länge“ (6 Nachkommastellen), z. B. 51.172805, 6.456152";
        return false;
    }

    [RelayCommand]
    private void AddTemplate()
    {
        if (!AppServices.Routes.EnsureEmptyPackageIfNeeded())
        {
            StatusMessage = "Leeres Route-Paket konnte nicht angelegt werden.";
            return;
        }

        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        // Nach erstem Anlegen: Listen/Status aktualisieren (z. B. neuer Betrieb).
        if (AvailableRoutes.Count == 0 && _allTemplates.Count == 0)
        {
            RefreshFromEditor();
        }

        var code = PlannerStopCode.SuggestNext(_allTemplates.Select(t => t.StopCode));
        var item = new ManagedStopTemplateItem
        {
            StopCode = code,
            StopNameItcs = string.Empty,
            RadiusMeters = ManagedStopTemplateItem.DefaultRadiusMeters
        };
        _allTemplates.Add(item);
        ApplyFilter();
        SelectedTemplate = FilteredTemplates.FirstOrDefault(t => t.Id == item.Id);
        StatusMessage = $"Neue Vorlage ({code}) – ITCS-Name ergänzen und speichern.";
        MarkDirty();
    }

    private void EnsureStopNamesBeforeCommit()
    {
        foreach (var template in _allTemplates)
        {
            if (!string.IsNullOrWhiteSpace(template.StopNameItcs))
            {
                continue;
            }

            if (PlannerStopCode.IsValid(template.StopCode))
            {
                template.StopNameItcs = $"Haltestelle {PlannerStopCode.Normalize(template.StopCode)}";
            }
        }
    }

    private void PruneEmptyDrafts()
    {
        var removed = _allTemplates.RemoveAll(t => t.IsEmptyDraft());
        if (removed > 0)
        {
            ApplyFilter();
            if (SelectedTemplate is not null &&
                !_allTemplates.Any(t => t.Id == SelectedTemplate.Id))
            {
                SelectedTemplate = FilteredTemplates.FirstOrDefault();
            }
        }
    }

    [RelayCommand]
    private void DuplicateTemplate()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Vorlage auswählen.";
            return;
        }

        var copy = Clone(SelectedTemplate);
        copy.Id = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(copy.StopNameItcs))
        {
            copy.StopNameItcs += " (Kopie)";
        }

        _allTemplates.Add(copy);
        ApplyFilter();
        SelectedTemplate = FilteredTemplates.FirstOrDefault(t => t.Id == copy.Id);
        StatusMessage = "Vorlage dupliziert – bitte speichern.";
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveTemplate()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Vorlage auswählen.";
            return;
        }

        _allTemplates.RemoveAll(t => t.Id == SelectedTemplate.Id);
        ApplyFilter();
        SelectedTemplate = FilteredTemplates.FirstOrDefault();
        StatusMessage = "Vorlage entfernt – „Speichern“ nicht vergessen.";
        _sync.MarkDirty();
        RemoveTemplateButtonIsSuccess = true;
        SaveButtonIsSuccess = false;
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    [RelayCommand]
    private void PickVrrStop()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        try
        {
            var prefill = VrrStopAssignmentManager.PrefillQuery(
                SelectedTemplate.StopNameItcs,
                SelectedTemplate.VrrStopId);
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var dialog = new VrrStopFinderDialog(prefill)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true || dialog.SelectedEntry is null)
            {
                return;
            }

            var assignment = VrrStopAssignmentManager.FromCatalogEntry(dialog.SelectedEntry);
            VrrStopAssignmentManager.ApplyToTemplate(SelectedTemplate, assignment);
            SyncCoordinateFieldsFromSelected();
            StatusMessage = $"VRR-ID „{SelectedTemplate.VrrStopId}“ übernommen ({assignment.DisplayName}).";
            MarkDirty();
        }
        catch (Exception ex)
        {
            StatusMessage = $"VRR-Suche fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickAnnouncementCoordinatesOnMap() =>
        OpenGpsMapPicker(
            "Ansage (GPS)",
            formatCurrent: t => CoordinateFormatting.FormatFromParts(t.AnnouncementLat, t.AnnouncementLng),
            formatOther: t => CoordinateFormatting.FormatFromParts(t.StopLat, t.StopLng),
            "Haltestelle",
            apply: (lat, lon) =>
            {
                SelectedTemplate!.AnnouncementLat = lat;
                SelectedTemplate.AnnouncementLng = lon;
                SyncCoordinateFieldsFromSelected();
            });

    [RelayCommand]
    private void PickStopCoordinatesOnMap() =>
        OpenGpsMapPicker(
            "Haltestelle (GPS)",
            formatCurrent: t => CoordinateFormatting.FormatFromParts(t.StopLat, t.StopLng),
            formatOther: t => CoordinateFormatting.FormatFromParts(t.AnnouncementLat, t.AnnouncementLng),
            "Ansage",
            apply: (lat, lon) =>
            {
                SelectedTemplate!.StopLat = lat;
                SelectedTemplate.StopLng = lon;
                SyncCoordinateFieldsFromSelected();
            });

    private void OpenGpsMapPicker(
        string title,
        Func<ManagedStopTemplateItem, string> formatCurrent,
        Func<ManagedStopTemplateItem, string> formatOther,
        string otherLabel,
        Action<string, string> apply)
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        try
        {
            TryApplyCoordinateFields(out _);

            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var template = SelectedTemplate;
            var radius = template!.RadiusMeters > 0
                ? template.RadiusMeters
                : ManagedStopTemplateItem.DefaultRadiusMeters;
            var dialog = new GpsMapPickerDialog(
                title,
                formatCurrent(template),
                formatOther(template),
                otherLabel,
                radiusMeters: radius)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            if (CoordinateFormatting.TryParsePair(dialog.SelectedCoordinates, out var lat, out var lon))
            {
                apply(lat, lon);
                MarkDirty();
            }

            StatusMessage = $"{title} auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    private void RefreshSelectedTemplateBinding() => RefreshTemplateListLabels(rebuildFilter: false);

    /// <summary>ListBox zeigt DisplayLabel – nach Speichern/Ton-Änderung Labels aktualisieren ohne Listen-Reset.</summary>
    private void RefreshTemplateListLabels(bool rebuildFilter = true)
    {
        var selectedId = SelectedTemplate?.Id;
        if (rebuildFilter)
        {
            ApplyFilter();
        }
        else
        {
            foreach (var template in FilteredTemplates)
            {
                template.NotifyDisplayLabelChanged();
            }
        }

        if (selectedId is null || SelectedTemplate?.Id == selectedId)
        {
            return;
        }

        SelectedTemplate = FilteredTemplates.FirstOrDefault(t => t.Id == selectedId)
                           ?? FilteredTemplates.FirstOrDefault();
    }

    [RelayCommand]
    private void PickEmbeddedSoundFromList()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        var names = TryListEmbeddedSoundNames(out var listError);
        if (names is null)
        {
            StatusMessage = listError ?? "Keine Ansagen verfügbar.";
            return;
        }

        var prefill = SelectedTemplate.EmbeddedSoundFileName?.Trim();
        if (string.IsNullOrEmpty(prefill))
        {
            prefill = SelectedTemplate.StopNameItcs?.Trim();
        }

        var owner = ResolveDialogOwner();
        var searchHints = BuildEmbeddedSoundSearchHints(names);
        var dialog = new EmbeddedSoundPickerDialog(names, prefill, searchHints) { Owner = owner };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedFileName))
        {
            return;
        }

        SelectedTemplate.EmbeddedSoundFileName = dialog.SelectedFileName.Trim();
        SelectedTemplate.LocalAudioPath = null;
        RefreshSelectedTemplateBinding();
        StatusMessage =
            $"Ansage „{SelectedTemplate.EmbeddedSoundFileName}“ verknüpft (bereits in Soundbibliothek) – mit „Speichern“ übernehmen.";
        MarkDirty();
    }

    [RelayCommand]
    private void MergeEmbeddedSoundsFromList()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
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

        var names = TryListEmbeddedSoundNames(out var listError);
        if (names is null)
        {
            StatusMessage = listError ?? "Keine Ansagen verfügbar.";
            return;
        }

        if (!TryParsePauseSeconds(AnnouncementMergePauseSeconds, out var pauseSeconds, out var pauseError))
        {
            StatusMessage = pauseError ?? "Pause ungültig.";
            return;
        }

        var prefill = SelectedTemplate.EmbeddedSoundFileName?.Trim();
        if (string.IsNullOrEmpty(prefill))
        {
            prefill = SelectedTemplate.StopNameItcs?.Trim();
        }

        var owner = ResolveDialogOwner();
        var searchHints = BuildEmbeddedSoundSearchHints(names);
        var dialog = new EmbeddedSoundMultiPickerDialog(names, prefill, searchHintsByFileName: searchHints) { Owner = owner };
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

        var outputFileName = BuildMergedEmbeddedFileName(SelectedTemplate);
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

        SelectedTemplate.LocalAudioPath = outputPath;
        SelectedTemplate.EmbeddedSoundFileName = outputFileName;
        RefreshSelectedTemplateBinding();
        StatusMessage =
            $"{dialog.SelectedFileNames.Count} Schnipsel zu „{outputFileName}“ zusammengefügt" +
            (pauseSeconds > 0 ? $" (Pause {pauseSeconds:0.###} s)" : string.Empty) +
            " – mit „Speichern“ übernehmen.";
        MarkDirty();
    }

    private IReadOnlyList<string>? TryListEmbeddedSoundNames(out string? error)
    {
        error = null;
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            error = "Kein Route-Paket geladen.";
            return null;
        }

        var extraNames = _allTemplates
            .Select(t => t.EmbeddedSoundFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n));
        var names = EmbeddedSoundCatalog.ListReferenced(
            editor,
            editor.PackageRoot,
            AppServices.IsInitialized ? AppServices.Workspace : null,
            extraNames)
            .ToList();

        foreach (var template in _allTemplates)
        {
            var fileName = template.EmbeddedSoundFileName?.Trim();
            if (string.IsNullOrWhiteSpace(fileName) ||
                names.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(template.LocalAudioPath) && File.Exists(template.LocalAudioPath))
            {
                names.Add(fileName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        if (names.Count == 0)
        {
            error = "Keine eingebetteten Ansagen – zuerst unter „Ansagen“ Tondatei anlegen und speichern.";
            return null;
        }

        return names;
    }

    private Dictionary<string, string> BuildEmbeddedSoundSearchHints(IReadOnlyList<string> fileNames)
    {
        var hints = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

        void Append(string? fileName, params string?[] parts)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var text = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!hints.TryGetValue(fileName, out var builder))
            {
                builder = new StringBuilder();
                hints[fileName] = builder;
            }

            builder.Append(' ').Append(text);
        }

        foreach (var template in _allTemplates)
        {
            Append(
                template.EmbeddedSoundFileName,
                template.StopNameItcs,
                template.StopDisplay,
                template.DirectionDescription,
                template.Lines);
        }

        var editor = AppServices.Routes.Editor;
        if (editor is not null)
        {
            foreach (var announcement in ManagedAnnouncementTemplateEditor.LoadFromRoot(editor.PackageRoot))
            {
                Append(
                    announcement.EmbeddedSoundFileName,
                    announcement.DisplayName,
                    announcement.Description,
                    announcement.Lines);
            }
        }

        return fileNames.ToDictionary(
            name => name,
            name => hints.TryGetValue(name, out var builder)
                ? builder.ToString().Trim()
                : string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Window? ResolveDialogOwner()
    {
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !owner.IsLoaded)
        {
            owner = null;
        }

        return owner;
    }

    private static bool TryParsePauseSeconds(string raw, out double seconds, out string? error)
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

    private static string BuildMergedEmbeddedFileName(ManagedStopTemplateItem template)
    {
        var code = PlannerStopCode.Normalize(template.StopCode);
        if (string.IsNullOrEmpty(code))
        {
            code = "00000";
        }

        var safeName = string.Concat(
            (template.StopNameItcs ?? "ansage").Trim()
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

    [RelayCommand]
    private void PickSoundFile()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Haltestelle auswählen.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Tondatei für Haltestelle wählen",
            Filter = "Audio (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var ext = Path.GetExtension(dialog.FileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".wav";
        }

        var fileName = !string.IsNullOrWhiteSpace(SelectedTemplate.EmbeddedSoundFileName)
            ? SelectedTemplate.EmbeddedSoundFileName.Trim()
            : BuildEmbeddedFileName(SelectedTemplate.StopNameItcs, ext);

        SelectedTemplate.LocalAudioPath = dialog.FileName;
        SelectedTemplate.EmbeddedSoundFileName = fileName;
        RefreshSelectedTemplateBinding();
        StatusMessage =
            $"Ton gewählt – mit „Speichern“ wird „{fileName}“ in embeddedSounds eingetragen.";
        MarkDirty();
    }

    private static string BuildEmbeddedFileName(string? stopName, string extension)
    {
        var safe = string.Concat(
            (stopName ?? "ansage").Trim()
                .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_'));
        if (string.IsNullOrEmpty(safe))
        {
            safe = "ANSAGE";
        }

        if (safe.Length > 32)
        {
            safe = safe[..32];
        }

        return $"{safe}{extension}";
    }

    [RelayCommand]
    private void ClearEmbeddedSound()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        SelectedTemplate.EmbeddedSoundFileName = string.Empty;
        SelectedTemplate.LocalAudioPath = null;
        RefreshSelectedTemplateBinding();
        StatusMessage = "Tonzuordnung entfernt – „Speichern“ nicht vergessen.";
        MarkDirty();
    }

    [RelayCommand]
    private void InsertIntoRoute()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Vorlage auswählen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedRouteForInsert))
        {
            StatusMessage = "Bitte zuerst eine Route wählen (unter Routen anlegen, falls leer).";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedTemplate.StopNameItcs))
        {
            StatusMessage = "ITCS-Name fehlt – Vorlage zuerst benennen.";
            return;
        }

        editor.AddStopFromTemplate(SelectedRouteForInsert, Clone(SelectedTemplate));
        AppServices.Routes.ApplyEditorChanges("haltestellen-route", rebuildEmbeddedMedia: false);
        var code = PlannerStopCode.Normalize(SelectedTemplate.StopCode);
        var codeHint = string.IsNullOrEmpty(code) ? string.Empty : $" (ID {code})";
        StatusMessage =
            $"„{SelectedTemplate.StopNameItcs}“{codeHint} in Route „{SelectedRouteForInsert}“ eingefügt – unter „Routen“ bearbeitbar.";
        InsertIntoRouteButtonIsSuccess = true;
    }

    [RelayCommand]
    private void ImportFromAllRoutes()
    {
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var merge = MergeRouteStopsIntoLibrary();
        RefreshTemplateListLabels();
        StatusMessage = merge.Added > 0
            ? $"{merge.Added} neue Vorlage(n) aus allen Routen – insgesamt {_allTemplates.Count}. Bitte „Speichern“."
            : $"Keine neuen Haltestellen – {_allTemplates.Count} Vorlagen, {merge.RouteStopCount} Haltestellen in Routen bereits abgeglichen.";
        if (merge.Added > 0)
        {
            MarkDirty();
        }
    }

    [RelayCommand]
    private void ImportFromSelectedRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRouteForInsert))
        {
            StatusMessage = "Bitte Route wählen, aus der importiert werden soll.";
            return;
        }

        if (AppServices.Routes.Editor is null)
        {
            return;
        }

        var merge = MergeRouteStopsIntoLibrary(SelectedRouteForInsert);
        if (merge.RouteStopCount == 0)
        {
            StatusMessage = $"Route „{SelectedRouteForInsert}“ hat keine Haltestellen zum Importieren.";
            return;
        }

        RefreshTemplateListLabels();
        StatusMessage = merge.Added > 0
            ? $"{merge.Added} neue Vorlage(n) aus „{SelectedRouteForInsert}“ – insgesamt {_allTemplates.Count}. Bitte „Speichern“."
            : $"Route „{SelectedRouteForInsert}“ abgeglichen ({merge.RouteStopCount} Haltestellen, keine neuen Einträge).";
        if (merge.Added > 0)
        {
            MarkDirty();
        }
    }

    private void ApplyFilter()
    {
        var q = SearchQuery.Trim();
        FilteredTemplates.Clear();
        IEnumerable<ManagedStopTemplateItem> source = _allTemplates
            .OrderBy(t => t.StopCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.StopNameItcs, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(q))
        {
            source = source.Where(t =>
                (t.StopCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.StopNameItcs?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.StopDisplay?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.VrrStopId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.DirectionDescription?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Lines?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var t in source)
        {
            FilteredTemplates.Add(t);
        }

        if (SelectedTemplate is not null &&
            !FilteredTemplates.Any(t => t.Id == SelectedTemplate.Id))
        {
            SelectedTemplate = FilteredTemplates.FirstOrDefault();
        }
    }

    private static ManagedStopTemplateItem Clone(ManagedStopTemplateItem source) => new()
    {
        Id = source.Id,
        StopCode = source.StopCode,
        StopNameItcs = source.StopNameItcs,
        StopDisplay = source.StopDisplay,
        VrrStopId = source.VrrStopId,
        DirectionDescription = source.DirectionDescription,
        Lines = source.Lines,
        AnnouncementLat = source.AnnouncementLat,
        AnnouncementLng = source.AnnouncementLng,
        StopLat = source.StopLat,
        StopLng = source.StopLng,
        RadiusMeters = source.RadiusMeters,
        ExternalSoundUri = source.ExternalSoundUri,
        EmbeddedSoundFileName = source.EmbeddedSoundFileName,
        LocalAudioPath = source.LocalAudioPath
    };
}
