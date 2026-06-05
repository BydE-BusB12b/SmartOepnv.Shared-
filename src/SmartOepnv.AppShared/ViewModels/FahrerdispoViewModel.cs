using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.ViewModels;

public partial class FahrerdispoViewModel : EditorStatusViewModelBase
{
    public FahrerdispoViewModel() : base("Fahrerdisposition – Datum wählen und Einsätze planen.")
    {
    }

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;

    [ObservableProperty] private int driverCount;

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
            DriverCount = 0;
            RouteCount = 0;
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        DriverCount = editor.Employees.Count;
        RouteCount = editor.RouteNames.Count;
        StatusMessage =
            $"Fahrerdisposition für {SelectedDate:dd.MM.yyyy} – {DriverCount} Mitarbeiter, {RouteCount} Routen im Paket. " +
            "Einsatzplanung wird hier als Nächstes ergänzt.";
    }
}
