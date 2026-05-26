using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel : ObservableObject
{
    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string newRouteName = string.Empty;

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
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewRouteName) ? $"Route {Routes.Count + 1}" : NewRouteName.Trim();
        editor.AddRoute(name);
        NewRouteName = string.Empty;
        RefreshFromEditor();
        SelectedRoute = name;
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
