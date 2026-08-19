using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Bestätigung und Versand: App beenden (Abmelden + Prozessende).</summary>
public sealed class KomRemoteAppExitDialog : Window
{
    private readonly KomSendDialogGuard _sendGuard;

    public KomRemoteAppExitDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        _sendGuard = new KomSendDialogGuard(this);
        Owner = owner;
        Title = "App beenden";
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
            $"App auf {vehicle.DisplayName} beenden?",
            17,
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 8)));
        root.Children.Add(VehicleKomUi.MakeText(
            "Der Fahrer wird abgemeldet und die GPSAnsagen-App beendet. " +
            "So kann das Tablet in den Standby gehen, falls die App offen geblieben ist.",
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
            "App beenden senden",
            primary: true,
            isDefault: true,
            minWidth: 170);
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
                    $"Wirklich App-Beenden an {vehicle.DisplayName} senden?\n\nFahrer wird abgemeldet, App wird geschlossen."))
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
                        KomRemoteAppExitService.CommandType,
                        ct => KomRemoteAppExitService.UploadAsync(AppServices.Dropbox, phone, ct)))
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
