using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

public partial class MainWindow : Window
{
    private readonly FahrSession _session = new();
    private readonly FahrgeraetRouteSync _sync = new();
    private int _stopTapIndex = -1;
    private int _stopTapCount;
    private int _stopTapGeneration;
    private DateTime _stopTapAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        var tabular = new FontFeatureCollection { FontFeature.Parse("tnum") };
        ClockText.FontFeatures = tabular;
        DateText.FontFeatures = tabular;
        DelayText.FontFeatures = tabular;
        Bildschirmtastatur.Attach(this);
        FingerZug.An(StopsScroll, () => _scrollZiel = null);
        LadeAnzeige.Verbinden(ImportRingHost, ImportPercent);
        StopsScroll.LayoutUpdated += (_, _) =>
        {
            if (_stopMass)
            {
                return;
            }

            if (_scrollZiel is not null && StopsList.Children.Count > 0)
            {
                Nachscrollen();
            }

            if (StopsList.Children.Count == 0 || Math.Abs(Zeilenhoehe() - _zeile) < 2)
            {
                return;
            }

            _stopMass = true;
            try
            {
                RefreshStops();
            }
            finally
            {
                _stopMass = false;
            }
        };
        ForceKiosk();
            _session.LoadDevice();
            GeraetEinstellungen.ApplyTo(_session);
            TastenTon.Bereit();
            LinuxUsbBus.Start(_session);
            FmsLive.Start();
        Opened += (_, _) =>
        {
            ForceKiosk();
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            var settleTries = 0;
            settle.Tick += (_, _) =>
            {
                ForceKiosk();
                if (++settleTries >= 6)
                {
                    settle.Stop();
                }
            };
            settle.Start();
            try
            {
                _session.Package = _sync.TryLoadLocal();
            }
            catch
            {
            }

            if (_sync.IsDropboxConnected)
            {
                _ = RoutenAusDropboxAsync();
                var wacht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                wacht.Tick += (_, _) => _ = RoutenAusDropboxAsync();
                wacht.Start();
            }

            _session.Trip.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                PaintPasInfo();
                RefreshRouteLine();
                RefreshStops();
            });
            Wire();
            ShowShell();
            if (ItcsPause.Open && Content is Grid pauseHost)
            {
                ItcsPause.Present(_session, pauseHost);
            }
            BluetoothZwang.Start();
            var show = _session.Status;
            _session.Status = text =>
            {
                FahrProtokoll.Add(text);
                show?.Invoke(text);
            };
            _session.Funk.ConnectAsync(_session.DevicePhone);
            FahrUpdate.Start();
            _ = WatchIbisEthernetAsync();
            IbisIpDienst.Ensure(_session);
            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (_, _) => RefreshClock();
            clock.Start();
            RefreshClock();
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            blink.Tick += (_, _) =>
            {
                if (!_aufAkku)
                {
                    return;
                }

                _akkuHell = !_akkuHell;
                BatteryText.Opacity = _akkuHell ? 1 : 0.25;
            };
            blink.Start();
        };
    }

    private void ForceKiosk()
    {
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = false;
        Topmost = true;
        var screen = Screens?.Primary;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
        }

        WindowState = WindowState.FullScreen;
    }

    private int _routenPruefung;

    private async Task RoutenAusDropboxAsync()
    {
        if (Interlocked.Exchange(ref _routenPruefung, 1) == 1)
        {
            return;
        }

        try
        {
            var result = await _sync.SyncAsync(CancellationToken.None, new Progress<RoutenFortschritt>(stand =>
                LadeAnzeige.Setzen(stand.Prozent, stand.Meldung)));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Success && !result.Unchanged && result.Package is not null)
                {
                    _session.Package = result.Package;
                    RefreshRouteLine();
                    RefreshStops();
                    FahrProtokoll.Add(result.Message);
                }
                else if (!result.Success)
                {
                    FahrProtokoll.Add(result.Message);
                }
            });
        }
        catch (OperationCanceledException)
        {
            FahrProtokoll.Add("Update abgebrochen.");
        }
        catch (Exception ex)
        {
            FahrProtokoll.Add(ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(LadeAnzeige.KopfAus);
            Interlocked.Exchange(ref _routenPruefung, 0);
        }
    }

    public void LeaveToLogin()
    {
        ShowPage(TabletDialoge.Anmeldung(_session, () =>
        {
            StandortSend.Start(_session);
            ShowShell();
        }));
    }

    private void Wire()
    {
        Tap(PasInfoButton, () =>
        {
            if (_session.Trip.Running)
            {
                _session.Trip.Stop();
            }
            else
            {
                try
                {
                    _session.Trip.Start(_session);
                    if (_session.Trip.Running)
                    {
                        _rahmenErzwingen = true;
                        _session.Trip.BeimEinschalten(_session);
                    }
                }
                catch
                {
                }
            }

            PaintPasInfo();
        });
        Tap(SonderButton, () => ShowPage(FahrbetriebPages.Ansagen(_session)));
        Tap(RouteButton, ShowRoutes);
        Tap(LineButton, () => ShowPage(TabletDialoge.LinieKurs(_session, ShowShell)));
        Tap(ZielButton, () => ShowPage(TabletDialoge.Anzeigenliste(_session, ShowShell)));
        Tap(MailButton, () => ShowPage(FahrLuecken.Nachricht(_session)));
        Tap(ZblButton, () => ShowPage(FahrbetriebPages.Voip(_session)));
        Tap(UnfallButton, () => ShowPage(FahrLuecken.Nachricht(_session)));
        Tap(RazziaButton, () => ShowPage(FahrgeraetMehr.Entwerter(_session)));
        Tap(PauseButton, ShowPause);
        Tap(ItcsButton, ShowShell);
        Tap(VerkaufButton, () =>
        {
            Mark(VerkaufFrame);
            ShowPage(FahrbetriebPages.Verkauf(_session));
        });
        MenuButton.Click += (_, _) => ShowMenu();
        Tap(AdminButton, () => ShowPage(FahrTabletMenue.Administration(_session, ShowMenu, ShowPage)));
        Tap(DisplaysButton, () => ShowPage(FahrTabletMenue.Displays(_session, ShowMenu, ShowPage)));
        Tap(SettingsButton, () => ShowPage(FahrTabletMenue.Settings(_session, ShowMenu, ShowPage)));
        Tap(ZeitButton, () => ShowPage(FahrbetriebPages.Zeitwirtschaft(_session)));
        Tap(MangelButton, () => ShowPage(FahrgeraetMehr.Maengelkarte(_session)));
        Tap(AbmeldenButton, LogOut);
        Tap(AnsagenButton, () => ShowPage(FahrbetriebPages.Ansagen(_session)));
        Tap(MenuPauseButton, ShowPause);
        Tap(AnleitungButton, () => ShowPage(FahrLuecken.Anleitung()));
        Tap(ImpressumButton, () => ShowPage(FahrLuecken.Impressum(_session)));
        Tap(BeendenButton, () => ShowPage(TabletDialoge.AppBeenden(ShowMenu, ProgrammSchliessen)));
        Tap(NavButton, ShowNavigation);
        Tap(PostfachButton, () => ShowPage(FahrbetriebPages.Postfach(_session)));
    }

    private void Tap(Button button, Action action)
    {
        button.Click += (_, _) =>
        {
            TastenTon.Play();
            action();
        };
    }

    private void ShowNavigation()
    {
        NavHost.Content = FahrNavigation.Create(_session, CloseNavigation);
        ItcsLayer.ColumnDefinitions = new ColumnDefinitions("Auto,1.1*,1.6*");
        NavField.IsVisible = true;
        ItcsLayer.IsVisible = true;
        PageLayer.IsVisible = false;
        MenuLayer.IsVisible = false;
        Mark(ItcsFrame);
    }

    private void CloseNavigation()
    {
        NavField.IsVisible = false;
        NavHost.Content = null;
        ItcsLayer.ColumnDefinitions = new ColumnDefinitions("*,0,2*");
    }

    private void ShowShell()
    {
        CloseNavigation();
        PageLayer.IsVisible = false;
        MenuLayer.IsVisible = false;
        ItcsLayer.IsVisible = true;
        Mark(ItcsFrame);
        RefreshTrip();
    }

    private DispatcherTimer? _lockBarTimer;

    private void ProgrammSchliessen()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Environment.Exit(0);
    }

    private void ShowMenu()
    {
        if (_session.Trip.Running)
        {
            IvuTon.Error();
            if (!LockBar.IsVisible)
            {
                LockBar.IsVisible = true;
                _lockBarTimer?.Stop();
                _lockBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _lockBarTimer.Tick += (_, _) =>
                {
                    _lockBarTimer?.Stop();
                    LockBar.IsVisible = false;
                };
                _lockBarTimer.Start();
            }

            return;
        }

        TastenTon.Play();
        PageLayer.IsVisible = false;
        ItcsLayer.IsVisible = false;
        MenuLayer.IsVisible = true;
        Mark(MenuFrame);
    }

    private void ShowPage(Control page)
    {
        if (page is ScrollViewer scroll)
        {
            if (!scroll.Classes.Contains("menuScroll"))
            {
                scroll.Classes.Add("menuScroll");
            }

            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalAlignment = HorizontalAlignment.Stretch;
            scroll.VerticalAlignment = VerticalAlignment.Stretch;
            FingerZug.An(scroll);
            FingerZug.In(page);
        }

        PageHost.Content = page;
        ItcsLayer.IsVisible = false;
        MenuLayer.IsVisible = false;
        PageLayer.IsVisible = true;
    }

    private void LogOut()
    {
        ZeitPause.EndShift(_session);
        if (Content is Grid root)
        {
            ItcsPause.Dismiss(root);
        }

        GpsSpur.Event(_session, "logout", _session.Employee?.PersonnelNumber ?? "");
        _session.Employee = null;
        _session.Trip.Stop();
        StandortSend.Stop();
        LeaveToLogin();
    }

    private void ShowPause()
    {
        if (Content is Grid root)
        {
            ItcsPause.Present(_session, root);
        }
    }

    private void Mark(Border active)
    {
        foreach (var frame in new[] { ItcsFrame, VerkaufFrame, MenuFrame })
        {
            frame.BorderBrush = Brushes.White;
        }

        active.BorderBrush = new SolidColorBrush(Color.Parse("#FFFF00"));
        ItcsArrow.IsVisible = active == ItcsFrame;
    }

    private void PaintPasInfo()
    {
        var running = _session.Trip.Running;
        PasInfoButton.Content = running ? "Pas.-Info off" : "Pas.-Info on";
        PasInfoButton.Background = new SolidColorBrush(Color.Parse(running ? "#FF0000" : "#4CAF50"));
    }

    private void ShowRoutes()
    {
        var paket = _session.Package;
        var freigegeben = paket?.RouteNames.Where(paket.IsRouteInItcsRouteList).ToList() ?? [];
        var namen = freigegeben.Where(name => RoutenListe.Heute(_session, name)).ToList();
        var suche = FahrbetriebPages.Field("Suchen…");
        var modus = "linie";
        var sortierung = new Button
        {
            Content = "Linie ✓",
            Classes = { "pill" },
            MinHeight = 44,
            Background = new SolidColorBrush(Color.Parse("#2196F3"))
        };
        var hinweis = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };
        var liste = new StackPanel { Spacing = 6 };
        var haltezahl = new Dictionary<string, int>(StringComparer.Ordinal);

        void Malen()
        {
            liste.Children.Clear();
            var text = suche.Text?.Trim() ?? "";
            IEnumerable<string> treffer = namen;
            if (text.Length > 0)
            {
                treffer = namen.Where(name =>
                    name.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || RoutenListe.Beschriftung(name).Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            var geordnet = RoutenListe.Sortieren(treffer, modus, name =>
            {
                if (haltezahl.TryGetValue(name, out var zahl))
                {
                    return zahl;
                }

                zahl = paket?.GetStops(name).Count(stop => !stop.IsWaypoint) ?? 0;
                haltezahl[name] = zahl;
                return zahl;
            }).ToList();
            var zeigen = text.Length == 0 && geordnet.Count > 250
                ? geordnet.Take(250).ToList()
                : geordnet;
            hinweis.Text = text.Length == 0 && namen.Count > 250
                ? $"{zeigen.Count} von {namen.Count} Routen – bitte suchen…"
                : $"{geordnet.Count} von {namen.Count} Routen";
            sortierung.Content = modus switch
            {
                "name" => "Name ✓",
                "haltestellen" => "Haltest. ✓",
                _ => "Linie ✓"
            };
            foreach (var route in zeigen)
            {
                var gewaehlt = route;
                var knopf = new Button
                {
                    Content = RoutenListe.Beschriftung(gewaehlt),
                    Background = new SolidColorBrush(Color.Parse("#00008B")),
                    MinHeight = 44
                };
                knopf.Classes.Add("pill");
                knopf.Click += (_, _) =>
                {
                    TastenTon.Play();
                    _session.RouteName = gewaehlt;
                    _session.Trip.Reset();
                    ShowShell();
                };
                liste.Children.Add(knopf);
            }
        }

        suche.TextChanged += (_, _) => Malen();
        sortierung.Click += (_, _) =>
        {
            TastenTon.Play();
            modus = modus switch
            {
                "linie" => "name",
                "name" => "haltestellen",
                _ => "linie"
            };
            Malen();
        };
        Malen();
        var seite = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        seite.Children.Add(new TextBlock
        {
            Text = "Route",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeight.Bold
        });
        seite.Children.Add(suche);
        seite.Children.Add(sortierung);
        seite.Children.Add(hinweis);
        if (namen.Count == 0)
        {
            seite.Children.Add(new TextBlock
            {
                Text = paket is null
                    ? "Kein Routenpaket."
                    : freigegeben.Count == 0
                        ? "Keine Routen in der ITCS-Routenliste."
                        : "Heute kein Betriebsfenster für freigegebene Routen.",
                Foreground = Brushes.White,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            });
        }

        seite.Children.Add(liste);
        ShowPage(seite);
    }

    private void RefreshClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");
        DateText.Text = now.ToString("dd.MM.yyyy");
        var hasFix = _session.Trip.HasFix;
        GpsDot.Fill = new SolidColorBrush(Color.Parse(hasFix ? "#4CAF50" : "#FF0000"));
        GpsText.Text = hasFix ? "GPS aktiv" : "GPS inaktiv";
        OffRouteIcon.IsVisible = !hasFix;
        if (hasFix && _session.Trip.AccuracyMeters is int meters)
        {
            GpsAccuracy.Text = $"±{meters}m";
            GpsAccuracy.IsVisible = true;
            return;
        }

        if (hasFix)
        {
            GpsAccuracy.Text = "Suche...";
            GpsAccuracy.IsVisible = true;
        }
        else
        {
            GpsAccuracy.IsVisible = false;
        }
    }

    private void RefreshTrip()
    {
        PaintPasInfo();
        RefreshRouteLine();
        RefreshStops();
    }

    private void RefreshRouteLine()
    {
        if (string.IsNullOrWhiteSpace(_session.RouteName))
        {
            LineText.Text = "";
            RouteNameText.Text = "";
            DestText.Text = "";
            return;
        }

        var parsed = RouteDisplayHelper.Parse(_session.RouteName);
        LineText.Text = LinieMitFahrt(parsed);
        RouteNameText.Text = NachTrenner(parsed.Name);
        var ziel = NachTrenner(_session.Trip.ZielFuerKopf(_session));
        DestText.Text = string.IsNullOrWhiteSpace(ziel) ? "Ziel: Kein Ziel" : "Ziel: " + ziel;
    }

    private static string LinieMitFahrt(RouteDefinition parsed)
    {
        var linie = (parsed.LineCourse ?? "").Trim();
        var fahrt = RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber);
        if (fahrt.Length == 1)
        {
            fahrt = "0" + fahrt;
        }

        if (linie.Length == 0)
        {
            return fahrt;
        }

        return fahrt.Length == 0 ? linie : linie + ", " + fahrt;
    }

    /// <summary>
    /// Wie auf dem Tablet: alles vor „/“ oder „&gt;“ bleibt unsichtbar.
    /// „S28 Wuppertal Hbf / Mettmann Stadtwald“ wird „Mettmann Stadtwald“.
    /// </summary>
    private static string NachTrenner(string? text)
    {
        var wert = (text ?? "").Trim();
        var slash = wert.IndexOf('/');
        if (slash >= 0 && slash < wert.Length - 1)
        {
            var nachSlash = wert[(slash + 1)..].Trim();
            if (nachSlash.Length > 0)
            {
                wert = nachSlash;
            }
        }

        var pfeil = wert.IndexOf('>');
        if (pfeil >= 0 && pfeil < wert.Length - 1)
        {
            var nachPfeil = wert[(pfeil + 1)..].Trim();
            if (nachPfeil.Length > 0)
            {
                wert = nachPfeil;
            }
        }

        return wert;
    }

    private bool _aufAkku;
    private bool _selbsthaltung;
    private bool _akkuHell = true;
    private static readonly TimeSpan SelbsthaltungNach = TimeSpan.FromMinutes(10);

    private void ApplyAkku(int? percent, LinuxHeaderStatus.AkkuLage lage)
    {
        _aufAkku = lage.Akku;
        var selbst = lage is { Akku: true, SeitUtc: DateTime seit } && DateTime.UtcNow - seit >= SelbsthaltungNach;
        if (!_aufAkku)
        {
            _akkuHell = true;
            BatteryText.Opacity = 1;
        }

        BatteryText.Text = percent is int wert
            ? _aufAkku ? $"{wert}% !" : $"{wert}%"
            : _aufAkku ? "!" : "";
        BatteryText.IsVisible = percent.HasValue || _aufAkku;
        if (selbst == _selbsthaltung)
        {
            return;
        }

        _selbsthaltung = selbst;
        ApplySelbsthaltung();
    }

    private void ApplySelbsthaltung()
    {
        var an = _selbsthaltung;
        StopsHeading.IsVisible = !an;
        StopsScroll.IsVisible = !an;
        SelbsthaltungText.IsVisible = an;
        if (an)
        {
            StopOrangeFrame.IsVisible = false;
        }

        var routeMitPas = !string.IsNullOrWhiteSpace(_session.RouteName) && _session.Trip.Running;
        DelayBox.IsVisible = !an || routeMitPas;
        foreach (var knopf in AlleKnopfe())
        {
            knopf.IsEnabled = !an;
        }

        if (an)
        {
            CloseNavigation();
            if (PageLayer.IsVisible || MenuLayer.IsVisible)
            {
                ShowShell();
            }

            return;
        }

        RefreshStops();
    }

    private IEnumerable<Button> AlleKnopfe()
    {
        return
        [
            PasInfoButton, NavButton, SonderButton, RouteButton, LineButton, ZielButton, PostfachButton,
            MailButton, ZblButton, UnfallButton, RazziaButton, PauseButton, ItcsButton, VerkaufButton, MenuButton,
            AdminButton, DisplaysButton, SettingsButton, ZeitButton, MangelButton, AbmeldenButton, AnsagenButton,
            MenuPauseButton, AnleitungButton, ImpressumButton, BeendenButton
        ];
    }

    private bool _stopMass;
    private double _zeile;
    private double? _scrollZiel;
    private int? _gerahmt;
    private bool _rahmenErzwingen;
    private DateTime _stopLockUntil = DateTime.MinValue;

    private void RefreshStops()
    {
        if (_selbsthaltung)
        {
            StopOrangeFrame.IsVisible = false;
            return;
        }

        StopsList.Children.Clear();
        if (_session.Package == null || string.IsNullOrWhiteSpace(_session.RouteName))
        {
            StopOrangeFrame.IsVisible = false;
            return;
        }

        var hoehe = Zeilenhoehe();
        _zeile = hoehe;
        var luecke = Math.Max(4, hoehe * 0.05);
        var fokus = _session.Trip.RahmenIndex ?? _session.Trip.NextIndex;
        var kopf = _session.Trip.Running;
        var vorher = StopsScroll.Offset.Y;
        var rahmenVorher = _gerahmt;
        var erzwingen = _rahmenErzwingen;
        _rahmenErzwingen = false;
        if (kopf)
        {
            StopsList.Children.Add(new Border { Height = hoehe, IsHitTestVisible = false });
        }

        var stops = _session.Package.GetStops(_session.RouteName).ToList();
        var anzahl = stops.Count(stop => !stop.IsWaypoint);
        var sichtbar = 0;
        int? rahmen = null;
        for (var i = 0; i < stops.Count; i++)
        {
            if (stops[i].IsWaypoint)
            {
                continue;
            }

            var zeile = StopRow(stops[i], i, luecke, hoehe, sichtbar == 0, sichtbar == anzahl - 1, anzahl);
            zeile.Height = hoehe;
            StopsList.Children.Add(zeile);
            if (fokus == i)
            {
                rahmen = sichtbar;
            }

            sichtbar++;
        }

        // Fünf leere Zeilen wie auf dem Tablet, damit die letzte Haltestelle weiter nach oben rutschen kann.
        for (var n = 0; n < 5; n++)
        {
            StopsList.Children.Add(new Border { Height = hoehe, IsHitTestVisible = false });
        }

        var zeigen = kopf && rahmen is not null;
        StopOrangeFrame.IsVisible = zeigen;
        var rahmenGruen = fokus is int spielIndex && _session.Trip.Spielt(spielIndex);
        StopOrangeFrame.BorderBrush = new SolidColorBrush(Color.Parse(rahmenGruen ? "#4CAF50" : "#FFA500"));
        // Etwas höher als die Zeile, damit die Haltestelle mittig bleibt, und über die ganze Listenbreite.
        const double rahmenExtra = 4;
        StopOrangeFrame.Height = hoehe + rahmenExtra;
        StopOrangeFrame.Margin = new Thickness(0, hoehe - rahmenExtra / 2, 0, 0);
        if (!zeigen)
        {
            _gerahmt = null;
            _scrollZiel = null;
            var still = vorher;
            Dispatcher.UIThread.Post(() =>
            {
                var max = Math.Max(0, StopsScroll.Extent.Height - StopsScroll.Viewport.Height);
                StopsScroll.Offset = new Vector(0, Math.Min(still, max));
            }, DispatcherPriority.Background);
            return;
        }

        if (_gerahmt != rahmen || erzwingen)
        {
            _gerahmt = rahmen;
            // NaN = nach dem Layout die echte Zeilenmitte in den Rahmen setzen.
            // Ein festes index * Zeilenhöhe läuft weiter unten aus der Mitte.
            _scrollZiel = double.NaN;
            Dispatcher.UIThread.Post(Nachscrollen, DispatcherPriority.Background);
            return;
        }

        var behalten = vorher;
        Dispatcher.UIThread.Post(() =>
        {
            if (_scrollZiel is not null || _gerahmt != rahmenVorher)
            {
                return;
            }

            var max = Math.Max(0, StopsScroll.Extent.Height - StopsScroll.Viewport.Height);
            StopsScroll.Offset = new Vector(0, Math.Min(behalten, max));
        }, DispatcherPriority.Background);
    }

    private void Nachscrollen()
    {
        if (_scrollZiel is null || _gerahmt is not int index)
        {
            return;
        }

        var kindIndex = index + 1;
        if ((uint)kindIndex >= (uint)StopsList.Children.Count)
        {
            return;
        }

        var kind = StopsList.Children[kindIndex];
        if (kind.Bounds.Height < 1 || StopsScroll.Extent.Height < 1)
        {
            return;
        }

        var zeilenMitte = kind.Bounds.Y + kind.Bounds.Height / 2;
        var rahmenMitte = StopOrangeFrame.Margin.Top + StopOrangeFrame.Height / 2;
        var ziel = zeilenMitte - rahmenMitte;
        var max = Math.Max(0, StopsScroll.Extent.Height - StopsScroll.Viewport.Height);
        var y = Math.Clamp(ziel, 0, max);
        if (Math.Abs(StopsScroll.Offset.Y - y) > 0.5)
        {
            if (_scrollZiel is double gesetzt && !double.IsNaN(gesetzt) && Math.Abs(gesetzt - y) < 0.5)
            {
                _scrollZiel = null;
                return;
            }

            _scrollZiel = y;
            StopsScroll.Offset = new Vector(0, y);
            return;
        }

        _scrollZiel = null;
    }

    /// <summary>Sechs Haltestellen ganz, die siebte fast ganz. Ganze Pixel, damit sich die Zeilen nicht auseinanderziehen.</summary>
    private double Zeilenhoehe()
    {
        var sicht = StopsScroll.Bounds.Height;
        var hoehe = sicht < 80 ? 58 : sicht / 6.85;
        return Math.Max(48, Math.Round(hoehe));
    }

    private Control StopRow(RouteStopItem stop, int index, double luecke, double hoehe, bool erste, bool letzte, int anzahl)
    {
        var next = (_session.Trip.RahmenIndex ?? _session.Trip.NextIndex) == index;
        var spielt = _session.Trip.Spielt(index);
        var angesagt = _session.Trip.Angesagt(index);
        var rand = spielt || (next && !angesagt) ? "#4CAF50" : angesagt ? "#FFA500" : "#CCCCCC";
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A237E")),
            BorderBrush = new SolidColorBrush(Color.Parse(rand)),
            BorderThickness = new Thickness(spielt || angesagt || next ? 4 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(6, luecke, 0, luecke),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = stop.Name,
                        Foreground = Brushes.White,
                        FontSize = 18,
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = stop.Time,
                        Foreground = Brushes.White,
                        FontSize = 14,
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 1
                    }
                }
            }
        };
        var captured = index;
        card.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            if (FingerZug.Zieht)
            {
                return;
            }

            OnStopTap(captured);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        var gelb = new SolidColorBrush(Color.Parse("#FFEB3B"));
        var gruen = new SolidColorBrush(Color.Parse("#388E3C"));
        var marker = new Grid
        {
            Width = 37,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (anzahl > 1)
        {
            var linie = new Border
            {
                Width = 11,
                Background = gelb,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = erste ? VerticalAlignment.Bottom : letzte ? VerticalAlignment.Top : VerticalAlignment.Stretch
            };
            if (erste || letzte)
            {
                linie.Height = hoehe / 2;
            }

            marker.Children.Add(linie);
        }

        marker.Children.Add(new Grid
        {
            Width = 37,
            Height = 37,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Ellipse
                {
                    Width = 37,
                    Height = 37,
                    Fill = gelb,
                    Stroke = gruen,
                    StrokeThickness = 3
                },
                new TextBlock
                {
                    Text = "H",
                    Foreground = gruen,
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("37,*"),
            Margin = new Thickness(0)
        };
        row.Children.Add(marker);
        Grid.SetColumn(card, 1);
        row.Children.Add(card);
        return row;
    }

    private void OnStopTap(int index)
    {
        if (_selbsthaltung)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _stopLockUntil)
        {
            return;
        }

        // Ein Fingerdruck kommt auf dem Pi als Touch und als Maus. Das darf nur ein Tipp sein.
        if (_stopTapIndex == index && _stopTapAt != DateTime.MinValue && (now - _stopTapAt).TotalMilliseconds < 140)
        {
            return;
        }

        TastenTon.Play();
        if (_stopTapIndex == index && _stopTapAt != DateTime.MinValue && (now - _stopTapAt).TotalMilliseconds <= 550)
        {
            _stopTapCount++;
        }
        else
        {
            _stopTapCount = 1;
        }

        _stopTapIndex = index;
        _stopTapAt = now;
        var generation = ++_stopTapGeneration;
        if (_stopTapCount >= 3)
        {
            _stopTapCount = 0;
            _stopLockUntil = now.AddMilliseconds(400);
            _rahmenErzwingen = true;
            _session.Trip.PlayClicked(_session, index, greeting: true);
        }
        else if (_stopTapCount == 2)
        {
            _ = ConfirmStopDoubleTapAsync(generation, index);
        }
    }

    private async Task ConfirmStopDoubleTapAsync(int generation, int index)
    {
        await Task.Delay(160);
        if (generation != _stopTapGeneration || _stopTapCount < 2 || DateTime.UtcNow < _stopLockUntil)
        {
            return;
        }

        _stopTapCount = 0;
        _stopLockUntil = DateTime.UtcNow.AddMilliseconds(400);
        _rahmenErzwingen = true;
        _session.Trip.PlayClicked(_session, index, greeting: false);
    }

    private void PaintSignal(int level)
    {
        var bars = new[] { Signal0, Signal1, Signal2, Signal3 };
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i].Background = new SolidColorBrush(Color.Parse(i < level ? "#FFFFFF" : "#46FFFFFF"));
        }
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

            var level = LinuxHeaderStatus.SignalLevel();
            var battery = LinuxHeaderStatus.BatteryPercent(out _);
            var lage = LinuxHeaderStatus.LiesAkku();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IbisChipText.Text = show ? "IBIS-IP" : "IBIS-USB";
                IbisChip.Background = new SolidColorBrush(Color.Parse(show ? "#4CAF50" : "#FF0000"));
                PaintSignal(level);
                ApplyAkku(battery, lage);
            });
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
