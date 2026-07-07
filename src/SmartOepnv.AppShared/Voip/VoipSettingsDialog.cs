using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.Kom;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

public sealed class VoipSettingsDialog : Window
{
    private readonly VoipSettingsStore _store = new("Leitstelle");
    private readonly TextBox _publicHost;
    private readonly TextBox _depotLanHost;
    private readonly TextBox _funnelHost;
    private readonly TextBox _listenHost;
    private readonly TextBox _port;
    private readonly TextBox _turnHost;
    private readonly TextBox _turnPort;
    private readonly TextBox _turnUsername;
    private readonly TextBox _turnPassword;
    private readonly CheckBox _enabled;
    private readonly CheckBox _autoManageFunnel;
    private readonly TextBlock _hintText;
    private readonly RadioButton _modeManaged;
    private readonly StackPanel _managedPanel;
    private readonly TextBox _managedTurnPassword;
    private readonly CheckBox _showExpert;
    private readonly StackPanel _expertPanel;
    private readonly RadioButton _modeDepot;
    private readonly RadioButton _modeMobile;
    private readonly RadioButton _modeDual;
    private readonly RadioButton _modeFunnel;
    private readonly RadioButton _modeCloud;
    private readonly StackPanel _depotPanel;
    private readonly StackPanel _mobilePanel;
    private readonly StackPanel _funnelPanel;
    private readonly StackPanel _cloudPanel;
    private readonly TextBox _cloudHost;
    private readonly TextBox _cloudPort;
    private readonly CheckBox _cloudUseTls;

    public VoipSettings Settings { get; private set; }

    public VoipSettingsDialog(Window owner, VoipSettings? current = null)
    {
        Owner = owner;
        Title = "Funk / VoIP-Einstellungen";
        Width = 580;
        MinHeight = 420;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.85);
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Settings = current ?? _store.Load();
        if (Settings.ConnectivityMode == VoipConnectivityMode.Cloud &&
            string.Equals(
                VoipTailscaleFunnel.NormalizeHostname(Settings.CloudSignalingHost),
                VoipManagedCloud.DefaultHost,
                StringComparison.OrdinalIgnoreCase))
        {
            VoipManagedCloud.ApplyTo(Settings, Settings.TurnPassword);
        }

        if (string.IsNullOrWhiteSpace(Settings.DepotLanHost) &&
            VoipReachability.IsPrivateOrLocalHost(Settings.PublicSignalingHost))
        {
            Settings.DepotLanHost = Settings.PublicSignalingHost;
        }

