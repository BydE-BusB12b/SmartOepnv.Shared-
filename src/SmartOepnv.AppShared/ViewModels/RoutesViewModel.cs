using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel : ObservableObject
{
    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";

    public ObservableCollection<string> Routes { get; } = new();
    public ObservableCollection<RouteStopItem> Stops { get; } = new();

    public void RefreshFromEditor()
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
        StatusMessage = $"{Routes.Count} Route(n) geladen.";
    }

    partial void OnSelectedRouteChanged(string? value)
    {
        Stops.Clear();
        if (string.IsNullOrWhiteSpace(value))
        {
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
    }

    public void CommitChanges()
    {
        if (AppServices.Routes.Editor is null)
        {
            return;
        }

        AppServices.Routes.ApplyEditorChanges("routes");
        StatusMessage = $"{Routes.Count} Route(n) – lokal gespeichert.";
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
        CommitChanges();
    }

    [RelayCommand]
    private void RemoveStop(RouteStopItem? stop)
    {
        if (stop is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        AppServices.Routes.Editor?.RemoveStop(SelectedRoute, stop);
        Stops.Remove(stop);
        CommitChanges();
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
}
