using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

public sealed class VehicleKomMessageDialog : Window
{
    private readonly KomSendDialogGuard _sendGuard;

    public VehicleKomMessageDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        _sendGuard = new KomSendDialogGuard(this);
        Owner = owner;
        Title = "Meldung senden";
        Width = 520;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var templates = LoadTemplates();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = 0;
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            $"Meldung an {vehicle.DisplayName}",
            17,
            FontWeights.SemiBold), row++));
        root.Children.Add(MakeAtRow(VehicleKomUi.MakeText(
            templates.Count == 0
                ? "Keine Vorlagen im Paket – Text manuell eingeben (messageTemplates im Planer)."
                : "Vorlage wählen oder Text anpassen – wird wie „Meldung“ in der App per Dropbox gesendet.",
            13), row++));

        var templateBox = new ComboBox
        {
            ItemsSource = templates,
            Margin = new Thickness(0, 0, 0, 8),
            IsEditable = false
        };
        VehicleKomUi.StyleComboBox(templateBox);
        if (templates.Count > 0)
        {
            templateBox.SelectedIndex = 0;
        }

        Grid.SetRow(templateBox, row++);
        root.Children.Add(templateBox);

        var messageBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120,
            Text = templates.FirstOrDefault() ?? string.Empty
        };
        VehicleKomUi.StyleTextBox(messageBox);
        templateBox.SelectionChanged += (_, _) =>
        {
            if (templateBox.SelectedItem is string template)
            {
                messageBox.Text = template;
            }
        };

        Grid.SetRow(messageBox, row++);
        root.Children.Add(messageBox);

        var status = VehicleKomUi.MakeText(string.Empty, 12, margin: new Thickness(0, 8, 0, 0), muted: true);
        Grid.SetRow(status, row++);
        root.Children.Add(status);

        var cancel = VehicleKomUi.MakeButton("Abbrechen", margin: new Thickness(0, 0, 8, 0), isCancel: true);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var send = VehicleKomUi.MakeButton(
            "Meldung senden",
            primary: true,
            isDefault: true,
            minWidth: 130);
        send.IsEnabled = phone is not null;
        send.Click += async (_, _) =>
        {
            if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
            {
                return;
            }

            var text = messageBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                SmartConfirmDialog.ShowInfo(this, Title, "Bitte Nachrichtentext eingeben.");
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
                    ZblMessageService.CommandType,
                    ct => AppServices.Dropbox.UploadZblMessageAsync(phone, text, ct)))
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
    }

    private static List<string> LoadTemplates()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return [];
        }

        return editor.MessageTemplates
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    private static UIElement MakeAtRow(UIElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }
}
