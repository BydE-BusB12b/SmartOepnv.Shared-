using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel : ObservableObject, IEditorAreaViewModel
{
    private readonly EditorAreaSyncState _sync = new();

    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private RouteStopItem? selectedStop;
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";

    public ObservableCollection<string> Routes { get; } = new();
    public ObservableCollection<RouteStopItem> Stops { get; } = new();

    public RoutesViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        }
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    public void RefreshFromEditorIfNeeded()
    {
        if (!_sync.ShouldRefresh(Routes.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
    {
        Routes.Clear();
        Stops.Clear();
        SelectedRoute = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var route in editor.RouteNames)
        {
            Routes.Add(route);
        }

        SelectedRoute = Routes.FirstOrDefault();
        RefreshStopEditorCatalogs();
        StatusMessage = $"{Routes.Count} Route(n) geladen.";
        _sync.AfterRefresh();
    }

    private void ReloadRoutesList(string? selectRouteKey)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        Routes.Clear();
        foreach (var route in editor.RouteNames)
        {
            Routes.Add(route);
        }

        RefreshStopEditorCatalogs();

        if (!string.IsNullOrWhiteSpace(selectRouteKey) && Routes.Contains(selectRouteKey))
        {
            SelectedRoute = selectRouteKey;
        }
        else if (SelectedRoute is null || !Routes.Contains(SelectedRoute))
        {
            SelectedRoute = Routes.FirstOrDefault();
        }
        else
        {
            ReloadStopsForSelectedRoute();
        }

        StatusMessage = $"{Routes.Count} Route(n) geladen.";
    }

    private void ReloadStopsForSelectedRoute()
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

        RemoveSelectedStopCommand.NotifyCanExecuteChanged();
        NotifyMoveStopCommandsCanExecute();
    }

    partial void OnSelectedRouteChanged(string? value)
    {
        Stops.Clear();
        SelectedStop = null;
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

        EnrichStopTemplatesFromRoutes(AppServices.Routes.Editor);
        AppServices.Routes.ApplyEditorChanges("routes");
        StatusMessage = $"{Routes.Count} Route(n) – lokal gespeichert.";
        _sync.AfterCommit();
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
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dialog = new AddRouteDialog(editor.RouteNames.ToList()) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.ResultDefinition is null)
        {
            return;
        }

        if (!editor.TryAddRoute(dialog.ResultDefinition, dialog.CopyStopsFromRouteKey, out var displayKey, out var error))
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

    [RelayCommand]
    private void RemoveRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        AppServices.Routes.Editor?.RemoveRoute(SelectedRoute);
        _sync.MarkDirty();
        RefreshFromEditor();
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
        StatusMessage = direction < 0
            ? $"„{SelectedStop.Name}“ nach oben verschoben."
            : $"„{SelectedStop.Name}“ nach unten verschoben.";
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
        StatusMessage = $"„{name}“ aus Route entfernt.";
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

            StatusMessage = $"Fahrplan erstellt: {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Erstellen des Fahrplans: {ex.Message}";
        }
    }
}
