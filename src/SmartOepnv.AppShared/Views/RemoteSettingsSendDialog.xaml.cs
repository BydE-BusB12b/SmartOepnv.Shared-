using System.Windows;
using System.Windows.Controls;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class RemoteSettingsSendDialog : Window
{
    public string? SelectedPhoneNumber { get; private set; }
    public string? SelectedVehicleName { get; private set; }
    public RemoteDeviceSettingsPayload? Settings { get; private set; }

    private readonly ComboBox? _vehicleCombo;
    private readonly TextBox? _phoneBox;
    private readonly CheckBox _buttonSounds;
    private readonly CheckBox _autoOpenRoute;
    private readonly CheckBox _nightMode;
    private readonly ComboBox _gainCombo;
    private readonly CheckBox _tftTime;
    private readonly CheckBox _interiorNext3;
    private readonly CheckBox _gorba;
    private readonly CheckBox _tftLawo;
    private readonly CheckBox _hideNav;
    private readonly CheckBox _autoHideNavGps;
    private readonly ComboBox _zblMode;
    private readonly CheckBox _tftTcp;
    private readonly TextBox _tftPort;
    private readonly CheckBox _clientOn;
    private readonly ComboBox _clientProto;
    private readonly TextBox _clientHost;
    private readonly TextBox _clientPort;
    private readonly ComboBox _protocol;
    private readonly ComboBox _displayMode;
    private readonly TextBlock _templateHint;
    private readonly RemoteDeviceSettingsTemplateStore _templateStore = new();

    public RemoteSettingsSendDialog(IReadOnlyList<RegisteredVehicleInfo> vehicles)
    {
        Title = "Einstellungen senden";
        Width = 640;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Gerät und Tablet-Einstellungen wählen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Es wird remote_settings_(Telefonnummer).json nach Dropbox gesendet. Das Tablet übernimmt die Werte automatisch (innerhalb von ca. 10 s).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
            Opacity = 0.9
        });

        if (vehicles.Count > 0)
        {
            root.Children.Add(Label("Fahrzeug"));
            _vehicleCombo = new ComboBox
            {
                ItemsSource = vehicles.Select(v => $"{v.Name}  ({v.PhoneNumber})").ToList(),
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(_vehicleCombo);
        }
        else
        {
            root.Children.Add(Label("Telefonnummer (keine Fahrzeuge in JSON)"));
            _phoneBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(_phoneBox);
        }

        var templateRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var saveTemplate = new Button
        {
            Content = "Als Vorlage speichern",
            MinWidth = 150,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        saveTemplate.Click += (_, _) => SaveTemplate();
        var loadTemplate = new Button
        {
            Content = "Vorlage laden",
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4)
        };
        loadTemplate.Click += (_, _) => LoadTemplate();
        templateRow.Children.Add(saveTemplate);
        templateRow.Children.Add(loadTemplate);
        root.Children.Add(templateRow);

        _templateHint = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_templateHint);
        RefreshTemplateHint();

        root.Children.Add(Section("Einstellungen"));
        _buttonSounds = Check("Tastentöne aktivieren", true);
        _autoOpenRoute = Check("Automatische Routen-Öffnung", false);
        _nightMode = Check("Nachtmodus", false);
        root.Children.Add(_buttonSounds);
        root.Children.Add(_autoOpenRoute);
        root.Children.Add(_nightMode);

        root.Children.Add(Label("Fahrgastraum Stimmenverstärkung"));
        _gainCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        for (var p = -60; p <= 120; p += 20)
        {
            _gainCombo.Items.Add($"{p}%");
        }
        _gainCombo.SelectedIndex = 3; // 0%
        root.Children.Add(_gainCombo);

        _tftTime = Check("TFT Zeit", false);
        _interiorNext3 = Check("Innenanzeige: nächste 3 Haltestellen senden", false);
        _gorba = Check("Gorba TFT", false);
        _tftLawo = Check("TFT Lawo", false);
        _hideNav = Check("Status- und Navigationsleiste ausblenden", false);
        _autoHideNavGps = Check("Automatisch bei GPS-Betrieb", true);
        root.Children.Add(_tftTime);
        root.Children.Add(_interiorNext3);
        root.Children.Add(_gorba);
        root.Children.Add(_tftLawo);
        root.Children.Add(_hideNav);
        root.Children.Add(_autoHideNavGps);

        root.Children.Add(Label("ZBL-Button"));
        _zblMode = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _zblMode.Items.Add("Telefonnummer anrufen");
        _zblMode.Items.Add("Sprechwunsch an Leitstelle");
        _zblMode.SelectedIndex = 0;
        root.Children.Add(_zblMode);

        root.Children.Add(Section("TCP/IP TFT-Verbindungen"));
        _tftTcp = Check("TFT Anzeige über TCP/IP aktiv", false);
        root.Children.Add(_tftTcp);
        root.Children.Add(Label("Port (Server)"));
        _tftPort = new TextBox { Text = "5000", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_tftPort);
        _clientOn = Check("IBIS-Client (TCP/UDP, z. B. Lawo) aktiv", false);
        root.Children.Add(_clientOn);
        root.Children.Add(Label("Client-Protokoll"));
        _clientProto = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _clientProto.Items.Add("TCP");
        _clientProto.Items.Add("UDP");
        _clientProto.SelectedIndex = 0;
        root.Children.Add(_clientProto);
        root.Children.Add(Label("Client Ziel-IP"));
        _clientHost = new TextBox { Text = "192.168.1.20", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_clientHost);
        root.Children.Add(Label("Client Ziel-Port"));
        _clientPort = new TextBox { Text = "5000", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_clientPort);

        root.Children.Add(Section("Innen-/Außenanzeigen"));
        root.Children.Add(Label("Protokoll"));
        _protocol = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var p in new[]
                 {
                     "DS021T", "DS021", "DS021neu", "FMA-S1", "DS003", "DS003a",
                     "DS003a_Krefeld", "DS003a_UESTRA", "IBIS-IP"
                 })
        {
            _protocol.Items.Add(p);
        }
        _protocol.SelectedIndex = 0;
        root.Children.Add(_protocol);

        root.Children.Add(Label("Außenanzeige"));
        _displayMode = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        _displayMode.Items.Add("SICMA/LAWO (Zielnummer)");
        _displayMode.Items.Add("Direkt: Klartext");
        _displayMode.SelectedIndex = 0;
        root.Children.Add(_displayMode);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = new Button { Content = "Einstellungen senden", MinWidth = 160, IsDefault = true };
        ok.Click += (_, _) => OnOk(vehicles);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        scroll.Content = root;
        Content = scroll;

        TryApplyLastUsedTemplate();
    }

    private void RefreshTemplateHint()
    {
        var doc = _templateStore.Load();
        if (doc.Templates.Count == 0)
        {
            _templateHint.Text = "Noch keine Vorlage gespeichert.";
            return;
        }

        var last = string.IsNullOrWhiteSpace(doc.LastUsedName)
            ? doc.Templates[^1].Name
            : doc.LastUsedName;
        _templateHint.Text = $"{doc.Templates.Count} Vorlage(n) lokal · zuletzt: {last}";
    }

    private void TryApplyLastUsedTemplate()
    {
        var doc = _templateStore.Load();
        if (doc.Templates.Count == 0)
        {
            return;
        }

        var named = doc.Templates.FirstOrDefault(t =>
                        string.Equals(t.Name, doc.LastUsedName, StringComparison.OrdinalIgnoreCase))
                    ?? doc.Templates[^1];
        ApplySettingsToUi(named.Settings);
    }

    private void SaveTemplate()
    {
        if (!TryCollectSettings(out var settings, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var doc = _templateStore.Load();
        var suggested = string.IsNullOrWhiteSpace(doc.LastUsedName) ? "Standard" : doc.LastUsedName!;
        var name = PromptTemplateName("Vorlage speichern", "Name der Vorlage:", suggested);
        if (name is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Bitte einen Namen eingeben.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var exists = doc.Templates.Any(t =>
            string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            var overwrite = MessageBox.Show(
                this,
                $"Vorlage „{name.Trim()}“ überschreiben?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _templateStore.Upsert(name, settings);
        RefreshTemplateHint();
        MessageBox.Show(this, $"Vorlage „{name.Trim()}“ gespeichert.", Title,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadTemplate()
    {
        var doc = _templateStore.Load();
        if (doc.Templates.Count == 0)
        {
            MessageBox.Show(this, "Keine Vorlage vorhanden. Zuerst „Als Vorlage speichern“ nutzen.",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var names = doc.Templates.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var picked = PromptPickTemplate(names, doc.LastUsedName);
        if (picked is null)
        {
            return;
        }

        var named = doc.Templates.FirstOrDefault(t =>
            string.Equals(t.Name, picked, StringComparison.OrdinalIgnoreCase));
        if (named?.Settings is null)
        {
            MessageBox.Show(this, "Vorlage konnte nicht geladen werden.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplySettingsToUi(named.Settings);
        doc.LastUsedName = named.Name;
        _templateStore.Save(doc);
        RefreshTemplateHint();
    }

    private void OnOk(IReadOnlyList<RegisteredVehicleInfo> vehicles)
    {
        if (_vehicleCombo is not null && _vehicleCombo.SelectedIndex >= 0)
        {
            var vehicle = vehicles[_vehicleCombo.SelectedIndex];
            SelectedPhoneNumber = vehicle.PhoneNumber;
            SelectedVehicleName = vehicle.Name;
        }
        else if (_phoneBox is not null)
        {
            SelectedPhoneNumber = _phoneBox.Text.Trim();
            SelectedVehicleName = SelectedPhoneNumber;
        }

        if (string.IsNullOrWhiteSpace(SelectedPhoneNumber))
        {
            MessageBox.Show(this, "Bitte Telefonnummer eingeben oder Fahrzeug wählen.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryCollectSettings(out var settings, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings = settings;
        DialogResult = true;
        Close();
    }

    private bool TryCollectSettings(out RemoteDeviceSettingsPayload settings, out string error)
    {
        settings = new RemoteDeviceSettingsPayload();
        error = "";

        if (!int.TryParse(_tftPort.Text.Trim(), out var tftPort) || tftPort is < 1 or > 65535)
        {
            error = "Ungültiger TFT-Server-Port.";
            return false;
        }

        if (!int.TryParse(_clientPort.Text.Trim(), out var clientPort) || clientPort is < 1 or > 65535)
        {
            error = "Ungültiger Client-Ziel-Port.";
            return false;
        }

        var gainPercent = (_gainCombo.SelectedIndex * 20) - 60;
        settings = new RemoteDeviceSettingsPayload
        {
            ButtonSoundsEnabled = _buttonSounds.IsChecked == true,
            AutoOpenNextRoute = _autoOpenRoute.IsChecked == true,
            NightModeEnabled = _nightMode.IsChecked == true,
            FahrgastraumGainPercent = gainPercent,
            TftTimeEnabled = _tftTime.IsChecked == true,
            InteriorAltProtocolEnabled = _interiorNext3.IsChecked == true,
            ProtranStopsEnabled = _gorba.IsChecked == true,
            TftLawoEnabled = _tftLawo.IsChecked == true,
            HideNavigationBarEnabled = _hideNav.IsChecked == true,
            AutoHideNavigationOnGps = _autoHideNavGps.IsChecked == true,
            ZblContactMode = _zblMode.SelectedIndex == 1 ? "sprechwunsch" : "call",
            TftTcpEnabled = _tftTcp.IsChecked == true,
            TftTcpPort = tftPort,
            TcpSocketClientEnabled = _clientOn.IsChecked == true,
            TcpSocketProtocol = _clientProto.SelectedItem as string ?? "TCP",
            TcpSocketHost = _clientHost.Text.Trim(),
            TcpSocketPort = clientPort,
            SelectedProtocol = _protocol.SelectedItem as string ?? "DS021T",
            IbisDisplayControlMode = _displayMode.SelectedIndex == 1
                ? "DIRECT_CLEARTEXT"
                : "SICMA_ZD_TARGET_NUMBER"
        };
        return true;
    }

    private void ApplySettingsToUi(RemoteDeviceSettingsPayload s)
    {
        if (s.ButtonSoundsEnabled is bool buttonSounds)
        {
            _buttonSounds.IsChecked = buttonSounds;
        }

        if (s.AutoOpenNextRoute is bool autoOpen)
        {
            _autoOpenRoute.IsChecked = autoOpen;
        }

        if (s.NightModeEnabled is bool night)
        {
            _nightMode.IsChecked = night;
        }

        if (s.FahrgastraumGainPercent is int gain)
        {
            var clamped = ((gain / 20) * 20).Clamp(-60, 120);
            _gainCombo.SelectedIndex = ((clamped + 60) / 20).Clamp(0, _gainCombo.Items.Count - 1);
        }

        if (s.TftTimeEnabled is bool tftTime)
        {
            _tftTime.IsChecked = tftTime;
        }

        if (s.InteriorAltProtocolEnabled is bool interior)
        {
            _interiorNext3.IsChecked = interior;
        }

        if (s.ProtranStopsEnabled is bool gorba)
        {
            _gorba.IsChecked = gorba;
        }

        if (s.TftLawoEnabled is bool lawo)
        {
            _tftLawo.IsChecked = lawo;
        }

        if (s.HideNavigationBarEnabled is bool hideNav)
        {
            _hideNav.IsChecked = hideNav;
        }

        if (s.AutoHideNavigationOnGps is bool autoHide)
        {
            _autoHideNavGps.IsChecked = autoHide;
        }

        if (!string.IsNullOrWhiteSpace(s.ZblContactMode))
        {
            _zblMode.SelectedIndex =
                string.Equals(s.ZblContactMode, "sprechwunsch", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        if (s.TftTcpEnabled is bool tftTcp)
        {
            _tftTcp.IsChecked = tftTcp;
        }

        if (s.TftTcpPort is int tftPort)
        {
            _tftPort.Text = tftPort.ToString();
        }

        if (s.TcpSocketClientEnabled is bool clientOn)
        {
            _clientOn.IsChecked = clientOn;
        }

        if (!string.IsNullOrWhiteSpace(s.TcpSocketProtocol))
        {
            _clientProto.SelectedIndex =
                string.Equals(s.TcpSocketProtocol, "UDP", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        if (s.TcpSocketHost is not null)
        {
            _clientHost.Text = s.TcpSocketHost;
        }

        if (s.TcpSocketPort is int clientPort)
        {
            _clientPort.Text = clientPort.ToString();
        }

        if (!string.IsNullOrWhiteSpace(s.SelectedProtocol))
        {
            for (var i = 0; i < _protocol.Items.Count; i++)
            {
                if (string.Equals(_protocol.Items[i] as string, s.SelectedProtocol, StringComparison.OrdinalIgnoreCase))
                {
                    _protocol.SelectedIndex = i;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(s.IbisDisplayControlMode))
        {
            _displayMode.SelectedIndex =
                string.Equals(s.IbisDisplayControlMode, "DIRECT_CLEARTEXT", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
        }
    }

    private string? PromptTemplateName(string title, string prompt, string defaultName)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var box = new TextBox { Text = defaultName };
        panel.Children.Add(box);
        string? result = null;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => win.Close();
        var ok = new Button { Content = "Speichern", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) =>
        {
            result = box.Text;
            win.DialogResult = true;
            win.Close();
        };
        row.Children.Add(cancel);
        row.Children.Add(ok);
        panel.Children.Add(row);
        win.Content = panel;
        box.SelectAll();
        box.Focus();
        return win.ShowDialog() == true ? result : null;
    }

    private string? PromptPickTemplate(IReadOnlyList<string> names, string? preferred)
    {
        var win = new Window
        {
            Title = "Vorlage laden",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Gespeicherte Vorlage wählen:",
            Margin = new Thickness(0, 0, 0, 8)
        });
        var combo = new ComboBox { ItemsSource = names };
        var preferredIndex = names.ToList().FindIndex(n =>
            string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
        panel.Children.Add(combo);
        string? result = null;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => win.Close();
        var ok = new Button { Content = "Laden", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) =>
        {
            result = combo.SelectedItem as string;
            win.DialogResult = true;
            win.Close();
        };
        row.Children.Add(cancel);
        row.Children.Add(ok);
        panel.Children.Add(row);
        win.Content = panel;
        return win.ShowDialog() == true ? result : null;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 15,
        Margin = new Thickness(0, 12, 0, 6)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 2),
        Opacity = 0.9
    };

    private static CheckBox Check(string text, bool isChecked) => new()
    {
        Content = text,
        IsChecked = isChecked,
        Margin = new Thickness(0, 2, 0, 2)
    };
}

internal static class RemoteSettingsIntClamp
{
    public static int Clamp(this int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
