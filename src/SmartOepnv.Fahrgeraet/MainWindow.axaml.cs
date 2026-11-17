using Avalonia.Controls;

namespace SmartOepnv.Fahrgeraet;

public partial class MainWindow : Window
{
    private readonly FahrSession _session = new();
    private readonly FahrgeraetRouteSync _sync = new();

    public MainWindow()
    {
        InitializeComponent();
            _session.LoadDevice();
            GeraetEinstellungen.ApplyTo(_session);
            LinuxUsbBus.Start(_session);
            FmsLive.Start();
        _session.Status = text => StatusText.Text = text;
        Opened += (_, _) =>
        {
            try
            {
                _session.Package = _sync.TryLoadLocal();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }

            BuildNav();
            ShowItcs();
            BluetoothZwang.Start();
            var show = _session.Status;
            _session.Status = text =>
            {
                FahrProtokoll.Add(text);
                show?.Invoke(text);
            };
            _ = _session.Funk.ConnectAsync(_session.DevicePhone);
            FahrUpdate.Start();
            _ = WatchIbisEthernetAsync();
            IbisIpDienst.Ensure(_session);
        };
    }

    public void LeaveToLogin()
    {
        BuildNav();
        ShowLogin();
    }

    private async Task WatchIbisEthernetAsync()
    {
        while (true)
        {
            var show = false;
            try
            {
                show = LinuxIbisEthernet.Present();
            }
            catch
            {
                show = false;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IbisChip.Background = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(show ? "#2E7D32" : "#C62828"));
                IbisChipText.Text = "IBIS-IP";
            });
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private void BuildNav()
    {
        Nav.Children.Clear();
        Add("Anmeldung", ShowLogin);
        Add("Abmelden", () =>
        {
            GpsSpur.Event(_session, "logout", _session.Employee?.PersonnelNumber ?? "");
            _session.Employee = null;
            _session.Trip.Stop();
            StandortSend.Stop();
            ShowLogin();
        });
        Add("ITCS", ShowItcs);
        Add("Fahrt", ShowItcs);
        Add("Bearbeiten", () => PageHost.Content = FahrFahrtSeiten.Bearbeiten(_session, _sync));
        Add("VRR", () => PageHost.Content = FahrFahrtSeiten.Vrr(_session));
        Add("Ansagen", () => PageHost.Content = FahrbetriebPages.Ansagen(_session));
        Add("Anzeigen", () => PageHost.Content = FahrbetriebPages.Ziel(_session));
        Add("Entwerter", () => PageHost.Content = FahrgeraetMehr.Entwerter(_session));
        Add("VoIP", () => PageHost.Content = FahrbetriebPages.Voip(_session));
        Add("Zeitwirtschaft", () => PageHost.Content = FahrbetriebPages.Zeitwirtschaft(_session));
        Add("Verkauf", () => PageHost.Content = FahrbetriebPages.Verkauf(_session));
        Add("Mängelkarte", () => PageHost.Content = FahrgeraetMehr.Maengelkarte(_session));
        Add("Postfach", () => PageHost.Content = FahrbetriebPages.Postfach(_session));
        Add("Einstellungen", () => PageHost.Content = FahrgeraetMehr.Einstellungen(
            _session,
            () => PageHost.Content = FahrgeraetMehr.Fahrzeugdiagnose(_session)));
        Add("Dropbox", () => PageHost.Content = FahrLuecken.DropboxSeite());
        Add("Datentransfer", () => PageHost.Content = FahrLuecken.Transfer(_session));
        Add("Aufnahme", () => PageHost.Content = FahrLuecken.Ansagen(_session));
        Add("Linencode", () => PageHost.Content = FahrLuecken.LinienCodes(_session));
        Add("Manuelle Anzeige", () => PageHost.Content = FahrLuecken.ManuelleAnzeige(_session));
        Add("Zielliste", () => PageHost.Content = FahrLuecken.Ziele(_session));
        Add("Abfahrten", () => PageHost.Content = FahrLuecken.Abfahrten(_session));
        Add("Mitarbeiter", () => PageHost.Content = FahrLuecken.Mitarbeiter(_session));
        Add("Geräte", () => PageHost.Content = FahrLuecken.Geraete(_session));
        Add("Nachricht", () => PageHost.Content = FahrLuecken.Nachricht(_session));
        Add("Pause", ShowPause);
        Add("Anleitung", () => PageHost.Content = FahrLuecken.Anleitung());
        Add("Protokoll", () => PageHost.Content = FahrLuecken.Protokoll());
        Add("Impressum", () => PageHost.Content = FahrLuecken.Impressum(_session));
        Add("Update", () => PageHost.Content = FahrLuecken.Update());
        Add("Bluetooth", () => PageHost.Content = FahrLuecken.Bluetooth());
    }

    private void ShowPause()
    {
        ItcsPause.Begin(_session);
        ShowItcs();
    }

    private void Add(string caption, Action show)
    {
        var button = new Button
        {
            Content = caption,
            Classes = { "touch" }
        };
        button.Click += (_, _) =>
        {
            TastenTon.Play();
            show();
        };
        Nav.Children.Add(button);
    }

    private void ShowLogin() =>
        PageHost.Content = FahrbetriebPages.Login(_session, () =>
        {
            StatusText.Text = _session.Employee is null ? "" : _session.Employee.Name;
            StandortSend.Start(_session);
            ShowItcs();
        });

    private void ShowItcs()
    {
        var who = _session.Employee?.Name ?? "nicht angemeldet";
        var routes = _session.Package?.RouteNames.Count ?? 0;
        StatusText.Text = $"{who} · {routes} Routen" +
                          (string.IsNullOrWhiteSpace(_session.RouteName) ? "" : $" · {_session.RouteName}");
        PageHost.Content = FahrFahrtSeiten.Fahrt(_session, () =>
            PageHost.Content = FahrbetriebPages.Itcs(_session, _sync, ShowItcs));
    }
}
