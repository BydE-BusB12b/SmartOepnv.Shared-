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
    public SevSignEditorViewModel() : base("SEV-Schilder als PDF im NRW-Standardlayout erstellen.")
    {
        foreach (var option in SevOperatorCatalog.All)
        {
            OperatorSelections.Add(new SevOperatorSelectionItem(option));
        }
    }

    [ObservableProperty] private string line = "S 28";

    [ObservableProperty] private string destination = "Düsseldorf, Hauptbahnhof";

    [ObservableProperty] private string newStopName = string.Empty;

    [ObservableProperty] private SevStopItem? selectedStop;

    [ObservableProperty] private string? selectedRoute;

    [ObservableProperty] private bool importRouteReverse;

    public ObservableCollection<SevOperatorSelectionItem> OperatorSelections { get; } = [];

    public ObservableCollection<SevStopItem> Stops { get; } = [];

    public ObservableCollection<string> Routes { get; } = [];

    public bool HasRoutes => Routes.Count > 0;

    public void RefreshFromEditor()
    {
        Routes.Clear();
        OnPropertyChanged(nameof(HasRoutes));

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
