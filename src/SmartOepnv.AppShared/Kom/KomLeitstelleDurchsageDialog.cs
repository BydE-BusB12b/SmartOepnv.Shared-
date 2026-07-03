using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

public sealed class KomLeitstelleDurchsageDialog : Window
{
    private readonly LeitstelleDurchsageRecorder _recorder = new();
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _status;
    private readonly Button _recordButton;
    private readonly Button _sendButton;
    private bool _hasRecording;

    public KomLeitstelleDurchsageDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        Owner = owner;
        Title = "Fahrgastraum-Durchsage";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closed += (_, _) => _recorder.Dispose();

        var phone = VehicleKomUi.ResolvePhoneOrWarn(this, vehicle);
        if (phone is null)
        {
            Loaded += (_, _) => { DialogResult = false; Close(); };
        }

        var root = new StackPanel();
        root.Children.Add(VehicleKomUi.MakeText(
            $"Durchsage an {vehicle.DisplayName}",
            17,
            FontWeights.SemiBold));
        root.Children.Add(VehicleKomUi.MakeText(
            "Aufnahme starten, sprechen, stoppen und senden – wird im Fahrgastraum abgespielt (max. 3 Min.).",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 12)));

        _status = VehicleKomUi.MakeText("Bereit zur Aufnahme.", 13, margin: new Thickness(0, 0, 0, 12), muted: true);
        root.Children.Add(_status);

        _recordButton = VehicleKomUi.MakeButton(
            "Aufnahme starten",
            horizontalAlignment: HorizontalAlignment.Stretch);
        _recordButton.Margin = new Thickness(0, 0, 0, 8);
        _recordButton.IsDefault = false;
        _recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        root.Children.Add(_recordButton);

        _sendButton = VehicleKomUi.MakeButton(
            "Durchsage senden",
            primary: true,
            horizontalAlignment: HorizontalAlignment.Stretch);
        _sendButton.Margin = new Thickness(0, 0, 0, 8);
        _sendButton.IsEnabled = false;
        _sendButton.IsDefault = false;
        _sendButton.Click += async (_, _) => await SendAsync(vehicle, phone);
        root.Children.Add(_sendButton);

        var cancel = VehicleKomUi.MakeButton(
            "Schließen",
            isCancel: true,
            horizontalAlignment: HorizontalAlignment.Right);
        cancel.IsDefault = false;
        cancel.Click += (_, _) => Close();
        root.Children.Add(cancel);

        VehicleKomUi.PrepareWindow(this, root);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            if (_recorder.IsRecording)
            {
                _status.Text = $"Aufnahme läuft … {_recorder.Elapsed.TotalSeconds:0} s";
            }
        };
    }

    private async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
        {
            _timer.Stop();
            await _recorder.StopAsync();
            _recordButton.Content = "Aufnahme starten";
            _hasRecording = true;
            _status.Text = "Aufnahme bereit zum Senden.";
            _sendButton.IsEnabled = true;
            return;
        }

        _recorder.Discard();
        _hasRecording = false;
        _sendButton.IsEnabled = false;
        try
        {
            _recorder.Start();
            _recordButton.Content = "Aufnahme stoppen";
            _status.Text = "Aufnahme läuft … 0 s";
            _timer.Start();
        }
        catch (Exception ex)
        {
            _status.Text = $"Mikrofon nicht verfügbar: {ex.Message}";
        }
    }

    private async Task SendAsync(VehicleListItemViewModel vehicle, string? phone)
    {
        if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
        {
            return;
        }

        if (!_hasRecording)
        {
            SmartConfirmDialog.ShowInfo(this, Title, "Bitte zuerst eine Aufnahme erstellen.");
            return;
        }

        var confirm = SmartConfirmDialog.ShowConfirm(
            this,
            Title,
            $"Aufnahme jetzt an {vehicle.DisplayName} senden?\n\nErst nach „Ja“ wird die Durchsage hochgeladen.");
        if (!confirm)
        {
            return;
        }

        _sendButton.IsEnabled = false;
        _recordButton.IsEnabled = false;
        try
        {
            if (_recorder.IsRecording)
            {
                _timer.Stop();
                await _recorder.StopAsync();
            }

            var bytes = _recorder.FinishToM4aBytes();
            _hasRecording = false;
            if (bytes is null || bytes.Length == 0)
            {
                _status.Text = "Aufnahme leer oder ungültig.";
                return;
            }

            var outcome = await KomCommandSendFlow.ExecuteAsync(
                this,
                _status,
                vehicle.DisplayName,
                phone,
                KomLeitstelleDurchsageService.CommandType,
                ct => KomLeitstelleDurchsageService.UploadAsync(AppServices.Dropbox, phone, bytes, ct));
            if (outcome is KomCommandSendOutcome.Success or KomCommandSendOutcome.Timeout)
            {
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"Fehler: {ex.Message}";
        }
        finally
        {
            _recordButton.IsEnabled = true;
            _sendButton.IsEnabled = false;
        }
    }
}
