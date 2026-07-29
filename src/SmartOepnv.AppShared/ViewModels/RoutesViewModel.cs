using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.RoutePath;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel : ObservableObject, IEditorAreaViewModel
{
    private const int SaveSuccessFeedbackMs = 5000;

    private readonly EditorAreaSyncState _sync = new();
    private CancellationTokenSource? _saveButtonFeedbackCts;
    private readonly SearchQueryDebouncer _searchDebouncer;

    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private RouteStopItem? selectedStop;
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private string routeOperatingDaysDisplay = string.Empty;
    [ObservableProperty] private string routeDateFrom = string.Empty;
    [ObservableProperty] private string routeDateTo = string.Empty;
    [ObservableProperty] private string routeDateRangeDisplay = string.Empty;
    [ObservableProperty] private string routeOperatingDatesText = string.Empty;
    [ObservableProperty] private string routeOperatingDatesDisplay = string.Empty;
    [ObservableProperty] private string routeInteriorDisplayDestination = string.Empty;
    [ObservableProperty] private bool routeItcsRouteListEnabled = true;
    [ObservableProperty] private bool routeMainDeviceOnly;
    [ObservableProperty] private bool saveButtonIsSuccess;
    [ObservableProperty] private bool isRouteSettingsExpanded;
    /// <summary>Erhöhen, damit die Zeit-Warnungsfarbe in der Haltestellenliste neu gebunden wird.</summary>
    [ObservableProperty] private int stopTimeOrderWarningTick;
    /// <summary>Erhöhen, damit Routenwechsel-Zeile unter Endhaltestelle live aktualisiert wird.</summary>
    [ObservableProperty] private int routeChangeDisplayTick;
    [ObservableProperty] private bool hasAnyStopTimeOrderWarning;
    [ObservableProperty] private string stopTimeOrderWarningText = string.Empty;

    public string RouteSettingsButtonLabel =>
        IsRouteSettingsExpanded ? "Verkehrstage & Gültigkeit ausblenden" : "Verkehrstage & Gültigkeit";

    private readonly HashSet<int> _stopTimeOrderWarningIndices = [];
    private bool _suppressOperatingDaySync;
    private bool _suppressDateRangeSync;
    private bool _suppressOperatingDatesSync;
    private bool _suppressInteriorDestinationSync;
    private bool _suppressItcsRouteListSync;
    private bool _suppressMainDeviceOnlySync;

    private readonly List<string> _allRoutes = [];
    public ObservableCollection<string> FilteredRoutes { get; } = [];
    public ObservableCollection<RouteStopItem> Stops { get; } = new();
    public ObservableCollection<OperatingDayOptionItem> RouteOperatingDaySelections { get; } = [];

    public bool HasRouteOperatingDaysDisplay => !string.IsNullOrWhiteSpace(RouteOperatingDaysDisplay);
    public bool HasRouteDateRangeDisplay => !string.IsNullOrWhiteSpace(RouteDateRangeDisplay);
    public bool HasRouteOperatingDatesDisplay => !string.IsNullOrWhiteSpace(RouteOperatingDatesDisplay);

    public RoutesViewModel()
    {
        _searchDebouncer = new SearchQueryDebouncer(ApplyRouteFilter);

        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var item = new OperatingDayOptionItem(day, name);
            AttachRouteOperatingDayHandler(item);
            RouteOperatingDaySelections.Add(item);
        }

        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        }
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    public void RefreshFromEditorIfNeeded()
    {
        if (!_sync.ShouldRefresh(_allRoutes.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
    {
        _allRoutes.Clear();
        FilteredRoutes.Clear();
        Stops.Clear();
        SelectedRoute = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket – „Route hinzufügen“ legt ein leeres Paket für den neuen Betrieb an.";
            return;
        }

        foreach (var route in editor.RouteNames)
        {
            _allRoutes.Add(route);
        }

        ApplyRouteFilter();
        SelectedRoute = FilteredRoutes.FirstOrDefault();
        RefreshStopEditorCatalogs();
        UpdateRouteStatusMessage();
        _sync.AfterRefresh();
    }

    partial void OnSearchQueryChanged(string value) => _searchDebouncer.Schedule();

    private void ApplyRouteFilter()
    {
        var query = SearchQuery.Trim();
        FilteredRoutes.Clear();

        IEnumerable<string> source = _allRoutes;
        if (!string.IsNullOrEmpty(query))
        {
            source = _allRoutes.Where(route => RouteMatchesSearch(route, query));
        }

        foreach (var route in RouteDisplayHelper.SortRoutesByLineCourseAndTrip(source))
        {
            FilteredRoutes.Add(route);
        }

        if (SelectedRoute is not null && !FilteredRoutes.Contains(SelectedRoute))
        {
            SelectedRoute = FilteredRoutes.FirstOrDefault();
        }

        UpdateRouteStatusMessage();
    }

    private static bool RouteMatchesSearch(string routeKey, string query)
    {
        var definition = RouteDisplayHelper.Parse(routeKey);
        var haystack = string.Join(' ',
            routeKey,
            definition.Name,
            definition.LineCourse,
            definition.TripNumber,
            definition.PassengerDisplayLine);

        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        return tokens.All(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateRouteStatusMessage()
    {
        if (AppServices.Routes.Editor is null)
        {
            return;
        }

        var total = _allRoutes.Count;
        var filtered = FilteredRoutes.Count;
        var query = SearchQuery.Trim();
        StatusMessage = string.IsNullOrEmpty(query)
            ? $"{total} Route(n) geladen."
            : $"{filtered} von {total} Route(n) – Suche: „{query}“";
    }

    private void ReloadRoutesList(string? selectRouteKey)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        _allRoutes.Clear();
        foreach (var route in editor.RouteNames)
        {
            _allRoutes.Add(route);
        }

        ApplyRouteFilter();
        RefreshStopEditorCatalogs();

        if (!string.IsNullOrWhiteSpace(selectRouteKey) && FilteredRoutes.Contains(selectRouteKey))
        {
            SelectedRoute = selectRouteKey;
        }
        else if (SelectedRoute is null || !FilteredRoutes.Contains(SelectedRoute))
        {
            SelectedRoute = FilteredRoutes.FirstOrDefault();
        }
        else
        {
            ReloadStopsForSelectedRoute();
        }

        UpdateRouteStatusMessage();
    }

    /// <summary>
    /// Übernimmt die sichtbare Haltestellenliste in den Editor, damit die Fahrplanerstellung aktuelle Zeiten nutzt.
    /// </summary>
    private void PrepareEditorForAutoSchedule(EditableRoutePackage editor)
    {
        if (!string.IsNullOrWhiteSpace(SelectedRoute) && Stops.Count > 0)
        {
            editor.ReplaceStopsForRoute(SelectedRoute, Stops);
        }

        editor.ConsolidateRouteKeys();
    }

    public void ReloadStopsForSelectedRoute()
    {
        Stops.Clear();
        SelectedStop = null;
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            RemoveSelectedStopCommand.NotifyCanExecuteChanged();
            NotifyMoveStopCommandsCanExecute();
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        foreach (var stop in editor.GetStops(SelectedRoute))
        {
            Stops.Add(stop);
        }

        RefreshStopTimeOrderWarnings(showDialog: false);
        RemoveSelectedStopCommand.NotifyCanExecuteChanged();
        NotifyMoveStopCommandsCanExecute();
    }

    /// <summary>Nach Haltestellen-Dialog: Liste und Zielauswahl aus dem Editor neu laden.</summary>
    public void RefreshStopAfterEdit(RouteStopItem stop)
    {
        var plannerCode = stop.PlannerStopCode;
        var route = SelectedRoute;
        ReloadStopsForSelectedRoute();
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        SelectedStop = Stops.FirstOrDefault(item =>
            string.Equals(item.PlannerStopCode, plannerCode, StringComparison.Ordinal))
            ?? Stops.FirstOrDefault(item => ReferenceEquals(item, stop));
        NotifyStopEditorStateChanged();
    }

    partial void OnRouteOperatingDaysDisplayChanged(string value)
    {
        OnPropertyChanged(nameof(HasRouteOperatingDaysDisplay));
    }

    partial void OnRouteDateRangeDisplayChanged(string value)
    {
        OnPropertyChanged(nameof(HasRouteDateRangeDisplay));
    }

    partial void OnRouteOperatingDatesDisplayChanged(string value)
    {
        OnPropertyChanged(nameof(HasRouteOperatingDatesDisplay));
    }

    partial void OnRouteDateFromChanged(string value)
    {
        if (_suppressDateRangeSync)
        {
            return;
        }

        PersistRouteDateRangeFromSelection();
    }

    partial void OnRouteDateToChanged(string value)
    {
        if (_suppressDateRangeSync)
        {
            return;
        }

        PersistRouteDateRangeFromSelection();
    }

    partial void OnRouteOperatingDatesTextChanged(string value)
    {
        if (_suppressOperatingDatesSync)
        {
            return;
        }

        PersistRouteOperatingDatesFromSelection();
    }

    partial void OnIsRouteSettingsExpandedChanged(bool value) =>
        OnPropertyChanged(nameof(RouteSettingsButtonLabel));

    [RelayCommand]
    private void ToggleRouteSettings() => IsRouteSettingsExpanded = !IsRouteSettingsExpanded;

    partial void OnRouteInteriorDisplayDestinationChanged(string value)
    {
        if (_suppressInteriorDestinationSync)
        {
            return;
        }

        PersistRouteInteriorDisplayDestinationFromSelection();
    }

    partial void OnRouteItcsRouteListEnabledChanged(bool value)
    {
        if (_suppressItcsRouteListSync)
        {
            return;
        }

        PersistRouteItcsRouteListFromSelection();
    }

    partial void OnRouteMainDeviceOnlyChanged(bool value)
    {
        if (_suppressMainDeviceOnlySync)
        {
            return;
        }

        PersistRouteMainDeviceOnlyFromSelection();
    }

    partial void OnSelectedRouteChanged(string? value)
    {
        IsRouteSettingsExpanded = false;
        EditRouteCommand.NotifyCanExecuteChanged();
        CopyNavigationDataCommand.NotifyCanExecuteChanged();
        Stops.Clear();
        SelectedStop = null;
        LoadRouteOperatingDaysForSelection(value);
        LoadRouteDateRangeForSelection(value);
        LoadRouteOperatingDatesForSelection(value);
        LoadRouteInteriorDisplayDestinationForSelection(value);
        LoadRouteItcsRouteListForSelection(value);
        LoadRouteMainDeviceOnlyForSelection(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveSelectedStopCommand.NotifyCanExecuteChanged();
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        foreach (var stop in editor.GetStops(value))
        {
            Stops.Add(stop);
        }

        RemoveSelectedStopCommand.NotifyCanExecuteChanged();
        NotifyMoveStopCommandsCanExecute();
    }

    partial void OnSelectedStopChanged(RouteStopItem? value)
    {
        RemoveSelectedStopCommand.NotifyCanExecuteChanged();
        NotifyMoveStopCommandsCanExecute();
        SyncSelectedStopVrrStopIdFromStop();
        NotifyStopEditorStateChanged();
    }

    private void NotifyMoveStopCommandsCanExecute()
    {
        MoveSelectedStopUpCommand.NotifyCanExecuteChanged();
        MoveSelectedStopDownCommand.NotifyCanExecuteChanged();
    }

    public void CommitChangesIfDirty()
    {
        if (!_sync.HasPendingChanges)
        {
            return;
        }

        CommitChanges();
    }

    public void CommitChanges()
    {
        if (AppServices.Routes.Editor is null)
        {
            return;
        }

        AppServices.Routes.Editor.ConsolidateRouteKeys();

        PersistRouteDateRangeFromSelection();
        var previousRouteKey = SelectedRoute;
        var routeKey = SyncSelectedRouteOperatingDaysToEditor();
        if (!string.IsNullOrWhiteSpace(routeKey))
        {
            SelectedRoute = routeKey;
            // Volle Listen-Neuberechnung nur wenn sich der Routenschlüssel wirklich ändert
            // (Verkehrstage) – sonst kostet das bei jedem Haltestellen-Speichern unnötig Zeit.
            if (!RouteDisplayHelper.RouteKeysMatch(previousRouteKey, routeKey))
            {
                ReloadRoutesList(routeKey);
            }
        }

        EnrichStopTemplatesFromRoutes(AppServices.Routes.Editor);
        // Haltestellen/Routen-Felder: Audio in JSON belassen (Rebuild nur bei Ansagen-/Kartei-Saves).
        AppServices.Routes.ApplyEditorChanges("routes", rebuildEmbeddedMedia: false);
        StatusMessage = $"{_allRoutes.Count} Route(n) – lokal gespeichert.";
        SaveButtonIsSuccess = true;
        _ = ShowSaveButtonSuccessFeedbackAsync();
        _sync.AfterCommit();
    }

    private async Task ShowSaveButtonSuccessFeedbackAsync()
    {
        _saveButtonFeedbackCts?.Cancel();
        _saveButtonFeedbackCts?.Dispose();
        _saveButtonFeedbackCts = new CancellationTokenSource();
        var token = _saveButtonFeedbackCts.Token;

        try
        {
            await Task.Delay(SaveSuccessFeedbackMs, token).ConfigureAwait(true);
            SaveButtonIsSuccess = false;
        }
        catch (TaskCanceledException)
        {
            // neuer Speichervorgang oder Bearbeitung hat Feedback zurückgesetzt
        }
    }

    private void CancelSaveButtonSuccessFeedback()
    {
        _saveButtonFeedbackCts?.Cancel();
        _saveButtonFeedbackCts?.Dispose();
        _saveButtonFeedbackCts = null;
        SaveButtonIsSuccess = false;
    }

    private static void EnrichStopTemplatesFromRoutes(EditableRoutePackage? editor)
    {
        if (editor is null || editor.StopTemplates.Count == 0)
        {
            return;
        }

        var templates = editor.StopTemplates.ToList();
        var merge = StopTemplateRouteMerger.MergeAllRouteStops(templates, editor);
        if (merge.Enriched > 0)
        {
            editor.ReplaceStopTemplates(templates);
        }
    }

    [RelayCommand]
    private void AddRoute()
    {
        if (!AppServices.Routes.EnsureEmptyPackageIfNeeded())
        {
            StatusMessage = "Leeres Route-Paket konnte nicht angelegt werden.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dialog = new AddRouteDialog(editor) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.ResultDefinition is null)
        {
            return;
        }

        if (!editor.TryAddRoute(
                dialog.ResultDefinition,
                dialog.ResultOperatingDays,
                dialog.CopyStopsFromRouteKey,
                out var displayKey,
                out var error,
                dialog.ResultItcsRouteListEnabled,
                dialog.ResultMainDeviceOnly,
                dialog.ResultDateRange,
                dialog.ResultOperatingDates))
        {
            StatusMessage = error ?? "Route konnte nicht angelegt werden.";
            return;
        }

        _sync.MarkDirty();
        RefreshFromEditor();
        SelectedRoute = displayKey;
        StatusMessage = dialog.CopyStopsFromRouteKey is null
            ? $"Route „{displayKey}“ hinzugefügt."
            : $"Route „{displayKey}“ angelegt (Haltestellen kopiert).";
        CommitChanges();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedRoute))]
    private void EditRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dialog = new AddRouteDialog(editor, editingRouteKey: SelectedRoute) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.ResultDefinition is null)
        {
            return;
        }

        if (!editor.TryUpdateRoute(
                SelectedRoute,
                dialog.ResultDefinition,
                dialog.ResultOperatingDays,
                dialog.ResultItcsRouteListEnabled,
                dialog.ResultMainDeviceOnly,
                out var displayKey,
                out var error,
                dialog.ResultDateRange,
                dialog.ResultOperatingDates))
        {
            StatusMessage = error ?? "Route konnte nicht gespeichert werden.";
            return;
        }

        _sync.MarkDirty();
        RefreshFromEditor();
        SelectedRoute = displayKey;
        StatusMessage = $"Route „{displayKey}“ gespeichert.";
        CommitChanges();
    }

    private bool CanEditSelectedRoute() => !string.IsNullOrWhiteSpace(SelectedRoute);

    [RelayCommand]
    private void RemoveRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        AppServices.Routes.Editor?.RemoveRoute(SelectedRoute);
        _sync.MarkDirty();
        ReloadRoutesList(null);
        CommitChanges();
    }

    [RelayCommand]
    private void AddStop()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        AppServices.Routes.Editor?.AddStop(SelectedRoute);
        OnSelectedRouteChanged(SelectedRoute);
        _sync.MarkDirty();
        CommitChanges();
    }

    [RelayCommand]
    private void AddStopFromLibrary()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Bitte zuerst eine Route auswählen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var templates = editor.StopTemplates
            .Where(t => t.HasPersistableContent())
            .ToList();
        if (templates.Count == 0)
        {
            StatusMessage = "Keine Haltestellen in der Kartei – bitte unter „Haltestellen“ anlegen.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dialog = new PickStopFromLibraryDialog(templates) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.SelectedTemplate is null)
        {
            return;
        }

        editor.AddStopFromTemplate(SelectedRoute, dialog.SelectedTemplate);
        OnSelectedRouteChanged(SelectedRoute);
        _sync.MarkDirty();
        CommitChanges();
        StatusMessage = $"„{dialog.SelectedTemplate.StopNameItcs}“ aus Kartei eingefügt.";
        TryPromptNavReuse();
    }

    [RelayCommand]
    private void OfferNavReuse()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Bitte zuerst eine Route auswählen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        if (RoutePathNavReusePrompt.TryOffer(owner, editor, SelectedRoute, out var edges))
        {
            _sync.MarkDirty();
            CommitChanges();
            StatusMessage = $"Navidaten übernommen ({edges} Verbindungen).";
            return;
        }

        StatusMessage = "Keine passenden Navidaten in anderen Routen gefunden.";
    }

    private void TryPromptNavReuse()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        var owner = Application.Current?.MainWindow;
        if (RoutePathNavReusePrompt.TryOffer(owner, editor, SelectedRoute, out var edges))
        {
            _sync.MarkDirty();
            CommitChanges();
            StatusMessage = $"Navidaten übernommen ({edges} Verbindungen) – kein erneutes Snappen nötig.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedStop))]
    private void RemoveSelectedStop()
    {
        if (SelectedStop is null)
        {
            return;
        }

        RemoveStop(SelectedStop);
        SelectedStop = null;
    }

    private bool CanRemoveSelectedStop() => SelectedStop is not null;

    [RelayCommand(CanExecute = nameof(CanMoveSelectedStopUp))]
    private void MoveSelectedStopUp() => MoveSelectedStop(-1);

    [RelayCommand(CanExecute = nameof(CanMoveSelectedStopDown))]
    private void MoveSelectedStopDown() => MoveSelectedStop(1);

    private bool CanMoveSelectedStopUp() =>
        SelectedStop is not null && Stops.IndexOf(SelectedStop) > 0;

    private bool CanMoveSelectedStopDown()
    {
        if (SelectedStop is null)
        {
            return false;
        }

        var index = Stops.IndexOf(SelectedStop);
        return index >= 0 && index < Stops.Count - 1;
    }

    private void MoveSelectedStop(int direction)
    {
        if (SelectedStop is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null || !editor.TryMoveStop(SelectedRoute, SelectedStop, direction))
        {
            return;
        }

        var index = Stops.IndexOf(SelectedStop);
        if (index < 0)
        {
            return;
        }

        Stops.Move(index, index + direction);
        _sync.MarkDirty();
        CommitChanges();
        var movedMessage = direction < 0
            ? $"„{SelectedStop.Name}“ nach oben verschoben."
            : $"„{SelectedStop.Name}“ nach unten verschoben.";
        if (!RefreshStopTimeOrderWarnings(showDialog: true))
        {
            StatusMessage = movedMessage;
        }

        NotifyMoveStopCommandsCanExecute();
    }

    [RelayCommand]
    private void RemoveStop(RouteStopItem? stop)
    {
        if (stop is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(stop.Name) ? "Haltestelle" : stop.Name.Trim();
        AppServices.Routes.Editor?.RemoveStop(SelectedRoute, stop);
        Stops.Remove(stop);
        if (ReferenceEquals(SelectedStop, stop))
        {
            SelectedStop = null;
        }

        _sync.MarkDirty();
        CommitChanges();
        if (!RefreshStopTimeOrderWarnings(showDialog: false))
        {
            StatusMessage = $"„{name}“ aus Route entfernt.";
        }

        RemoveSelectedStopCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SaveChanges()
    {
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Nichts zu speichern.";
            return;
        }

        CommitChanges();
        // CommitChanges setzt StatusMessage auf „gespeichert“ – Warnung danach erneut setzen.
        RefreshStopTimeOrderWarnings(showDialog: true);
    }

    /// <summary>true, wenn die Haltestelle in der Liste eine rückwärts springende Uhrzeit hat.</summary>
    public bool HasStopTimeOrderWarning(RouteStopItem? stop)
    {
        if (stop is null)
        {
            return false;
        }

        var index = Stops.IndexOf(stop);
        return index >= 0 && _stopTimeOrderWarningIndices.Contains(index);
    }

    /// <summary>
    /// Prüft rückwärts springende Haltestellenzeiten.
    /// </summary>
    /// <returns>true, wenn mindestens eine Warnung gesetzt wurde.</returns>
    public bool RefreshStopTimeOrderWarnings(bool showDialog)
    {
        var issues = RouteStopTimeOrder.FindIssues(Stops);
        _stopTimeOrderWarningIndices.Clear();
        foreach (var issue in issues)
        {
            _stopTimeOrderWarningIndices.Add(issue.Index);
        }

        HasAnyStopTimeOrderWarning = issues.Count > 0;
        StopTimeOrderWarningText = HasAnyStopTimeOrderWarning
            ? RouteStopTimeOrder.FormatWarningMessage(issues)
            : string.Empty;
        StopTimeOrderWarningTick++;

        if (!HasAnyStopTimeOrderWarning)
        {
            return false;
        }

        StatusMessage = StopTimeOrderWarningText;
        if (showDialog)
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null)
            {
                SmartConfirmDialog.ShowInfo(owner, "Uhrzeit-Reihenfolge", StopTimeOrderWarningText);
            }
        }

        return true;
    }

    /// <summary>Nach Zeit-Änderung einer Haltestelle: Warnung, falls sie vor der vorherigen liegt.</summary>
    public void NotifyStopTimeEdited(RouteStopItem? stop)
    {
        var showDialog = stop is not null && RouteStopTimeOrder.HasIssueForStop(Stops, stop);
        RefreshStopTimeOrderWarnings(showDialog);
    }

    [RelayCommand]
    private void ShowAutoSchedule()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        PrepareEditorForAutoSchedule(editor);

        if (AutoSchedulePlanner.GetSortedTemplateRoutes(editor).Count == 0)
        {
            StatusMessage = "Keine Routen als Vorlage verfügbar.";
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            var dialog = new AutoScheduleDialog(editor, SelectedRoute) { Owner = owner };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _sync.MarkDirty();
            ReloadRoutesList(dialog.CreatedFirstRouteKey);
            CommitChanges();
            StatusMessage = "Fahrplan erfolgreich erstellt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fahrplan fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyNavigationDataForSelectedRoute))]
    private void CopyNavigationData()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        if (!editor.TryCopyNavigationDataFromAutoScheduleSource(SelectedRoute, out var error))
        {
            StatusMessage = error ?? "Navidaten konnten nicht kopiert werden.";
            return;
        }

        var source = editor.GetAutoScheduleSourceRoute(SelectedRoute);
        _sync.MarkDirty();
        CommitChanges();
        StatusMessage = string.IsNullOrWhiteSpace(source)
            ? "Navidaten kopiert."
            : $"Navidaten von „{source}“ nach „{SelectedRoute}“ kopiert.";
    }

    private bool CanCopyNavigationDataForSelectedRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return false;
        }

        var editor = AppServices.Routes.Editor;
        return editor is not null &&
               !string.IsNullOrWhiteSpace(editor.GetAutoScheduleSourceRoute(SelectedRoute));
    }

    [RelayCommand]
    private void ShowRouteChain()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        try
        {
            PrepareEditorForAutoSchedule(editor);
            var owner = Application.Current?.MainWindow;
            string? initialLineCourse = null;
            if (!string.IsNullOrWhiteSpace(SelectedRoute))
            {
                var parsed = RouteDisplayHelper.Parse(SelectedRoute);
                if (!string.IsNullOrWhiteSpace(parsed.LineCourse))
                {
                    initialLineCourse = parsed.LineCourse;
                }
            }

            var dialog = new RouteChainDialog(editor, initialLineCourse) { Owner = owner };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Routenschnur konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportScheduleHtml()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Bitte zuerst eine Route auswählen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var routeStops = editor.GetStops(SelectedRoute).Where(s => !s.IsWaypoint).ToList();
        if (routeStops.Count == 0)
        {
            StatusMessage = "Keine Haltestellen für Fahrplan gefunden.";
            return;
        }

        try
        {
            var html = RouteScheduleHtmlExporter.BuildHtml(SelectedRoute, routeStops);
            var fileName = RouteScheduleHtmlExporter.BuildFileName(SelectedRoute);
            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartOepnv",
                "Fahrplaene");
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, fileName);
            File.WriteAllText(targetPath, html, System.Text.Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });

            // Ordner im Explorer zeigen (AppData\Local ist sonst leicht zu übersehen)
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{targetPath}\"",
                UseShellExecute = true
            });

            StatusMessage = $"Fahrplan gespeichert unter:{Environment.NewLine}{targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Erstellen des Fahrplans: {ex.Message}";
        }
    }

    private void LoadRouteOperatingDaysForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        if (string.IsNullOrWhiteSpace(routeKey) || editor is null)
        {
            ApplyRouteOperatingDayToSelection([]);
            RouteOperatingDaysDisplay = string.Empty;
            return;
        }

        var configured = editor.GetRouteOperatingDays(routeKey);
        var selected = RouteOperatingDaysEditor.IsConfiguredForAllDays(configured)
            ? RouteOperatingDaysEditor.AllDays.ToHashSet()
            : configured;
        ApplyRouteOperatingDayToSelection(selected);
        RouteOperatingDaysDisplay = DutyOperatingDayHelper.FormatDisplay(selected);
    }

    private void LoadRouteDateRangeForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        _suppressDateRangeSync = true;
        try
        {
            if (string.IsNullOrWhiteSpace(routeKey) || editor is null)
            {
                RouteDateFrom = string.Empty;
                RouteDateTo = string.Empty;
                RouteDateRangeDisplay = string.Empty;
                return;
            }

            var range = editor.GetRouteDateRange(routeKey);
            RouteDateFrom = range.From is { } from ? RouteDateRange.FormatDate(from) : string.Empty;
            RouteDateTo = range.To is { } to ? RouteDateRange.FormatDate(to) : string.Empty;
            RouteDateRangeDisplay = RouteDateRange.FormatDisplay(range);
        }
        finally
        {
            _suppressDateRangeSync = false;
        }
    }

    private void PersistRouteDateRangeFromSelection()
    {
        if (_suppressDateRangeSync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        if (!RouteDateRange.TryParse(RouteDateFrom, RouteDateTo, out var range))
        {
            StatusMessage = "Ungültiges Datum – bitte TT.MM.JJJJ verwenden (von ≤ bis).";
            return;
        }

        editor.SetRouteDateRange(SelectedRoute, range.IsRestricted ? range : null);
        RouteDateRangeDisplay = RouteDateRange.FormatDisplay(range);
        _sync.MarkDirty();
        StatusMessage = range.IsRestricted
            ? $"Gültigkeit für „{SelectedRoute}“ gesetzt – bitte speichern."
            : $"Gültigkeit für „{SelectedRoute}“ entfernt – bitte speichern.";
    }

    private void LoadRouteOperatingDatesForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        _suppressOperatingDatesSync = true;
        try
        {
            if (string.IsNullOrWhiteSpace(routeKey) || editor is null)
            {
                RouteOperatingDatesText = string.Empty;
                RouteOperatingDatesDisplay = string.Empty;
                return;
            }

            var dates = editor.GetRouteOperatingDates(routeKey);
            RouteOperatingDatesText = RouteOperatingDatesEditor.FormatDisplay(dates);
            RouteOperatingDatesDisplay = RouteOperatingDatesText;
        }
        finally
        {
            _suppressOperatingDatesSync = false;
        }
    }

    private void PersistRouteOperatingDatesFromSelection()
    {
        if (_suppressOperatingDatesSync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        if (!RouteOperatingDatesEditor.TryParseDateList(
                RouteOperatingDatesText,
                out var dates,
                out var error))
        {
            StatusMessage = error ?? "Ungültige Betriebstage.";
            return;
        }

        editor.SetRouteOperatingDates(SelectedRoute, dates);
        RouteOperatingDatesDisplay = RouteOperatingDatesEditor.FormatDisplay(dates);
        _sync.MarkDirty();
        StatusMessage = dates.Count > 0
            ? $"Betriebstage für „{SelectedRoute}“ gesetzt ({dates.Count}) – bitte speichern."
            : $"Betriebstage für „{SelectedRoute}“ entfernt – bitte speichern.";
    }

    private void LoadRouteInteriorDisplayDestinationForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        _suppressInteriorDestinationSync = true;
        try
        {
            RouteInteriorDisplayDestination = string.IsNullOrWhiteSpace(routeKey) || editor is null
                ? string.Empty
                : editor.GetRouteInteriorDisplayDestination(routeKey);
        }
        finally
        {
            _suppressInteriorDestinationSync = false;
        }
    }

    private void PersistRouteInteriorDisplayDestinationFromSelection()
    {
        if (_suppressInteriorDestinationSync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.SetRouteInteriorDisplayDestination(SelectedRoute, RouteInteriorDisplayDestination);
        _sync.MarkDirty();
        StatusMessage = $"Haltestellenanzeige-Zieltext für „{SelectedRoute}“ geändert – bitte speichern.";
    }

    private void LoadRouteItcsRouteListForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        _suppressItcsRouteListSync = true;
        try
        {
            RouteItcsRouteListEnabled = !string.IsNullOrWhiteSpace(routeKey) && editor is not null &&
                                        editor.IsRouteInItcsRouteList(routeKey);
        }
        finally
        {
            _suppressItcsRouteListSync = false;
        }
    }

    private void PersistRouteItcsRouteListFromSelection()
    {
        if (_suppressItcsRouteListSync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.SetRouteInItcsRouteList(SelectedRoute, RouteItcsRouteListEnabled);
        _sync.MarkDirty();
        StatusMessage = RouteItcsRouteListEnabled
            ? $"Route „{SelectedRoute}“ erscheint in der ITCS-Routenliste – bitte speichern."
            : $"Route „{SelectedRoute}“ nicht in der ITCS-Routenliste – bitte speichern.";
    }

    private void LoadRouteMainDeviceOnlyForSelection(string? routeKey)
    {
        var editor = AppServices.Routes.Editor;
        _suppressMainDeviceOnlySync = true;
        try
        {
            RouteMainDeviceOnly = !string.IsNullOrWhiteSpace(routeKey) && editor is not null &&
                                  editor.IsRouteMainDeviceOnly(routeKey);
        }
        finally
        {
            _suppressMainDeviceOnlySync = false;
        }
    }

    private void PersistRouteMainDeviceOnlyFromSelection()
    {
        if (_suppressMainDeviceOnlySync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.SetRouteMainDeviceOnly(SelectedRoute, RouteMainDeviceOnly);
        _sync.MarkDirty();
        StatusMessage = RouteMainDeviceOnly
            ? $"Route „{SelectedRoute}“ nur für Hauptnutzergeräte – bitte speichern."
            : $"Route „{SelectedRoute}“ für alle Geräte sichtbar – bitte speichern.";
    }

    private void AttachRouteOperatingDayHandler(OperatingDayOptionItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OperatingDayOptionItem.IsSelected) || _suppressOperatingDaySync)
            {
                return;
            }

            PersistRouteOperatingDaysFromSelection();
        };
    }

    private void PersistRouteOperatingDaysFromSelection()
    {
        if (_suppressOperatingDaySync || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        var newRouteKey = SyncSelectedRouteOperatingDaysToEditor();
        if (string.IsNullOrWhiteSpace(newRouteKey))
        {
            return;
        }

        MarkRoutesDirty();
        ReloadRoutesList(newRouteKey);
    }

    private string? SyncSelectedRouteOperatingDaysToEditor()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return SelectedRoute;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return SelectedRoute;
        }

        var selected = RouteOperatingDaySelections
            .Where(option => option.IsSelected)
            .Select(option => option.Day)
            .ToList();
        var newRouteKey = editor.ApplyOperatingDaysChange(SelectedRoute, selected);
        RouteOperatingDaysDisplay = DutyOperatingDayHelper.FormatDisplay(selected);
        return newRouteKey;
    }

    private void ApplyRouteOperatingDayToSelection(IEnumerable<DutyOperatingDay> days)
    {
        _suppressOperatingDaySync = true;
        try
        {
            var set = days.ToHashSet();
            foreach (var option in RouteOperatingDaySelections)
            {
                option.IsSelected = set.Count == 0 || set.Contains(option.Day);
            }
        }
        finally
        {
            _suppressOperatingDaySync = false;
        }

        OnPropertyChanged(nameof(HasRouteOperatingDaysDisplay));
    }

    private void MarkRoutesDirty()
    {
        CancelSaveButtonSuccessFeedback();
        _sync.MarkDirty();
        StatusMessage = string.IsNullOrWhiteSpace(SelectedRoute)
            ? StatusMessage
            : $"Verkehrstage für „{SelectedRoute}“ geändert – bitte speichern.";
    }
}
