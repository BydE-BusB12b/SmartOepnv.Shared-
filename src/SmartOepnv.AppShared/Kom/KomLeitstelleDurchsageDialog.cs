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
    private readonly Button _sendButton;
    private readonly TabControl _modeTabs;
    private Button _recordButton = null!;
    private TextBox _ttsTextBox = null!;
    private ComboBox _voiceCombo = null!;
    private Button _previewButton = null!;
    private bool _hasRecording;

    public KomLeitstelleDurchsageDialog(VehicleListItemViewModel vehicle, Window owner)
    {
        Owner = owner;
        Title = "Fahrgastraum-Durchsage";
        Width = 520;
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
            "Selbst sprechen oder Text per Sprachausgabe – wird im Fahrgastraum abgespielt (max. 3 Min.).",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 12)));

        _status = VehicleKomUi.MakeText("Bereit.", 13, margin: new Thickness(0, 0, 0, 12), muted: true);
        root.Children.Add(_status);

        _modeTabs = new TabControl { Margin = new Thickness(0, 0, 0, 12) };
        _modeTabs.SelectionChanged += (_, _) =>
        {
            if (_recorder.IsRecording)
            {
                _ = StopRecordingAsync();
            }

            UpdateSendButtonState();
        };
        _modeTabs.Items.Add(BuildRecordingTab());
        _modeTabs.Items.Add(BuildTtsTab());
        root.Children.Add(_modeTabs);

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

    private TabItem BuildRecordingTab()
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(VehicleKomUi.MakeText(
            "Mikrofon: Aufnahme starten, sprechen, stoppen und senden.",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));

        _recordButton = VehicleKomUi.MakeButton(
            "Aufnahme starten",
            horizontalAlignment: HorizontalAlignment.Stretch);
        _recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        panel.Children.Add(_recordButton);

        return new TabItem { Header = "Sprechen", Content = panel };
    }

    private TabItem BuildTtsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(VehicleKomUi.MakeText(
            $"Text eingeben (max. {LeitstelleDurchsageTts.MaxTextLength} Zeichen) – Windows-Sprachausgabe.",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));

        _ttsTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8)
        };
        VehicleKomUi.StyleTextBox(_ttsTextBox);
        _ttsTextBox.TextChanged += (_, _) => UpdateSendButtonState();
        panel.Children.Add(_ttsTextBox);

        _voiceCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        VehicleKomUi.StyleComboBox(_voiceCombo);
        foreach (var voice in LeitstelleDurchsageTts.ListVoices())
        {
            _voiceCombo.Items.Add(voice);
        }

        _voiceCombo.DisplayMemberPath = nameof(LeitstelleDurchsageTts.VoiceOption.DisplayName);
        if (_voiceCombo.Items.Count > 0)
        {
            _voiceCombo.SelectedIndex = 0;
        }
        else
        {
            panel.Children.Insert(1, VehicleKomUi.MakeText(
                "Keine Windows-Stimme gefunden – unter „Einstellungen → Sprache“ eine deutsche Stimme installieren.",
                12,
                muted: true,
                margin: new Thickness(0, 0, 0, 8)));
        }

        panel.Children.Add(VehicleKomUi.MakeText("Stimme", 12, muted: true, margin: new Thickness(0, 0, 0, 4)));
        panel.Children.Add(_voiceCombo);

        _previewButton = VehicleKomUi.MakeButton(
            "Anhören",
            horizontalAlignment: HorizontalAlignment.Stretch);
        _previewButton.Margin = new Thickness(0, 8, 0, 0);
        _previewButton.Click += (_, _) => PreviewTts();
        panel.Children.Add(_previewButton);

        return new TabItem { Header = "Text (TTS)", Content = panel };
    }

    private void PreviewTts()
    {
        var text = _ttsTextBox.Text.Trim();
        if (text.Length == 0)
        {
            SmartConfirmDialog.ShowInfo(this, Title, "Bitte zuerst Text eingeben.");
            return;
        }

        if (text.Length > LeitstelleDurchsageTts.MaxTextLength)
        {
            SmartConfirmDialog.ShowInfo(this, Title, $"Text ist zu lang (max. {LeitstelleDurchsageTts.MaxTextLength} Zeichen).");
            return;
        }

        var voice = _voiceCombo.SelectedItem as LeitstelleDurchsageTts.VoiceOption;
        try
        {
            _status.Text = "Sprachausgabe …";
            LeitstelleDurchsageTts.Preview(text, voice?.Name);
            _status.Text = "Anhören beendet.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Sprachausgabe fehlgeschlagen: {ex.Message}";
        }
    }

    private void UpdateSendButtonState()
    {
        if (_modeTabs.SelectedIndex == 0)
        {
            _sendButton.IsEnabled = _hasRecording;
            return;
        }

        var text = _ttsTextBox.Text.Trim();
        _sendButton.IsEnabled = text.Length > 0 && text.Length <= LeitstelleDurchsageTts.MaxTextLength;
    }

    private async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
        {
            await StopRecordingAsync();
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

    private async Task StopRecordingAsync()
    {
        _timer.Stop();
        await _recorder.StopAsync();
        _recordButton.Content = "Aufnahme starten";
        _hasRecording = true;
        _status.Text = "Aufnahme bereit zum Senden.";
        UpdateSendButtonState();
    }

    private async Task SendAsync(VehicleListItemViewModel vehicle, string? phone)
    {
        if (!VehicleKomUi.EnsureDropboxConnected(this) || phone is null)
        {
            return;
        }

        var isTts = _modeTabs.SelectedIndex == 1;
        if (!isTts && !_hasRecording)
        {
            SmartConfirmDialog.ShowInfo(this, Title, "Bitte zuerst eine Aufnahme erstellen.");
            return;
        }

        if (isTts && _ttsTextBox.Text.Trim().Length == 0)
        {
            SmartConfirmDialog.ShowInfo(this, Title, "Bitte zuerst Text eingeben.");
            return;
        }

        var confirmDetail = isTts
            ? $"Text-Durchsage jetzt an {vehicle.DisplayName} senden?\n\nErst nach „Ja“ wird die Sprachausgabe erzeugt und hochgeladen."
            : $"Aufnahme jetzt an {vehicle.DisplayName} senden?\n\nErst nach „Ja“ wird die Durchsage hochgeladen.";
        var confirm = SmartConfirmDialog.ShowConfirm(this, Title, confirmDetail);
        if (!confirm)
        {
            return;
        }

        _sendButton.IsEnabled = false;
        _recordButton.IsEnabled = false;
        _previewButton.IsEnabled = false;
        try
        {
            byte[]? bytes;
            if (isTts)
            {
                _status.Text = "Erzeuge Sprachausgabe …";
                var ttsText = _ttsTextBox.Text;
                var voiceName = (_voiceCombo.SelectedItem as LeitstelleDurchsageTts.VoiceOption)?.Name;
                bytes = await Task.Run(() =>
                    LeitstelleDurchsageTts.SynthesizeToM4aBytes(ttsText, voiceName)).ConfigureAwait(true);
            }
            else
            {
                if (_recorder.IsRecording)
                {
                    _timer.Stop();
                    await _recorder.StopAsync();
                }

                bytes = _recorder.FinishToM4aBytes();
                _hasRecording = false;
            }

            if (bytes is null || bytes.Length == 0)
            {
                _status.Text = isTts ? "Sprachausgabe leer oder fehlgeschlagen." : "Aufnahme leer oder ungültig.";
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
            _previewButton.IsEnabled = true;
            UpdateSendButtonState();
        }
    }
}
