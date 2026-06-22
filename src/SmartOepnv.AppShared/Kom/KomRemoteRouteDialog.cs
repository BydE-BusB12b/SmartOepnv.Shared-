using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

public sealed class KomRemoteRouteDialog : Window
{
    public KomRemoteRouteDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        Owner = owner;
        Title = "Fernroute auslösen";
        Width = 520;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var routes = RouteDisplayHelper.SortRoutesByLineCourseAndTrip(
            AppServices.Routes.Editor?.RouteNames ?? []);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = 0;
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            $"Fernroute an {vehicle.DisplayName} senden",
            17,
            FontWeights.SemiBold), row++));
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            routes.Count == 0
                ? "Keine Routen im Paket geladen."
                : "Route wählen – das Fahrzeug öffnet sie und aktiviert Pas.Info (wie in der App).",
            13), row++));

        var list = new ListBox
        {
            ItemsSource = routes,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = routes.Count > 0
        };
        VehicleKomUi.StyleListBox(list);
        if (routes.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        Grid.SetRow(list, row++);
        root.Children.Add(list);

        var pasInfo = new CheckBox
        {
            Content = "Pas.Info aktivieren",
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0)
        };
        VehicleKomUi.StyleCheckBox(pasInfo);
        Grid.SetRow(pasInfo, row++);
        root.Children.Add(pasInfo);

        var status = VehicleKomUi.MakeText(string.Empty, 12, margin: new Thickness(0, 8, 0, 0), muted: true);
        Grid.SetRow(status, row++);
        root.Children.Add(status);

        var cancel = VehicleKomUi.MakeButton("Abbrechen", margin: new Thickness(0, 0, 8, 0), isCancel: true);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var send = VehicleKomUi.MakeButton(
            "Fernroute senden",
            primary: true,
            isDefault: true,
            minWidth: 140);
        send.IsEnabled = routes.Count > 0 && phone is not null;
        send.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (list.SelectedItem is not string route)
            {
                MessageBox.Show(this, "Bitte eine Route wählen.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            send.IsEnabled = false;
            cancel.IsEnabled = false;
            status.Text = "Sende Fernroute …";
            try
            {
                var commandId = await KomRemoteRouteService.UploadAsync(
                    AppServices.Dropbox,
                    phone,
                    route,
                    pasInfo.IsChecked == true);
                if (commandId > 0)
                {
                    MessageBox.Show(this,
                        $"Fernroute „{route}“ an {vehicle.DisplayName} gesendet.",
                        Title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    status.Text = "Senden fehlgeschlagen.";
                }
            }
            catch (Exception ex)
            {
                status.Text = $"Fehler: {ex.Message}";
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
