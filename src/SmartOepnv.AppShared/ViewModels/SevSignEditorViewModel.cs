using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Sev;
using SmartOepnv.Core;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.AppShared.ViewModels;

public partial class SevStopItem : ObservableObject
{
    public SevStopItem(string name) => _name = name;

    [ObservableProperty] private string _name;
}

public partial class SevOperatorSelectionItem(SevOperatorOption option) : ObservableObject
{
    public SevOperatorOption Option { get; } = option;

    [ObservableProperty] private bool _isSelected;
}

public partial class SevSignEditorViewModel : EditorStatusViewModelBase
{
    private string? _loadedDraftId;
    private string _lastSuggestedDraftName = string.Empty;
    private bool _suppressDraftNameSync;

    public SevSignEditorViewModel() : base("SEV-Schilder als PDF im NRW-Standardlayout erstellen.")
    {
        foreach (var option in SevOperatorCatalog.All)
        {
            OperatorSelections.Add(new SevOperatorSelectionItem(option));
        }

        ReloadDraftList();
    }

    [ObservableProperty] private string line = "S 28";

    [ObservableProperty] private string destination = "Düsseldorf, Hauptbahnhof";

    [ObservableProperty] private string newStopName = string.Empty;

    [ObservableProperty] private string draftName = string.Empty;

    [ObservableProperty] private SevStopItem? selectedStop;

    [ObservableProperty] private string? selectedRoute;

    [ObservableProperty] private bool importRouteReverse;

    [ObservableProperty] private SevSignDraft? selectedDraft;

    public ObservableCollection<SevOperatorSelectionItem> OperatorSelections { get; } = [];

    public ObservableCollection<SevStopItem> Stops { get; } = [];

    public ObservableCollection<string> Routes { get; } = [];

    public ObservableCollection<SevSignDraft> SavedDrafts { get; } = [];

    public bool HasRoutes => Routes.Count > 0;

    public bool HasSavedDrafts => SavedDrafts.Count > 0;

    public bool CanUpdateLoadedDraft => !string.IsNullOrEmpty(_loadedDraftId);

    partial void OnSelectedDraftChanged(SevSignDraft? value)
    {
        if (value is not null)
        {
            DraftName = value.Name;
        }
    }

    partial void OnDraftNameChanged(string value)
    {
        if (_suppressDraftNameSync)
        {
            return;
        }

        if (_loadedDraftId is null)
        {
            return;
        }

        var loaded = GetLoadedDraft();
        if (loaded is not null &&
            !string.Equals(loaded.Name, value.Trim(), StringComparison.Ordinal))
        {
            _loadedDraftId = null;
            NotifyDraftCommands();
        }
    }

    partial void OnLineChanged(string value) => SyncDraftNameAfterContentChange();

    partial void OnDestinationChanged(string value) => SyncDraftNameAfterContentChange();

    public void RefreshFromEditor()
    {
        Routes.Clear();
        OnPropertyChanged(nameof(HasRoutes));
        ReloadDraftList();

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            SelectedRoute = null;
            if (Stops.Count == 0)
            {
                StatusMessage = "Kein Route-Paket geladen – Haltestellen manuell oder nach Import unter Übersicht.";
            }

            return;
        }

        foreach (var route in editor.RouteNames)
        {
            Routes.Add(route);
        }

