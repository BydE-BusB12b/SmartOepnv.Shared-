using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Fahrer aus dem Roster wählen und remote am Fahrzeug anmelden, oder abmelden.</summary>
public sealed class KomRemoteDriverLoginDialog : Window
{
    private readonly KomSendDialogGuard _sendGuard;

    public KomRemoteDriverLoginDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        _sendGuard = new KomSendDialogGuard(this);
        Owner = owner;
        Title = "Fern-Fahreranmeldung";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var employees = (AppServices.Routes.Editor?.Employees ?? Array.Empty<EmployeeRosterItem>())
            .Where(e => !string.IsNullOrWhiteSpace(KomRemoteDriverLoginService.EmployeeRosterItemPin(e.PersonnelNumber)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PersonnelNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var root = new StackPanel();
        root.Children.Add(VehicleKomUi.MakeText(
            $"Fahrer an {vehicle.DisplayName} anmelden",
            17,
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 8)));
        root.Children.Add(VehicleKomUi.MakeText(
            employees.Count == 0
                ? "Kein Mitarbeiter mit Personalnummer im Roster – unter Personalverwaltung anlegen und Routen syncen."
                : "Der gewählte Fahrer wird auf dem Fahrzeuggerät ohne PIN/Passwort-Eingabe angemeldet (Leitstellen-Freigabe).",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 12)));

        var list = new ListBox
        {
            ItemsSource = employees,
            DisplayMemberPath = nameof(EmployeeRosterItem.DisplayLabel),
            Height = 220,
            Margin = new Thickness(0, 0, 0, 8),
            IsEnabled = employees.Count > 0
        };
        VehicleKomUi.StyleListBox(list);
        if (employees.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        root.Children.Add(list);

        var status = VehicleKomUi.MakeText(string.Empty, 12, margin: new Thickness(0, 0, 0, 8), muted: true);
        root.Children.Add(status);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var cancel = VehicleKomUi.MakeButton("Abbrechen", margin: new Thickness(0, 0, 8, 0), isCancel: true);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var logout = VehicleKomUi.MakeButton("Abmelden senden", margin: new Thickness(0, 0, 8, 0), minWidth: 140);
        logout.IsEnabled = phone is not null;
        logout.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (!SmartConfirmDialog.ShowConfirm(
                    this,
                    Title,
                    $"Fahrer auf {vehicle.DisplayName} wirklich remote abmelden?"))
            {
                return;
            }

            await SendAsync(
                list,
                status,
                cancel,
                logout,
                loginButton: null,
                phone,
                vehicle.DisplayName,
                login: false,
                employee: null);
        };

        var login = VehicleKomUi.MakeButton(
            "Anmelden senden",
            primary: true,
            isDefault: true,
            minWidth: 150);
        login.IsEnabled = employees.Count > 0 && phone is not null;
        login.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (list.SelectedItem is not EmployeeRosterItem employee)
            {
                SmartConfirmDialog.ShowInfo(this, Title, "Bitte einen Fahrer wählen.");
                return;
            }

            var pin = KomRemoteDriverLoginService.EmployeeRosterItemPin(employee.PersonnelNumber);
            if (pin.Length != 4)
            {
                SmartConfirmDialog.ShowInfo(this, Title, "Personalnummer ungültig (keine 4 Ziffern).");
                return;
            }

            if (!SmartConfirmDialog.ShowConfirm(
                    this,
                    Title,
                    $"{employee.DisplayLabel} auf {vehicle.DisplayName} anmelden?"))
            {
                return;
            }

            await SendAsync(
                list,
                status,
                cancel,
                logout,
                login,
                phone,
                vehicle.DisplayName,
                login: true,
                employee);
        };

        bar.Children.Add(cancel);
        bar.Children.Add(logout);
        bar.Children.Add(login);
        root.Children.Add(bar);

        VehicleKomUi.PrepareWindow(this, root);
    }

    private async Task SendAsync(
        ListBox list,
        TextBlock status,
        Button cancel,
        Button logout,
        Button? loginButton,
        string phone,
        string vehicleName,
        bool login,
        EmployeeRosterItem? employee)
    {
        list.IsEnabled = false;
        cancel.IsEnabled = false;
        logout.IsEnabled = false;
        if (loginButton is not null)
        {
            loginButton.IsEnabled = false;
        }

        _sendGuard.BeginSend();
        try
        {
            if (await KomCommandSendFlow.SendAndReleaseDialogAsync(
                    this,
                    status,
                    vehicleName,
                    phone,
                    KomRemoteDriverLoginService.CommandType,
                    ct => login
                        ? KomRemoteDriverLoginService.UploadLoginAsync(
                            AppServices.Dropbox,
                            phone,
                            KomRemoteDriverLoginService.EmployeeRosterItemPin(employee!.PersonnelNumber),
                            employee.PersonnelNumber,
                            employee.Name,
                            ct)
                        : KomRemoteDriverLoginService.UploadLogoutAsync(AppServices.Dropbox, phone, ct)))
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
                list.IsEnabled = list.Items.Count > 0;
                cancel.IsEnabled = true;
                logout.IsEnabled = true;
                if (loginButton is not null)
                {
                    loginButton.IsEnabled = list.Items.Count > 0;
                }
            }
        }
    }
}
