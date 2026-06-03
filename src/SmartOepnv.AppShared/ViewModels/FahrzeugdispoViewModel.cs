using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.ViewModels;

public partial class FahrzeugdispoViewModel : EditorStatusViewModelBase
{
    public FahrzeugdispoViewModel() : base("Fahrzeugdispo – Datum wählen und Fahrzeuge zuweisen.")
    {
    }

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;

    [ObservableProperty] private int vehicleCount;

    [ObservableProperty] private int routeCount;

    partial void OnSelectedDateChanged(DateTime value) => RefreshSummary();

    public void RefreshFromEditor()
    {
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            VehicleCount = 0;
            RouteCount = 0;
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        VehicleCount = editor.RegisteredVehicles.Count;
        RouteCount = editor.RouteNames.Count;
        StatusMessage =
            $"Fahrzeugdispo für {SelectedDate:dd.MM.yyyy} – {VehicleCount} Fahrzeuge, {RouteCount} Routen im Paket. " +
            "Fahrzeug-Einsatzplanung wird hier als Nächstes ergänzt.";
    }
}
