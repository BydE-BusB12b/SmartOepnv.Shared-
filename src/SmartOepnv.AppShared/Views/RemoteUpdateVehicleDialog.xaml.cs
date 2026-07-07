using System.Windows;
using System.Windows.Controls;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public class RemoteUpdateVehicleDialog : Window
{
    public string? SelectedPhoneNumber { get; private set; }
    public string? SelectedVehicleName { get; private set; }

    public RemoteUpdateVehicleDialog(IReadOnlyList<RegisteredVehicleInfo> vehicles)
    {
        Title = "Fernupdate auslösen";
        Width = 500;
        Height = vehicles.Count > 0 ? 280 : 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(24) };
        for (var i = 0; i < 5; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var row = 0;
        root.Children.Add(MakeTextBlock("Fahrzeug für Fernupdate auswählen", 18, FontWeights.SemiBold, ref row));
        root.Children.Add(MakeTextBlock(
            "Route wird nach Dropbox gesendet, danach lädt das Fahrzeug automatisch. Bei aktivem Pas.Info bleibt die Position in der Route erhalten.",
            14,
            FontWeights.Normal,
            ref row));

        ComboBox? combo = null;
        TextBox? phoneBox = null;

        if (vehicles.Count > 0)
        {
            combo = new ComboBox
            {
                MinHeight = 36,
                Margin = new Thickness(0, 12, 0, 0),
                ItemsSource = vehicles.Select(v => $"{v.Name} ({v.PhoneNumber})").ToList()
            };
            combo.SelectedIndex = 0;
            Grid.SetRow(combo, row++);
            root.Children.Add(combo);
        }
        else
        {
            root.Children.Add(MakeTextBlock(
                "Keine Fahrzeuge in der JSON-Datei (registeredVehicles). Telefonnummer manuell eingeben:",
                14,
                FontWeights.Normal,
                ref row));
            phoneBox = new TextBox
            {
                MinHeight = 36,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Telefonnummer des Mitarbeitergeräts, z. B. 491701234567"
            };
            Grid.SetRow(phoneBox, row++);
            root.Children.Add(phoneBox);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var ok = new Button { Content = "Senden + Fernupdate", MinWidth = 170, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (combo is not null && combo.SelectedIndex >= 0)
            {
                var vehicle = vehicles[combo.SelectedIndex];
                SelectedPhoneNumber = vehicle.PhoneNumber;
                SelectedVehicleName = vehicle.Name;
            }
            else if (phoneBox is not null)
            {
                SelectedPhoneNumber = phoneBox.Text.Trim();
                SelectedVehicleName = SelectedPhoneNumber;
            }

            if (string.IsNullOrWhiteSpace(SelectedPhoneNumber))
            {
                MessageBox.Show(this, "Bitte Telefonnummer eingeben oder Fahrzeug wählen.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, row);
        root.Children.Add(buttons);

        Content = root;
    }

    private static TextBlock MakeTextBlock(string text, double fontSize, FontWeight weight, ref int row)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = row == 0 ? new Thickness(0) : new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(block, row++);
        return block;
    }
}