        var root = new StackPanel();
        root.Children.Add(VehicleKomUi.MakeText(
            "Funk für Tablets – mit Smart ÖPNV Cloud kein eigener Server beim Kunden nötig.",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 12)));

        _enabled = new CheckBox
        {
            Content = "VoIP/Funk aktiviert",
            IsChecked = Settings.Enabled,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_enabled);

        _modeManaged = new RadioButton
        {
            Content = "Smart ÖPNV Funk (empfohlen) – auch unterwegs / LTE",
            IsChecked = Settings.ConnectivityMode is VoipConnectivityMode.ManagedCloud or VoipConnectivityMode.Cloud,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _modeDepot = new RadioButton
        {
            Content = "Nur Betriebshof / WLAN – Tablet im gleichen Netz wie dieser PC",
            IsChecked = Settings.ConnectivityMode == VoipConnectivityMode.DepotWlan,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _modeCloud = new RadioButton
        {
            Content = "Eigener Cloud/VPS-Server (Experte)",
            IsChecked = Settings.ConnectivityMode == VoipConnectivityMode.Cloud &&
                         !VoipManagedCloud.IsManagedMode(Settings),
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _modeFunnel = new RadioButton
        {
            Content = "Tailscale Funnel (Experte)",
            IsChecked = Settings.ConnectivityMode == VoipConnectivityMode.TailscaleFunnel,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _modeMobile = new RadioButton
        {
            Content = "Öffentliche IP / Router (Legacy)",
            IsChecked = Settings.ConnectivityMode == VoipConnectivityMode.MobilePublic,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _modeDual = new RadioButton
        {
            Content = "Automatisch WLAN + öffentliche IP (Legacy)",
            IsChecked = Settings.ConnectivityMode == VoipConnectivityMode.Dual,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _modeManaged.Checked += (_, _) => RefreshUi();
        _modeDepot.Checked += (_, _) => RefreshUi();
        _modeCloud.Checked += (_, _) => RefreshUi();
        _modeFunnel.Checked += (_, _) => RefreshUi();
        _modeMobile.Checked += (_, _) => RefreshUi();
        _modeDual.Checked += (_, _) => RefreshUi();
        root.Children.Add(_modeManaged);

        _managedPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        _managedPanel.Children.Add(VehicleKomUi.MakeText(
            $"Cloud-Server: {VoipManagedCloud.DefaultHost}:{VoipConstants.DefaultSignalingPort} (vorkonfiguriert)",
            12,
            muted: true));
        _managedPanel.Children.Add(VehicleKomUi.MakeText(
            VoipManagedCloud.IsReady(Settings)
                ? "TURN-Passwort ist gespeichert. Nur ändern, wenn am Server ein neues Passwort gesetzt wurde."
                : "Einmalig: TURN-Passwort aus der Server-Installation eintragen (steht am Ende von bash /tmp/voip-cloud-install.sh).",
            11,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        _managedPanel.Children.Add(VehicleKomUi.MakeText("TURN-Passwort (einmalig)", 12, muted: true));
        _managedTurnPassword = MakeInput(string.Empty);
        _managedPanel.Children.Add(_managedTurnPassword);
        root.Children.Add(_managedPanel);

        root.Children.Add(_modeDepot);

        _expertPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        _showExpert = new CheckBox
        {
            Content = "Erweiterte Einstellungen (eigener Server, Tailscale, TURN manuell)",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _showExpert.Checked += (_, _) =>
        {
            _expertPanel.Visibility = _showExpert.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            RefreshUi();
        };
        root.Children.Add(_showExpert);
        root.Children.Add(_expertPanel);

        _expertPanel.Children.Add(VehicleKomUi.MakeText("Experten-Modus", 12, FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 8)));
        _expertPanel.Children.Add(_modeCloud);
        _expertPanel.Children.Add(_modeFunnel);
        _expertPanel.Children.Add(_modeMobile);
        _expertPanel.Children.Add(_modeDual);

        _cloudPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _cloudPanel.Children.Add(VehicleKomUi.MakeText(
            "Cloud/VPS – Signaling und TURN auf einem Server (siehe VOIP_CLOUD_SETUP.md)",
            12,
            muted: true));
        _cloudPanel.Children.Add(VehicleKomUi.MakeText(
            "Tablets und Leitstelle verbinden sich nur ausgehend zum Server – kein Router, kein Tailscale.",
            11,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        _cloudPanel.Children.Add(VehicleKomUi.MakeText("Cloud-Server (Hostname oder IP)", 12, muted: true));
        _cloudHost = MakeInput(Settings.CloudSignalingHost);
        _cloudHost.TextChanged += (_, _) => RefreshUi();
        _cloudPanel.Children.Add(_cloudHost);
        _cloudUseTls = new CheckBox
        {
            Content = "TLS verwenden (wss://, Port 443 – empfohlen hinter nginx)",
            IsChecked = Settings.CloudSignalingUseTls,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _cloudUseTls.Checked += (_, _) => RefreshUi();
        _cloudUseTls.Unchecked += (_, _) => RefreshUi();
        _cloudPanel.Children.Add(_cloudUseTls);
        _cloudPanel.Children.Add(VehicleKomUi.MakeText(
            "Cloud-Port (leer = 443 bei TLS, sonst 8787)",
            12,
            muted: true,
            margin: new Thickness(0, 8, 0, 0)));
        _cloudPort = MakeInput(Settings.CloudSignalingPort > 0 ? Settings.CloudSignalingPort.ToString() : string.Empty);
        _cloudPanel.Children.Add(_cloudPort);

        _funnelPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _funnelPanel.Children.Add(VehicleKomUi.MakeText(
            "Tailscale Funnel (nur auf diesem PC – Tablets verbinden sich automatisch über Dropbox-Config)",
            12,
            muted: true));
        _funnelPanel.Children.Add(VehicleKomUi.MakeText(
            "Einmalig: Tailscale anmelden, dann „Funnel aktivieren“ (öffnet Browser).",
            11,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        var enableFunnel = VehicleKomUi.MakeButton(
            "Funnel aktivieren (Browser)",
            horizontalAlignment: HorizontalAlignment.Stretch);
        enableFunnel.Margin = new Thickness(0, 0, 0, 8);
        enableFunnel.Click += (_, _) => OpenFunnelEnablePage();
        _funnelPanel.Children.Add(enableFunnel);
        var detectFunnel = VehicleKomUi.MakeButton(
            "Funnel-URL ermitteln",
            primary: true,
            horizontalAlignment: HorizontalAlignment.Stretch);
        detectFunnel.Margin = new Thickness(0, 0, 0, 8);
        detectFunnel.Click += (_, _) => DetectFunnelUrl();
        _funnelPanel.Children.Add(detectFunnel);
        _funnelPanel.Children.Add(VehicleKomUi.MakeText("Funnel-Host (*.ts.net)", 12, muted: true));
        _funnelHost = MakeInput(Settings.TailscaleFunnelHost);
        _funnelHost.TextChanged += (_, _) => RefreshUi();
        _funnelPanel.Children.Add(_funnelHost);
        _autoManageFunnel = new CheckBox
        {
            Content = "Funnel automatisch mit VoIP starten/stoppen",
            IsChecked = Settings.AutoManageTailscaleFunnel,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _funnelPanel.Children.Add(_autoManageFunnel);
        _expertPanel.Children.Add(_funnelPanel);

        _depotPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var recommendedLan = VoipReachability.TryGetRecommendedLanHost();
        if (!string.IsNullOrWhiteSpace(recommendedLan))
        {
            _depotPanel.Children.Add(VehicleKomUi.MakeText(
                $"WLAN-IP dieses PCs: {recommendedLan}",
                12,
                muted: true,
                margin: new Thickness(0, 0, 0, 8)));
            var applyLan = VehicleKomUi.MakeButton(
                $"Betriebshof: WLAN-IP übernehmen ({recommendedLan})",
                primary: true,
                horizontalAlignment: HorizontalAlignment.Stretch);
            applyLan.Margin = new Thickness(0, 0, 0, 8);
            applyLan.Click += (_, _) => ApplyLanIp(recommendedLan);
            _depotPanel.Children.Add(applyLan);
        }

        _depotPanel.Children.Add(VehicleKomUi.MakeText("WLAN-IP (Betriebshof / Fallback)", 12, muted: true));
        _depotLanHost = MakeInput(Settings.DepotLanHost);
        _depotLanHost.TextChanged += (_, _) => RefreshUi();
        _depotPanel.Children.Add(_depotLanHost);
        root.Children.Add(_depotPanel);

        _mobilePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _mobilePanel.Children.Add(VehicleKomUi.MakeText(
            "Öffentliche IP oder DynDNS (Legacy)",
            12,
            muted: true));
        _mobilePanel.Children.Add(VehicleKomUi.MakeText(
            "Router: TCP-Port 8787 → IP dieses PCs weiterleiten.",
            11,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        var detectPublic = VehicleKomUi.MakeButton(
            "Öffentliche IP ermitteln",
            horizontalAlignment: HorizontalAlignment.Stretch);
        detectPublic.Margin = new Thickness(0, 0, 0, 8);
        detectPublic.Click += async (_, _) => await DetectPublicIpAsync().ConfigureAwait(true);
        _mobilePanel.Children.Add(detectPublic);
        _publicHost = MakeInput(
            Settings.ConnectivityMode is VoipConnectivityMode.DepotWlan or VoipConnectivityMode.TailscaleFunnel
                ? string.Empty
                : Settings.PublicSignalingHost);
        _publicHost.TextChanged += (_, _) => RefreshUi();
        _mobilePanel.Children.Add(_publicHost);
        _expertPanel.Children.Add(_mobilePanel);

        _hintText = VehicleKomUi.MakeText(BuildHint(), 11, muted: true, margin: new Thickness(0, 4, 0, 0));
        root.Children.Add(_hintText);

        _expertPanel.Children.Add(VehicleKomUi.MakeText(
            "Listener (0.0.0.0 = alle Netzwerkadapter, empfohlen)",
            12,
            muted: true,
            margin: new Thickness(0, 10, 0, 0)));
        _listenHost = MakeInput(
            string.IsNullOrWhiteSpace(Settings.ListenHost) || Settings.ListenHost == "127.0.0.1"
                ? "0.0.0.0"
                : Settings.ListenHost);
        _expertPanel.Children.Add(_listenHost);

        _expertPanel.Children.Add(VehicleKomUi.MakeText("Signaling-Port", 12, muted: true, margin: new Thickness(0, 8, 0, 0)));
        _port = MakeInput(Settings.SignalingPort.ToString());
        _expertPanel.Children.Add(_port);

        _expertPanel.Children.Add(VehicleKomUi.MakeText(
            "TURN-Server (öffentlich – für Sprechverbindung unterwegs/LTE nötig)",
            12,
            muted: true,
            margin: new Thickness(0, 8, 0, 0)));
        _expertPanel.Children.Add(VehicleKomUi.MakeText(
            "Ohne TURN: Signaling ok, aber Audio (ICE) schlägt fehl.",
            11,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        var turnButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var applyTestTurn = VehicleKomUi.MakeButton("Test-TURN übernehmen", horizontalAlignment: HorizontalAlignment.Stretch);
        applyTestTurn.Margin = new Thickness(0, 0, 8, 0);
        applyTestTurn.Click += (_, _) => ApplyTestTurnPreset();
        var testTurn = VehicleKomUi.MakeButton("TURN testen", horizontalAlignment: HorizontalAlignment.Stretch);
        testTurn.Click += (_, _) => TestTurnReachability();
        turnButtons.Children.Add(applyTestTurn);
        turnButtons.Children.Add(testTurn);
        _expertPanel.Children.Add(turnButtons);
        _expertPanel.Children.Add(VehicleKomUi.MakeText("TURN-Host (öffentlich, kein 192.168.x.x)", 12, muted: true));
        _turnHost = MakeInput(Settings.TurnHost);
        _expertPanel.Children.Add(_turnHost);
        _expertPanel.Children.Add(VehicleKomUi.MakeText("TURN-Port", 12, muted: true, margin: new Thickness(0, 6, 0, 0)));
        _turnPort = MakeInput(Settings.TurnPort.ToString());
        _expertPanel.Children.Add(_turnPort);
        _expertPanel.Children.Add(VehicleKomUi.MakeText("TURN-Benutzername", 12, muted: true, margin: new Thickness(0, 6, 0, 0)));
        _turnUsername = MakeInput(Settings.TurnUsername);
        _expertPanel.Children.Add(_turnUsername);
        _expertPanel.Children.Add(VehicleKomUi.MakeText("TURN-Passwort", 12, muted: true, margin: new Thickness(0, 6, 0, 0)));
        _turnPassword = MakeInput(Settings.TurnPassword);
        _expertPanel.Children.Add(_turnPassword);

        var applyCloudTurn = VehicleKomUi.MakeButton(
            "TURN = Cloud-Server übernehmen",
            horizontalAlignment: HorizontalAlignment.Stretch);
        applyCloudTurn.Margin = new Thickness(0, 8, 0, 0);
        applyCloudTurn.Click += (_, _) =>
        {
            var host = _cloudHost.Text.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                VehicleKomUi.SafeShowMessage(this, "Bitte zuerst Cloud-Server eintragen.", Title, MessageBoxImage.Warning);
                return;
            }

            _turnHost.Text = host;
            if (string.IsNullOrWhiteSpace(_turnPort.Text.Trim()) || _turnPort.Text.Trim() == "80")
            {
                _turnPort.Text = VoipConstants.DefaultTurnPort.ToString();
            }

            RefreshUi();
        };
        _cloudPanel.Children.Add(applyCloudTurn);
        _expertPanel.Children.Add(_cloudPanel);

        if (VoipWindowsPortSetup.IsPortReservationMissing(Settings))
        {
            var fixPort = VehicleKomUi.MakeButton(
                "VoIP-Port freigeben (Administrator)",
                primary: true,
                horizontalAlignment: HorizontalAlignment.Stretch);
            fixPort.Margin = new Thickness(0, 12, 0, 0);
            fixPort.Click += (_, _) => LaunchPortFix();
            root.Children.Add(fixPort);
        }

        root.Children.Add(VehicleKomUi.MakeText(
            "Nach Speichern: Funk → VoIP-Config nach Dropbox → Tablet: Config jetzt laden",
            11,
            muted: true,
            margin: new Thickness(0, 12, 0, 0)));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0)
        };
        scroll.Content = root;

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = VehicleKomUi.MakeButton("Abbrechen", isCancel: true);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var save = VehicleKomUi.MakeButton("Speichern", primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => SaveAndClose();
        footer.Children.Add(cancel);
        footer.Children.Add(save);

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        Grid.SetRow(footer, 1);
        outer.Children.Add(scroll);
        outer.Children.Add(footer);

        VehicleKomUi.PrepareWindow(this, outer);
        RefreshUi();
    }

    private VoipConnectivityMode SelectedMode =>
        _modeManaged.IsChecked == true
            ? VoipConnectivityMode.ManagedCloud
            : _modeCloud.IsChecked == true
                ? VoipConnectivityMode.Cloud
                : _modeFunnel.IsChecked == true
                    ? VoipConnectivityMode.TailscaleFunnel
                    : _modeMobile.IsChecked == true
                        ? VoipConnectivityMode.MobilePublic
                        : _modeDual.IsChecked == true
                            ? VoipConnectivityMode.Dual
                            : VoipConnectivityMode.DepotWlan;

    private VoipSettings BuildTurnSettingsFromUi() =>
        new()
        {
            TurnHost = _turnHost.Text.Trim(),
            TurnPort = int.TryParse(_turnPort.Text.Trim(), out var turnPort) ? turnPort : VoipConstants.DefaultTurnPort,
            TurnUsername = _turnUsername.Text.Trim(),
            TurnPassword = _turnPassword.Text
        };

    private void ApplyTestTurnPreset()
    {
        var preset = VoipTurnPresets.ApplyOpenRelayTest(new VoipSettings());
        _turnHost.Text = preset.TurnHost;
        _turnPort.Text = preset.TurnPort.ToString();
        _turnUsername.Text = preset.TurnUsername;
        _turnPassword.Text = preset.TurnPassword;
        RefreshUi();
        VehicleKomUi.SafeShowMessage(
            this,
            "Test-TURN eingetragen (openrelay.metered.ca).\n\n" +
            "Nur zum Ausprobieren – für den Dauerbetrieb eigenen TURN-Server (VPS) verwenden.\n\n" +
            "Speichern → Config nach Dropbox → erneut anrufen.",
            Title,
            MessageBoxImage.Information);
    }

    private void TestTurnReachability()
    {
        var turn = BuildTurnSettingsFromUi();
        if (string.IsNullOrWhiteSpace(turn.TurnHost))
        {
            VehicleKomUi.SafeShowMessage(this, "Bitte TURN-Host eintragen.", Title, MessageBoxImage.Warning);
            return;
        }

        if (VoipReachability.IsPrivateOrLocalHost(turn.TurnHost))
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "TURN-Host muss öffentlich erreichbar sein (keine 192.168.x.x).",
                Title,
                MessageBoxImage.Warning);
            return;
        }

        if (turn.IsTurnServerReachable())
        {
            VehicleKomUi.SafeShowMessage(
                this,
                $"TURN erreichbar:\n{turn.BuildTurnUrlForRemotePeers()}",
                Title,
                MessageBoxImage.Information);
            return;
        }

        VehicleKomUi.SafeShowMessage(
            this,
            $"TURN {turn.TurnHost}:{turn.TurnPort} nicht per TCP erreichbar.\n\n" +
            "Hinweis: Manche Netze blockieren Port 80 – Port 443 probieren.\n" +
            "Oder eigenen TURN-Server (VPS, Port 3478) eintragen.\n\n" +
            "Trotzdem speichern und testen – WebRTC kann trotzdem funktionieren.",
            Title,
            MessageBoxImage.Warning);
    }

    private void OpenFunnelEnablePage()
    {
        if (!VoipTailscaleFunnel.IsTailscaleCliAvailable())
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "Tailscale wurde nicht gefunden.\nBitte zuerst installieren und anmelden.",
                Title,
                MessageBoxImage.Warning);
            return;
        }

        var login = VoipTailscaleFunnel.TryGetLoginState();
        if (login?.NeedsLogin == true)
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "Bitte zuerst Tailscale anmelden (Icon neben der Uhr).",
                Title,
                MessageBoxImage.Information);
            return;
        }

        var enableUrl = VoipTailscaleFunnel.TryGetFunnelEnableUrl();
        if (string.IsNullOrWhiteSpace(enableUrl))
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "Funnel-Aktivierungs-Link konnte nicht ermittelt werden.\n\n" +
                "Manuell: https://login.tailscale.com/admin/acls\n" +
                "→ Bereich „Funnel“ → „Add Funnel to policy“",
                Title,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(enableUrl) { UseShellExecute = true });
            VehicleKomUi.SafeShowMessage(
                this,
                "Browser geöffnet – Funnel in Tailscale mit „Enable“ bestätigen.\n\n" +
                "Danach „Funnel-URL ermitteln“ und speichern.",
                Title,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            VehicleKomUi.SafeShowMessage(
                this,
                $"Browser konnte nicht geöffnet werden:\n{ex.Message}\n\nLink:\n{enableUrl}",
                Title,
                MessageBoxImage.Warning);
        }
    }

    private void DetectFunnelUrl()
    {
        if (!VoipTailscaleFunnel.IsTailscaleCliAvailable())
        {
            var installPath = VoipTailscaleFunnel.ResolveExecutablePath();
            VehicleKomUi.SafeShowMessage(
                this,
                "Tailscale wurde nicht gefunden.\n\n" +
                (string.IsNullOrWhiteSpace(installPath)
                    ? "Bitte installieren: https://tailscale.com/download\n" +
                      "Erwarteter Pfad: C:\\Program Files\\Tailscale\\tailscale.exe"
                    : $"Gefunden unter:\n{installPath}\n\nBitte Leitstelle neu starten.") +
                "\n\nDanach erneut „Funnel-URL ermitteln“.",
                Title,
                MessageBoxImage.Warning);
            return;
        }

        var login = VoipTailscaleFunnel.TryGetLoginState();
        if (login?.NeedsLogin == true)
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "Tailscale ist installiert, aber noch nicht angemeldet.\n\n" +
                "1. Tailscale-App im Infobereich (Uhr) öffnen\n" +
                "2. Mit Konto anmelden\n" +
                "3. Danach hier erneut „Funnel-URL ermitteln“",
                Title,
                MessageBoxImage.Information);
            return;
        }

        var status = VoipTailscaleFunnel.QueryStatus();
        var hostname = status.PublicHostname ?? VoipTailscaleFunnel.TryDetectPublicHostname();
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            _funnelHost.Text = hostname;
            _listenHost.Text = "0.0.0.0";
            if (_modeDepot.IsChecked == true)
            {
                _modeFunnel.IsChecked = true;
            }

            RefreshUi();
            if (!status.IsFunnelActive)
            {
                var enableUrl = VoipTailscaleFunnel.TryGetFunnelEnableUrl();
                VehicleKomUi.SafeShowMessage(
                    this,
                    $"Funnel-Host {hostname} eingetragen.\n\n" +
                    "Funnel ist noch nicht aktiv – einmalig „Funnel aktivieren (Browser)“ klicken.\n" +
                    "Danach speichern → Config nach Dropbox.",
                    Title,
                    MessageBoxImage.Information);
                if (!string.IsNullOrWhiteSpace(enableUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(enableUrl) { UseShellExecute = true });
                    }
                    catch
                    {
                        // optional
                    }
                }

                return;
            }

            VehicleKomUi.SafeShowMessage(
                this,
                $"Funnel-Host {hostname} eingetragen.\nFunnel ist aktiv.\n\nSpeichern → Config nach Dropbox.",
                Title,
                MessageBoxImage.Information);
            return;
        }

        VehicleKomUi.SafeShowMessage(
            this,
            "Keine *.ts.net-URL gefunden.\n\n" +
            "1. Tailscale anmelden\n" +
            "2. „Funnel aktivieren (Browser)“ klicken\n" +
            "3. Erneut „Funnel-URL ermitteln“",
            Title,
            MessageBoxImage.Warning);
    }

    private async Task DetectPublicIpAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ip = (await client.GetStringAsync("https://api.ipify.org").ConfigureAwait(true)).Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                VehicleKomUi.SafeShowMessage(this, "Öffentliche IP konnte nicht gelesen werden.", Title,
                    MessageBoxImage.Warning);
                return;
            }

            _publicHost.Text = ip;
            _listenHost.Text = "0.0.0.0";
            if (_modeDepot.IsChecked == true || _modeFunnel.IsChecked == true)
            {
                _modeDual.IsChecked = true;
            }

            RefreshUi();
            VehicleKomUi.SafeShowMessage(
                this,
                $"Öffentliche IP {ip} eingetragen.\n\nRouter: TCP 8787 → Leitstellen-PC\nSpeichern → Config nach Dropbox.",
                Title,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            VehicleKomUi.SafeShowMessage(
                this,
                $"Öffentliche IP nicht ermittelbar:\n{ex.Message}",
                Title,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyLanIp(string lanIp)
    {
        _depotLanHost.Text = lanIp;
        if (SelectedMode == VoipConnectivityMode.DepotWlan)
        {
            _publicHost.Text = lanIp;
            if (!VoipReachability.IsPrivateOrLocalHost(_turnHost.Text))
            {
                _turnHost.Text = lanIp;
            }
        }

        _listenHost.Text = "0.0.0.0";
        if (_modeMobile.IsChecked == true)
        {
            _modeDepot.IsChecked = true;
        }

        RefreshUi();
        VehicleKomUi.SafeShowMessage(
            this,
            $"WLAN-IP {lanIp} eingetragen.",
            Title,
            MessageBoxImage.Information);
    }

    private void RefreshUi()
    {
        var mode = SelectedMode;
        _managedPanel.Visibility = mode == VoipConnectivityMode.ManagedCloud
            ? Visibility.Visible
            : Visibility.Collapsed;
        _cloudPanel.Visibility = mode == VoipConnectivityMode.Cloud
            ? Visibility.Visible
            : Visibility.Collapsed;
        _funnelPanel.Visibility = mode == VoipConnectivityMode.TailscaleFunnel
            ? Visibility.Visible
            : Visibility.Collapsed;
        _mobilePanel.Visibility = mode is VoipConnectivityMode.MobilePublic or VoipConnectivityMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        _depotPanel.Visibility = mode is VoipConnectivityMode.DepotWlan
                or VoipConnectivityMode.TailscaleFunnel
                or VoipConnectivityMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        var hideLocal = mode is VoipConnectivityMode.Cloud or VoipConnectivityMode.ManagedCloud;
        _listenHost.Visibility = hideLocal ? Visibility.Collapsed : Visibility.Visible;
        _port.Visibility = hideLocal ? Visibility.Collapsed : Visibility.Visible;
        _hintText.Text = BuildHint();
    }

    private string BuildHint()
    {
        return SelectedMode switch
        {
            VoipConnectivityMode.ManagedCloud =>
                VoipManagedCloud.IsReady(Settings) || !string.IsNullOrWhiteSpace(_managedTurnPassword.Text)
                    ? $"✓ Smart ÖPNV Funk über {VoipManagedCloud.DefaultHost} – nach Speichern Config nach Dropbox."
                    : $"✓ Smart ÖPNV Funk – einmalig TURN-Passwort vom Server-Setup eintragen ({VoipManagedCloud.DefaultHost}).",
            VoipConnectivityMode.Cloud =>
                $"✓ Cloud: Tablets und Leitstelle → {FormatHost(_cloudHost.Text)} " +
                $"({(_cloudUseTls.IsChecked == true ? "wss" : "ws")}). TURN auf demselben Server eintragen.",
            VoipConnectivityMode.DepotWlan =>
                "✓ Nur Betriebshof/WLAN – Tablet muss im gleichen Netz sein.",
            VoipConnectivityMode.TailscaleFunnel =>
                $"✓ LTE: {FormatHost(_funnelHost.Text)} (wss), WLAN-Fallback: {FormatHost(_depotLanHost.Text)}. " +
                "TURN-VPS für Audio unterwegs eintragen.",
            VoipConnectivityMode.MobilePublic =>
                "✓ Nur Mobilfunk – Router muss Port 8787/TCP an diesen PC leiten.",
            VoipConnectivityMode.Dual =>
                $"✓ Automatisch: Tablet versucht {FormatHost(_publicHost.Text)} (Mobilfunk), " +
                $"sonst {FormatHost(_depotLanHost.Text)} (WLAN).",
            _ => string.Empty
        };
    }

    private static string FormatHost(string host) =>
        string.IsNullOrWhiteSpace(host) ? "—" : host.Trim();

    private void SaveAndClose()
    {
        var mode = SelectedMode;

        if (mode == VoipConnectivityMode.ManagedCloud)
        {
            var turnPass = _managedTurnPassword.Text.Trim();
            if (string.IsNullOrWhiteSpace(turnPass))
            {
                turnPass = Settings.TurnPassword;
            }

            if (string.IsNullOrWhiteSpace(turnPass))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Bitte einmalig das TURN-Passwort eintragen.\n\n" +
                    "Das steht am Ende der Server-Installation (bash /tmp/voip-cloud-install.sh).\n" +
                    "Nicht das IONOS-Root-Passwort verwenden.",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }

            Settings = new VoipSettings
            {
                Enabled = _enabled.IsChecked == true,
                DispatchDisplayName = Settings.DispatchDisplayName,
                WindowsPortSetupCompletedUtc = Settings.WindowsPortSetupCompletedUtc,
                DepotLanHost = Settings.DepotLanHost
            };
            VoipManagedCloud.ApplyTo(Settings, turnPass);
            _store.Save(Settings);
            DialogResult = true;
            Close();
            VehicleKomUi.SafeShowMessage(
                Owner ?? this,
                "VoIP-Einstellungen gespeichert.\n\n" +
                "VoIP-Config wird nach Dropbox geschrieben (falls verbunden).\n" +
                "Tablet: Config jetzt laden oder App neu starten.",
                Title,
                MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            VehicleKomUi.SafeShowMessage(this, "Ungültiger Port.", Title, MessageBoxImage.Warning);
            return;
        }

        var depotLan = _depotLanHost.Text.Trim();
        var publicHost = _publicHost.Text.Trim();
        var funnelHost = VoipTailscaleFunnel.NormalizeHostname(_funnelHost.Text);
        var cloudHost = VoipTailscaleFunnel.NormalizeHostname(_cloudHost.Text);
        var cloudUseTls = _cloudUseTls.IsChecked == true;
        var cloudPort = int.TryParse(_cloudPort.Text.Trim(), out var parsedCloudPort) ? parsedCloudPort : 0;

        if (mode == VoipConnectivityMode.Cloud)
        {
            if (string.IsNullOrWhiteSpace(cloudHost))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Bitte Cloud-Server (Hostname oder IP) eintragen.",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }

            if (cloudPort is < 0 or > 65535)
            {
                VehicleKomUi.SafeShowMessage(this, "Ungültiger Cloud-Port.", Title, MessageBoxImage.Warning);
                return;
            }

            publicHost = cloudHost;
            depotLan = string.Empty;
        }
        else if (mode == VoipConnectivityMode.DepotWlan)
        {
            if (string.IsNullOrWhiteSpace(depotLan))
            {
                VehicleKomUi.SafeShowMessage(this, "Bitte WLAN-IP eintragen oder „WLAN-IP übernehmen“.", Title,
                    MessageBoxImage.Warning);
                return;
            }

            publicHost = depotLan;
        }
        else if (mode == VoipConnectivityMode.TailscaleFunnel)
        {
            if (string.IsNullOrWhiteSpace(depotLan))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Bitte WLAN-IP für den Betriebshof-Fallback eintragen.",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(funnelHost))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Bitte Funnel-Host (*.ts.net) eintragen oder „Funnel-URL ermitteln“.",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }

            if (!VoipTailscaleFunnel.IsTsNetHost(funnelHost))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Funnel-Host muss auf .ts.net enden (z. B. leitstelle.tailnet.ts.net).",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }

            publicHost = funnelHost;
        }
        else if (mode == VoipConnectivityMode.MobilePublic)
        {
            if (string.IsNullOrWhiteSpace(publicHost))
            {
                VehicleKomUi.SafeShowMessage(this, "Bitte öffentliche IP oder DynDNS eintragen.", Title,
                    MessageBoxImage.Warning);
                return;
            }

            if (VoipReachability.IsPrivateOrLocalHost(publicHost))
            {
                VehicleKomUi.SafeShowMessage(this,
                    "Mobilfunk-Modus braucht eine öffentliche IP oder DynDNS (keine 192.168.x.x).",
                    Title, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(publicHost) || string.IsNullOrWhiteSpace(depotLan))
            {
                VehicleKomUi.SafeShowMessage(
                    this,
                    "Modus „Automatisch“ braucht WLAN-IP und öffentliche IP/DynDNS.",
                    Title,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (mode != VoipConnectivityMode.Cloud &&
            mode != VoipConnectivityMode.ManagedCloud &&
            VoipReachability.IsPrivateOrLocalHost(depotLan) &&
            !VoipReachability.IsLocalAdapterAddress(depotLan))
        {
            var recommended = VoipReachability.TryGetRecommendedLanHost() ?? "—";
            VehicleKomUi.SafeShowMessage(
                this,
                $"Die WLAN-IP {depotLan} gehört nicht zu diesem PC.\n\nEmpfohlen: {recommended}",
                Title,
                MessageBoxImage.Warning);
            return;
        }

        var turnFromUi = BuildTurnSettingsFromUi();
        if (mode == VoipConnectivityMode.Cloud &&
            string.IsNullOrWhiteSpace(turnFromUi.TurnHost) &&
            !string.IsNullOrWhiteSpace(cloudHost))
        {
            turnFromUi.TurnHost = cloudHost;
            if (turnFromUi.TurnPort <= 0)
            {
                turnFromUi.TurnPort = VoipConstants.DefaultTurnPort;
            }
        }

        if (mode == VoipConnectivityMode.TailscaleFunnel && !turnFromUi.IsTurnServerReachable())
        {
            var answer = MessageBox.Show(
                this,
                "TURN-Server ist per TCP-Test nicht erreichbar.\n\n" +
                "Trotzdem speichern und Funk testen?\n" +
                "(WebRTC kann auch ohne erfolgreichen TCP-Test funktionieren.)",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Settings = new VoipSettings
        {
            Enabled = _enabled.IsChecked == true,
            ConnectivityMode = mode,
            PublicSignalingHost = publicHost,
            DepotLanHost = depotLan,
            TailscaleFunnelHost = funnelHost,
            CloudSignalingHost = cloudHost,
            CloudSignalingUseTls = cloudUseTls,
            CloudSignalingPort = cloudPort,
            AutoManageTailscaleFunnel = _autoManageFunnel.IsChecked == true,
            ListenHost = _listenHost.Text.Trim(),
            SignalingPort = port,
            TurnHost = turnFromUi.TurnHost,
            TurnPort = turnFromUi.TurnPort,
            TurnUsername = turnFromUi.TurnUsername,
            TurnPassword = turnFromUi.TurnPassword,
            DispatchDisplayName = Settings.DispatchDisplayName,
            WindowsPortSetupCompletedUtc = Settings.WindowsPortSetupCompletedUtc
        };
        _store.Save(Settings);
        DialogResult = true;
        Close();
        VehicleKomUi.SafeShowMessage(
            Owner ?? this,
            "VoIP-Einstellungen gespeichert.\n\n" +
            "VoIP-Config wird nach Dropbox geschrieben (falls verbunden).\n" +
            "Tablet: Config jetzt laden oder App neu starten.",
            Title,
            MessageBoxImage.Information);
    }

    private void LaunchPortFix()
    {
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            VehicleKomUi.SafeShowMessage(this, "Zuerst gültigen Port eintragen.", Title, MessageBoxImage.Warning);
            return;
        }

        var temp = new VoipSettings { SignalingPort = port };
        if (VoipWindowsPortSetup.TryLaunchElevatedRegistration(temp, out _, automatic: true))
        {
            VehicleKomUi.SafeShowMessage(
                this,
                "Windows-Administrator mit „Ja“ bestätigen – VoIP startet danach automatisch neu.",
                Title,
                MessageBoxImage.Information);
        }
    }

    private static TextBox MakeInput(string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 0) };
        VehicleKomUi.StyleTextBox(box);
        return box;
    }
}
