using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.AppShared.ViewModels;

public partial class StopsLibraryViewModel : ObservableObject
{
    private readonly List<ManagedStopTemplateItem> _allTemplates = [];

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private ManagedStopTemplateItem? selectedTemplate;
    [ObservableProperty] private string? selectedRouteForInsert;
    [ObservableProperty] private string selectedAnnouncementCoordinates = string.Empty;
    [ObservableProperty] private string selectedStopCoordinates = string.Empty;

    private bool _syncingCoordinates;

    public ObservableCollection<ManagedStopTemplateItem> FilteredTemplates { get; } = [];
    public ObservableCollection<string> AvailableRoutes { get; } = [];

    public StopsLibraryViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChanges);
        }
    }

    public void RefreshFromEditor()
    {
        _allTemplates.Clear();
        FilteredTemplates.Clear();
        AvailableRoutes.Clear();
        SelectedTemplate = null;
        SelectedRouteForInsert = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
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
    }

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
        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        editor.SyncEmbeddedSoundsFromStopTemplates(_allTemplates, workspace);
        AppServices.Routes.ApplyEditorChanges("haltestellen");

        foreach (var t in _allTemplates)
        {
            t.LocalAudioPath = null;
        }

        SyncCoordinateFieldsFromSelected();
        RefreshTemplateListLabels();
        StatusMessage =
            $"{_allTemplates.Count} Vorlagen lokal gespeichert – werden mit Routen-Export/Dropbox übertragen.";
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedTemplateChanged(ManagedStopTemplateItem? value) => SyncCoordinateFieldsFromSelected();

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
    }

    private void SyncCoordinateFieldsFromSelected()
    {
        _syncingCoordinates = true;
        if (SelectedTemplate is null)
        {
            SelectedAnnouncementCoordinates = string.Empty;
            SelectedStopCoordinates = string.Empty;
        }
        else
        {
            SelectedAnnouncementCoordinates = CoordinateFormatting.FormatFromParts(
                SelectedTemplate.AnnouncementLat,
                SelectedTemplate.AnnouncementLng);
            SelectedStopCoordinates = CoordinateFormatting.FormatFromParts(
                SelectedTemplate.StopLat,
                SelectedTemplate.StopLng);
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
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
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
            RefreshSelectedTemplateBinding();
            StatusMessage = $"VRR-ID „{SelectedTemplate.VrrStopId}“ übernommen ({assignment.DisplayName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"VRR-Suche fehlgeschlagen: {ex.Message}";
        }
    }

    private void RefreshSelectedTemplateBinding() => RefreshTemplateListLabels();

    /// <summary>ListBox zeigt DisplayLabel – nach Speichern/Ton-Änderung Liste und Auswahl neu binden.</summary>
    private void RefreshTemplateListLabels()
    {
        var selectedId = SelectedTemplate?.Id;
        ApplyFilter();
        var restored = selectedId is null
            ? null
            : FilteredTemplates.FirstOrDefault(t => t.Id == selectedId)
              ?? FilteredTemplates.FirstOrDefault();
        SelectedTemplate = null;
        SelectedTemplate = restored;
    }

    [RelayCommand]
    private void PickEmbeddedSoundFromList()
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

        var extra = editor.AnnouncementTemplates
            .Select(t => t.EmbeddedSoundFileName)
            .Concat(_allTemplates.Select(t => t.EmbeddedSoundFileName));
        var names = EmbeddedSoundCatalog.ListAvailable(
            editor.PackageRoot,
            AppServices.IsInitialized ? AppServices.Workspace : null,
            extra);

        if (names.Count == 0)
        {
            StatusMessage = "Keine eingebetteten Ansagen – zuerst unter „Ansagen“ Tondatei anlegen und speichern.";
            return;
        }

        var prefill = SelectedTemplate.EmbeddedSoundFileName?.Trim();
        if (string.IsNullOrEmpty(prefill))
        {
            prefill = SelectedTemplate.StopNameItcs?.Trim();
        }

        var owner = Application.Current?.MainWindow;
        if (owner is not null && !owner.IsLoaded)
        {
            owner = null;
        }

        var dialog = new EmbeddedSoundPickerDialog(names, prefill) { Owner = owner };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedFileName))
        {
            return;
        }

        SelectedTemplate.EmbeddedSoundFileName = dialog.SelectedFileName.Trim();
        SelectedTemplate.LocalAudioPath = null;
        RefreshSelectedTemplateBinding();
        StatusMessage = $"Ansage „{SelectedTemplate.EmbeddedSoundFileName}“ zugeordnet – mit „Speichern“ übernehmen.";
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
        AppServices.Routes.ApplyEditorChanges("haltestellen-route");
        StatusMessage =
            $"„{SelectedTemplate.StopNameItcs}“ in Route „{SelectedRouteForInsert}“ eingefügt – unter „Routen“ bearbeitbar.";
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
                (t.DirectionDescription?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
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
