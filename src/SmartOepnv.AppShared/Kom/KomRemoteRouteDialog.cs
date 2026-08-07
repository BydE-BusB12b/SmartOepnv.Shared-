using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Kom;

public sealed class KomRemoteRouteDialog : Window
{
    private readonly KomSendDialogGuard _sendGuard;
    private readonly ObservableCollection<RoutePickItem> _allRoutes = [];
    private readonly ICollectionView _view;
    private string _filter = string.Empty;

    public KomRemoteRouteDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        _sendGuard = new KomSendDialogGuard(this);
        Owner = owner;
        Title = "Fernroute auslösen";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        foreach (var key in RouteDisplayHelper.SortRoutesByLineCourseAndTrip(
                     AppServices.Routes.Editor?.RouteNames ?? []))
        {
            _allRoutes.Add(new RoutePickItem(key));
        }

        _view = CollectionViewSource.GetDefaultView(_allRoutes);
        _view.Filter = FilterRow;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            _allRoutes.Count == 0
                ? "Keine Routen im Paket geladen."
                : "Route wählen – das Fahrzeug öffnet sie und aktiviert Pas.Info (wie in der App). " +
                  "Sortiert nach Linie/Kurs und Fahrt.",
            13), row++));

        var filterBox = new TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        VehicleKomUi.StyleTextBox(filterBox);
        Grid.SetRow(filterBox, row++);
        root.Children.Add(filterBox);

        var list = new ListBox
        {
            ItemsSource = _view,
            DisplayMemberPath = nameof(RoutePickItem.Display),
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = _allRoutes.Count > 0
        };
        VehicleKomUi.StyleListBox(list);
        if (_allRoutes.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        filterBox.TextChanged += (_, _) =>
        {
            _filter = (filterBox.Text ?? string.Empty).Trim();
            _view.Refresh();
            if (list.SelectedItem is null && _view.Cast<object>().Any())
            {
                list.SelectedIndex = 0;
            }
        };

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
        send.IsEnabled = _allRoutes.Count > 0 && phone is not null;
        send.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (list.SelectedItem is not RoutePickItem pick)
            {
                SmartConfirmDialog.ShowInfo(this, Title, "Bitte eine Route wählen.");
                return;
            }

            send.IsEnabled = false;
            _sendGuard.BeginSend();
            try
            {
                if (await KomCommandSendFlow.SendAndReleaseDialogAsync(
                    this,
                    status,
                    vehicle.DisplayName,
                    phone,
                    KomRemoteRouteService.CommandType,
                    ct => KomRemoteRouteService.UploadAsync(
                        AppServices.Dropbox,
                        phone,
                        pick.Key,
                        pasInfo.IsChecked == true,
                        ct)))
                {
                    _sendGuard.EndSend();
                    return;
                }
            }
            catch (Exception ex)
            {
                SmartConfirmDialog.ShowInfo(this, Title, $"Senden fehlgeschlagen: {ex.Message}");
            }
            finally
            {
                if (IsLoaded)
                {
                    _sendGuard.EndSend();
                    send.IsEnabled = true;
                    cancel.IsEnabled = true;
                }
            }
        };

        var buttons = VehicleKomUi.MakeButtonRow(cancel, send);
        Grid.SetRow(buttons, row);
        root.Children.Add(buttons);

        VehicleKomUi.PrepareWindow(this, root);

        Loaded += (_, _) => filterBox.Focus();
    }

    private bool FilterRow(object obj)
    {
        if (obj is not RoutePickItem item)
        {
            return false;
        }

        if (string.IsNullOrEmpty(_filter))
        {
            return true;
        }

        return item.Key.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
               item.Display.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private static UIElement MakeAtRow(UIElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }

    private sealed class RoutePickItem(string key)
    {
        public string Key { get; } = key;
        public string Display { get; } = RouteDisplayHelper.ToLineCourseTripFirstDisplayString(key);
    }
}
