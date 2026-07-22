using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Bestätigung und Versand: Gerät sperren / entsperren.</summary>
public sealed class KomRemoteLockDialog : Window
{
    private readonly KomSendDialogGuard _sendGuard;

    public KomRemoteLockDialog(VehicleListItemViewModel vehicle, Window owner, bool locked)
    {
        _sendGuard = new KomSendDialogGuard(this);
        Owner = owner;
        Title = locked ? "Gerät sperren" : "Gerät entsperren";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var root = new StackPanel();
        root.Children.Add(VehicleKomUi.MakeText(
            locked
                ? $"Gerät von {vehicle.DisplayName} sperren?"
                : $"Gerät von {vehicle.DisplayName} entsperren?",
            17,
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 8)));
        root.Children.Add(VehicleKomUi.MakeText(
            locked
                ? "Das Fahrzeug-Tablet/Handy zeigt eine Sperrfläche und ist nicht bedienbar, bis entsperrt wird."
                : "Die Fernsperre wird aufgehoben – das Gerät ist wieder bedienbar.",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 12)));

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
        var send = VehicleKomUi.MakeButton(
            locked ? "Sperren senden" : "Entsperren senden",
            primary: true,
            isDefault: true,
            minWidth: 150);
        send.IsEnabled = phone is not null;
        send.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            if (!SmartConfirmDialog.ShowConfirm(
                    this,
                    Title,
                    locked
                        ? $"Wirklich Sperre an {vehicle.DisplayName} senden?"
                        : $"Wirklich Entsperren an {vehicle.DisplayName} senden?"))
            {
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
                        KomRemoteLockService.CommandType,
                        ct => KomRemoteLockService.UploadAsync(AppServices.Dropbox, phone, locked, ct)))
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
        bar.Children.Add(cancel);
        bar.Children.Add(send);
        root.Children.Add(bar);

        VehicleKomUi.PrepareWindow(this, root);
    }
}
