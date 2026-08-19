using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Kom;

internal static class VehicleDetailContentBuilder
{
    public static void Populate(StackPanel root, VehicleListItemViewModel vehicle)
    {
        root.Children.Clear();
        AddRow(root, "Status", vehicle.StatusLabel);
        AddRow(root, "Letztes Update", vehicle.LastUpdateLabel);
        AddRow(root, "Telefon", vehicle.PhoneDisplay);
        AddRow(root, "Angemeldeter Fahrer", vehicle.DriverDisplay);
        AddRow(root, "Linie/Kurs, Fahrt", vehicle.LineCourse ?? "–");
        AddRow(root, "Route", vehicle.RouteDisplay);
        AddRow(root, "Haltestelle", vehicle.StopDisplay);
        AddRow(root, "Ziel", vehicle.DestinationDisplay);
        AddRow(root, "Geschwindigkeit", vehicle.SpeedDisplay);
        AddRow(root, "Verspätung", vehicle.DelayDisplay);
        AddRow(root, "Akku", vehicle.BatteryDisplay);
        AddRow(root, "Straße", vehicle.StreetDisplay);
        AddRow(root, "Position", vehicle.PositionDisplay);
        AddRow(root, "Genauigkeit", vehicle.AccuracyDisplay);
        AddRow(root, "Appversion", vehicle.AppVersionDisplay);
        AddRow(root, "routes_export Version", vehicle.RoutesExportVersionDisplay);
        AddRow(root, "routes_update Version", vehicle.RoutesUpdateVersionDisplay, isLast: true);
    }

    public static string BuildTitle(VehicleListItemViewModel vehicle) => $"🚌 {vehicle.DisplayName}";

    private static void AddRow(StackPanel parent, string label, string value, bool isLast = false)
    {
        parent.Children.Add(VehicleKomUi.MakeText(label, 12, FontWeights.SemiBold, new Thickness(0, 8, 0, 2), muted: true));
        parent.Children.Add(VehicleKomUi.MakeText(
            value,
            14,
            margin: new Thickness(0, 0, 0, isLast ? 0 : 4)));
    }
}
