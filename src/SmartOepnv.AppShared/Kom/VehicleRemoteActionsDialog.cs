using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Kom;

public sealed class VehicleRemoteActionsDialog : Window
{
    public VehicleRemoteActionsDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        Owner = owner;
        Title = $"Fernsteuerung – {vehicle.DisplayName}";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel();
        root.Children.Add(VehicleKomUi.MakeText(
            $"Aktionen für {vehicle.DisplayName}",
            18,
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 12)));
        root.Children.Add(VehicleKomUi.MakeText(
            "Wählen Sie eine Fernsteuerung – wie in der Android-Tracking-Karte auf dem Haupthandy.",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 16)));

        root.Children.Add(VehicleKomUi.MakeActionButton("Fernsteuerung Ziel", () =>
        {
            new KomRemoteDestinationDialog(vehicle, this).ShowDialog();
        }));
        root.Children.Add(VehicleKomUi.MakeActionButton("Fernroute auslösen", () =>
        {
            new KomRemoteRouteDialog(vehicle, this).ShowDialog();
        }));
        root.Children.Add(VehicleKomUi.MakeActionButton("Fahrgastraum-Durchsage", () =>
        {
            new KomLeitstelleDurchsageDialog(vehicle, this).ShowDialog();
        }));
        root.Children.Add(VehicleKomUi.MakeActionButton("Meldungen", () =>
        {
            new VehicleKomMessageDialog(vehicle, this).ShowDialog();
        }));

        var close = VehicleKomUi.MakeButton(
            "Schließen",
            isCancel: true,
            margin: new Thickness(0, 20, 0, 0),
            horizontalAlignment: HorizontalAlignment.Right);
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        VehicleKomUi.PrepareWindow(this, root);
    }
}