        OnPropertyChanged(nameof(HasRoutes));
        SelectedRoute ??= Routes.FirstOrDefault();
    }

    private SevSignDraft? GetLoadedDraft() =>
        _loadedDraftId is null
            ? null
            : SavedDrafts.FirstOrDefault(d => d.Id == _loadedDraftId);

    private void SetDraftNameSilently(string name)
    {
        _suppressDraftNameSync = true;
        DraftName = name;
        _suppressDraftNameSync = false;
    }

    private void SyncDraftNameAfterContentChange()
    {
        var suggested = SevSignDraft.SuggestName(Line, Destination);
        var loaded = GetLoadedDraft();
        var currentName = DraftName.Trim();

        if (currentName.Length == 0 ||
            (loaded is not null &&
             string.Equals(currentName, loaded.Name, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(currentName, _lastSuggestedDraftName, StringComparison.OrdinalIgnoreCase))
        {
            SetDraftNameSilently(suggested);
        }

        _lastSuggestedDraftName = suggested;

        if (loaded is not null && !EditorContentEquals(loaded))
        {
            _loadedDraftId = null;
            NotifyDraftCommands();
        }
    }

    private bool EditorContentEquals(SevSignDraft draft)
    {
        if (!string.Equals(Line.Trim(), draft.Line, StringComparison.Ordinal) ||
            !string.Equals(Destination.Trim(), draft.Destination, StringComparison.Ordinal) ||
            ImportRouteReverse != draft.ImportRouteReverse ||
            !string.Equals(SelectedRoute, draft.SourceRoute, StringComparison.Ordinal))
        {
            return false;
        }

        var stopNames = Stops.Select(s => s.Name).ToList();
        if (stopNames.Count != draft.Stops.Count)
        {
            return false;
        }

        for (var i = 0; i < stopNames.Count; i++)
        {
            if (!string.Equals(stopNames[i], draft.Stops[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private SevSignDraftStore? TryGetDraftStore()
    {
        if (!AppServices.IsInitialized || AppServices.SevSignDrafts is null)
        {
            StatusMessage = "Lokale Vorlagen sind nur im Planer verfügbar.";
            return null;
        }

        return AppServices.SevSignDrafts;
    }

    private void ReloadDraftList()
    {
        SavedDrafts.Clear();
        OnPropertyChanged(nameof(HasSavedDrafts));

        if (!AppServices.IsInitialized || AppServices.SevSignDrafts is null)
        {
            return;
        }

        foreach (var draft in AppServices.SevSignDrafts.LoadAll())
        {
            SavedDrafts.Add(draft);
        }

        OnPropertyChanged(nameof(HasSavedDrafts));
        if (SelectedDraft is not null)
        {
            SelectedDraft = SavedDrafts.FirstOrDefault(d => d.Id == SelectedDraft.Id);
        }
    }

    [RelayCommand]
    private void SaveDraftAsNew()
    {
        PersistDraft(Guid.NewGuid().ToString("N"), isNew: true);
    }

    [RelayCommand(CanExecute = nameof(CanUpdateLoadedDraft))]
    private void UpdateDraft()
    {
        if (_loadedDraftId is null)
        {
            StatusMessage = "Keine geladene Vorlage – bitte „Neue Vorlage speichern“ verwenden.";
            return;
        }

        PersistDraft(_loadedDraftId, isNew: false);
    }

    private void PersistDraft(string id, bool isNew)
    {
        var store = TryGetDraftStore();
        if (store is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Line) && string.IsNullOrWhiteSpace(Destination) && Stops.Count == 0)
        {
            StatusMessage = "Nichts zu speichern – bitte Linie, Ziel oder Haltestellen eingeben.";
            return;
        }

        var name = DraftName.Trim();
        if (name.Length == 0)
        {
            name = SevSignDraft.SuggestName(Line, Destination);
        }

        if (!isNew)
        {
            var duplicateName = SavedDrafts.FirstOrDefault(d =>
                d.Id != id &&
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (duplicateName is not null)
            {
                StatusMessage =
                    $"Der Name „{name}“ ist bereits vergeben – bitte anderen Namen wählen oder „Neue Vorlage speichern“.";
                return;
            }
        }
        else if (SavedDrafts.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = CreateUniqueDraftName(name);
        }

        var draft = BuildDraftFromEditor(id);
        draft.Name = name;
        store.Save(draft);
        _loadedDraftId = draft.Id;
        DraftName = draft.Name;
        ReloadDraftList();
        SelectedDraft = SavedDrafts.FirstOrDefault(d => d.Id == draft.Id);
        NotifyDraftCommands();
        ReportSaveSuccess(isNew
            ? $"Neue Vorlage „{draft.Name}“ gespeichert."
            : $"Vorlage „{draft.Name}“ aktualisiert.");
    }

    private static string CreateUniqueDraftName(string baseName)
    {
        var trimmed = baseName.Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "SEV-Vorlage";
        }

        return $"{trimmed} ({DateTime.Now:dd.MM.yyyy HH:mm})";
    }

    private void NotifyDraftCommands()
    {
        OnPropertyChanged(nameof(CanUpdateLoadedDraft));
        UpdateDraftCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void LoadDraft()
    {
        if (SelectedDraft is null)
        {
            StatusMessage = "Bitte zuerst eine gespeicherte Vorlage auswählen.";
            return;
        }

        ApplyDraft(SelectedDraft);
        NotifyDraftCommands();
        StatusMessage = $"Vorlage „{SelectedDraft.Name}“ geladen – „Vorlage aktualisieren“ überschreibt nur diese Vorlage.";
    }

    [RelayCommand]
    private void DeleteDraft()
    {
        var store = TryGetDraftStore();
        if (store is null)
        {
            return;
        }

        if (SelectedDraft is null)
        {
            StatusMessage = "Bitte zuerst eine Vorlage zum Löschen auswählen.";
            return;
        }

        var name = SelectedDraft.Name;
        var id = SelectedDraft.Id;
        if (!store.Delete(id))
        {
            StatusMessage = "Vorlage konnte nicht gelöscht werden.";
            return;
        }

        if (string.Equals(_loadedDraftId, id, StringComparison.Ordinal))
        {
            _loadedDraftId = null;
        }

        ReloadDraftList();
        SelectedDraft = null;
        NotifyDraftCommands();
        StatusMessage = $"Vorlage „{name}“ gelöscht.";
    }

    [RelayCommand]
    private void NewDraft()
    {
        _loadedDraftId = null;
        _lastSuggestedDraftName = string.Empty;
        SetDraftNameSilently(string.Empty);
        SelectedDraft = null;
        Line = string.Empty;
        Destination = string.Empty;
        NewStopName = string.Empty;
        SelectedRoute = Routes.FirstOrDefault();
        ImportRouteReverse = false;
        Stops.Clear();
        SelectedStop = null;
        ResetOperatorSelections([SevOperatorKind.RegioBahn]);
        NotifyDraftCommands();
        StatusMessage = "Neue leere Vorlage – Daten eingeben und „Neue Vorlage speichern“.";
    }

    private SevSignDraft BuildDraftFromEditor(string id) =>
        new()
        {
            Id = id,
            Line = Line.Trim(),
            Destination = Destination.Trim(),
            Stops = Stops.Select(s => s.Name).ToList(),
            Operators = OperatorSelections
                .Where(s => s.IsSelected)
                .Select(s => s.Option.Kind)
                .ToList(),
            SourceRoute = SelectedRoute,
            ImportRouteReverse = ImportRouteReverse
        };

    private void ApplyDraft(SevSignDraft draft)
    {
        _loadedDraftId = draft.Id;
        SetDraftNameSilently(draft.Name);
        _lastSuggestedDraftName = SevSignDraft.SuggestName(draft.Line, draft.Destination);
        Line = draft.Line;
        Destination = draft.Destination;
        ImportRouteReverse = draft.ImportRouteReverse;
        SelectedRoute = string.IsNullOrWhiteSpace(draft.SourceRoute)
            ? Routes.FirstOrDefault()
            : Routes.Contains(draft.SourceRoute)
                ? draft.SourceRoute
                : draft.SourceRoute;

        Stops.Clear();
        foreach (var stop in draft.Stops)
        {
            if (string.IsNullOrWhiteSpace(stop))
            {
                continue;
            }

            Stops.Add(new SevStopItem(stop.Trim()));
        }

        SelectedStop = Stops.FirstOrDefault();
        ResetOperatorSelections(draft.Operators);
        NotifyDraftCommands();
    }

    private void ResetOperatorSelections(IReadOnlyList<SevOperatorKind> selectedKinds)
    {
        var selected = selectedKinds.Count > 0
            ? selectedKinds.ToHashSet()
            : [SevOperatorKind.RegioBahn];

        foreach (var item in OperatorSelections)
        {
            item.IsSelected = selected.Contains(item.Option.Kind);
        }
    }

    [RelayCommand]
    private void AddStop()
    {
        var name = NewStopName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Bitte einen Haltestellennamen eingeben.";
            return;
        }

        Stops.Add(new SevStopItem(name));
        NewStopName = string.Empty;
        SelectedStop = Stops[^1];
        StatusMessage = $"Haltestelle „{name}“ hinzugefügt.";
    }

    [RelayCommand]
    private void RemoveStop()
    {
        if (SelectedStop is null)
        {
            StatusMessage = "Bitte eine Haltestelle zum Entfernen auswählen.";
            return;
        }

        var removed = SelectedStop.Name;
        var index = Stops.IndexOf(SelectedStop);
        Stops.Remove(SelectedStop);
        SelectedStop = Stops.Count == 0
            ? null
            : Stops[Math.Clamp(index, 0, Stops.Count - 1)];
        StatusMessage = $"Haltestelle „{removed}“ entfernt.";
    }

    [RelayCommand]
    private void MoveStopUp()
    {
        if (SelectedStop is null)
        {
            return;
        }

        var index = Stops.IndexOf(SelectedStop);
        if (index <= 0)
        {
            return;
        }

        Stops.Move(index, index - 1);
        StatusMessage = "Haltestelle nach oben verschoben.";
    }

    [RelayCommand]
    private void MoveStopDown()
    {
        if (SelectedStop is null)
        {
            return;
        }

        var index = Stops.IndexOf(SelectedStop);
        if (index < 0 || index >= Stops.Count - 1)
        {
            return;
        }

        Stops.Move(index, index + 1);
        StatusMessage = "Haltestelle nach unten verschoben.";
    }

    [RelayCommand]
    private void ReverseStopsDirection()
    {
        if (Stops.Count < 2)
        {
            StatusMessage = "Mindestens zwei Haltestellen nötig, um die Richtung umzukehren.";
            return;
        }

        var previousLast = Stops[^1].Name;
        var previousFirst = Stops[0].Name;
        var selectedName = SelectedStop?.Name;
        var reversed = Stops.Reverse().ToList();
        Stops.Clear();
        foreach (var stop in reversed)
        {
            Stops.Add(stop);
        }

        var dest = Destination.Trim();
        if (dest.Length == 0 ||
            string.Equals(dest, previousLast, StringComparison.OrdinalIgnoreCase) ||
            dest.Contains(previousLast, StringComparison.OrdinalIgnoreCase))
        {
            Destination = previousFirst;
        }

        SelectedStop = selectedName is null
            ? null
            : Stops.FirstOrDefault(s => s.Name == selectedName);
        SyncDraftNameAfterContentChange();
        StatusMessage = "Haltestellenreihenfolge umgekehrt (Fahrtrichtung gedreht).";
    }

    [RelayCommand]
    private void ImportFromRoute()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte zuerst unter Übersicht importieren.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Bitte zuerst eine Route auswählen.";
            return;
        }

        var routeStops = editor.GetStops(SelectedRoute).ToList();
        if (ImportRouteReverse)
        {
            routeStops.Reverse();
        }

        var imported = SevRouteImportHelper.BuildFromRoute(SelectedRoute, routeStops);
        if (imported.Stops.Count == 0)
        {
            StatusMessage = imported.Summary;
            return;
        }

        Stops.Clear();
        foreach (var stop in imported.Stops)
        {
            Stops.Add(new SevStopItem(stop));
        }

        if (!string.IsNullOrWhiteSpace(imported.Line))
        {
            Line = imported.Line;
        }

        if (!string.IsNullOrWhiteSpace(imported.Destination))
        {
            Destination = imported.Destination;
        }

        SelectedStop = Stops.FirstOrDefault();
        SyncDraftNameAfterContentChange();
        StatusMessage = ImportRouteReverse
            ? $"{imported.Summary} (Richtung umgekehrt)"
            : imported.Summary;
    }

    [RelayCommand]
    private void ExportPdf()
    {
        if (string.IsNullOrWhiteSpace(Line))
        {
            StatusMessage = "Bitte eine Linie eingeben (z. B. RE 10 oder S 28).";
            return;
        }

        if (string.IsNullOrWhiteSpace(Destination))
        {
            StatusMessage = "Bitte ein Ziel eingeben (z. B. Krefeld, Hauptbahnhof).";
            return;
        }

        if (Stops.Count == 0)
        {
            StatusMessage = "Bitte mindestens eine Haltestelle hinzufügen.";
            return;
        }

        if (!OperatorSelections.Any(s => s.IsSelected))
        {
            StatusMessage = "Bitte mindestens einen Betreiber auswählen.";
            return;
        }

        var data = BuildSignData();
        var dialog = new SaveFileDialog
        {
            Title = "SEV-Schild als PDF speichern",
            Filter = "PDF-Dateien (*.pdf)|*.pdf",
            FileName = data.SuggestFileName(),
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SevSignPdfGenerator.Generate(data, dialog.FileName);
            ReportSaveSuccess($"PDF gespeichert: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF-Export fehlgeschlagen: {ex.Message}";
        }
    }

    public SevSignData BuildSignData() =>
        new()
        {
            Line = Line,
            Destination = Destination,
            Stops = Stops.Select(s => s.Name).ToList(),
            Operators = OperatorSelections
                .Where(s => s.IsSelected)
                .Select(s => s.Option.Kind)
                .ToList()
        };
}
