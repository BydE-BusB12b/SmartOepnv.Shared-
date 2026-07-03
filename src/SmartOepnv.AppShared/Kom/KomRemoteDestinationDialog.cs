using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Kom;

public sealed class KomRemoteDestinationDialog : Window
{
    public KomRemoteDestinationDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        Owner = owner;
        Title = "Fernsteuerung Ziel";
        Width = 520;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var destinations = KomOutsideDestinationCatalog.LoadListEnabledNames(AppServices.Routes.Editor);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = 0;
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            $"Ziel an {vehicle.DisplayName} senden",
            17,
            FontWeights.SemiBold), row++));
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            destinations.Count == 0
                ? "Keine ITCS-Außenziele – im Planer unter „Außenanzeigen“ anlegen (mit „In ITCS-Liste“), Planer schließen oder „Für Leitstelle speichern“, dann Leitstelle neu laden."
                : "Ziel aus der Außenanzeigen-Liste wählen – das Fahrzeug setzt das Ziel per Dropbox.",
            13), row++));

        var list = new ListBox
        {
            ItemsSource = destinations,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = destinations.Count > 0
        };
        VehicleKomUi.StyleListBox(list);
        if (destinations.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        Grid.SetRow(list, row++);
        root.Children.Add(list);

        var status = VehicleKomUi.MakeText(string.Empty, 12, margin: new Thickness(0, 8, 0, 0), muted: true);
        Grid.SetRow(status, row++);
        root.Children.Add(status);

        var cancel = VehicleKomUi.MakeButton("Abbrechen", margin: new Thickness(0, 0, 8, 0), isCancel: true);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var send = VehicleKomUi.MakeButton(
            "Ziel senden",
            primary: true,
            isDefault: true,
            minWidth: 120);
        send.IsEnabled = destinations.Count > 0 && phone is not null;
        send.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (list.SelectedItem is not string destination)
            {
                SmartConfirmDialog.ShowInfo(this, Title, "Bitte ein Ziel wählen.");
                return;
            }

            send.IsEnabled = false;
            cancel.IsEnabled = false;
            try
            {
                var outcome = await KomCommandSendFlow.ExecuteAsync(
                    this,
                    status,
                    vehicle.DisplayName,
                    phone,
                    KomRemoteDestinationService.CommandType,
                    ct => KomRemoteDestinationService.UploadAsync(
                        AppServices.Dropbox,
                        phone,
                        destination,
                        ct));
                if (outcome is KomCommandSendOutcome.Success or KomCommandSendOutcome.Timeout)
                {
                    DialogResult = true;
                    Close();
                }
            }
            finally
            {
                send.IsEnabled = true;
                cancel.IsEnabled = true;
            }
        };

        var buttons = VehicleKomUi.MakeButtonRow(cancel, send);
        Grid.SetRow(buttons, row);
        root.Children.Add(buttons);

        VehicleKomUi.PrepareWindow(this, root);
    }

    private static UIElement MakeAtRow(UIElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }
}
