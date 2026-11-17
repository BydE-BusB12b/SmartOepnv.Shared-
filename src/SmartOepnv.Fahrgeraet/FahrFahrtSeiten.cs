using Avalonia.Controls;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.Fahrgeraet;

public static class FahrFahrtSeiten
{
    public static Control Fahrt(FahrSession session, Action? openRoutes = null)
    {
        var trip = session.Trip;
        var port = FahrbetriebPages.Field("GPS-Anschluss");
        port.Text = session.GpsPort;
        var info = FahrbetriebPages.Info(trip.Status);
        var rows = new StackPanel { Spacing = 4 };
        var list = new ScrollViewer { Content = rows, Height = 420 };
        var start = FahrbetriebPages.Touch("GPS starten");
        start.Click += (_, _) =>
        {
            session.GpsPort = port.Text?.Trim() ?? "";
            session.SaveDevice();
            try
            {
                trip.Start(session);
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        var stop = FahrbetriebPages.Touch("GPS stoppen");
        stop.Click += (_, _) => trip.Stop();
        var now = FahrbetriebPages.Touch("Haltestelle jetzt ansagen");
        now.Click += (_, _) => trip.AnnounceNow(session);
        var reset = FahrbetriebPages.Touch("Folge zurücksetzen");
        reset.Click += (_, _) => trip.Reset();
        var direction = FahrbetriebPages.Touch("Linie / Kurs");
        // Doppeltipp bleibt die Außenansage. Kein Beenden, kein Schließen des Programms.
        var lastDirectionTap = DateTime.MinValue;
        direction.Click += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (lastDirectionTap != DateTime.MinValue && (now - lastDirectionTap).TotalMilliseconds <= 550)
            {
                lastDirectionTap = DateTime.MinValue;
                trip.PlayOutsideDirection(session);
                return;
            }

            lastDirectionTap = now;
        };
        // Wie Android: Doppeltipp auf eine Haltestelle spielt die Ansage und sendet Linie und Ziel.
        // Ein dritter Tipp innerhalb von 550 ms spielt die Begrüßung.
        var stopTapIndex = -1;
        var stopTapAt = DateTime.MinValue;
        var stopTapCount = 0;
        var stopTapGeneration = 0;
        void OnStopTap(int index)
        {
            var now = DateTime.UtcNow;
            if (stopTapIndex == index && stopTapAt != DateTime.MinValue && (now - stopTapAt).TotalMilliseconds <= 550)
            {
                stopTapCount++;
            }
            else
            {
                stopTapCount = 1;
            }

            stopTapIndex = index;
            stopTapAt = now;
            var generation = ++stopTapGeneration;
            if (stopTapCount >= 3)
            {
                stopTapCount = 0;
                trip.PlayClicked(session, index, greeting: true);
                return;
            }

            if (stopTapCount < 2)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(320);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != stopTapGeneration || stopTapCount >= 3)
                    {
                        return;
                    }

                    stopTapCount = 0;
                    trip.PlayClicked(session, index, greeting: false);
                });
            });
        }
        var routes = FahrbetriebPages.Touch(Sprache.T("Route wählen", "Route kiezen"));
        routes.Click += (_, _) => openRoutes?.Invoke();
        var pas = FahrbetriebPages.Touch("Pas.-Info");
        pas.Click += (_, _) =>
        {
            PasInfo.Active = !PasInfo.Active;
            if (PasInfo.Active)
            {
                trip.AlignPas(session);
            }
            else
            {
                GpsSpur.Event(session, "pas_off", "");
                info.Text = "Pas.-Info aus";
            }
        };
        var thanks = FahrbetriebPages.Touch("Danke");
        thanks.Click += async (_, _) => info.Text = await DankeAnzeige.Show(session);
        var simulate = FahrbetriebPages.Touch(Sprache.T("Fahrt simulieren", "Rit simuleren"));
        simulate.Click += (_, _) => trip.Simulate(session);
        var map = FahrKarte.Create();
        var navInfo = FahrbetriebPages.Info(FahrKarte.Instruction(session));
        var symbols = new StackPanel { Spacing = 2 };
        foreach (var symbol in FahrKarte.Symbols)
        {
            symbols.Children.Add(new TextBlock { Text = symbol, Foreground = Brushes.White, FontSize = 14 });
        }

        var symbolList = new ScrollViewer { Content = symbols, Height = 160, IsVisible = false };
        var nav = FahrbetriebPages.Touch(Sprache.T("Navigation", "Navigatie"));
        nav.Click += (_, _) =>
        {
            symbolList.IsVisible = !symbolList.IsVisible;
            navInfo.Text = FahrKarte.Instruction(session);
        };
        void Show()
        {
            rows.Children.Clear();
            var lines = trip.Lines(session);
            var stops = session.Package is null || string.IsNullOrWhiteSpace(session.RouteName)
                ? new List<RouteStopItem>()
                : session.Package.GetStops(session.RouteName).ToList();
            var next = trip.NextIndex ?? -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var marked = i == next;
                var rowIndex = i;
                var stop = i < stops.Count ? stops[i] : null;
                var row = new Button
                {
                    Content = StopRow(i, stop, lines[i]),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    Padding = new Avalonia.Thickness(8),
                    BorderThickness = new Avalonia.Thickness(3),
                    BorderBrush = marked ? new SolidColorBrush(Color.Parse("#FFA500")) : Brushes.Transparent,
                    CornerRadius = new Avalonia.CornerRadius(10)
                };
                row.Click += (_, _) => OnStopTap(rowIndex);
                rows.Children.Add(row);
            }

            direction.Content = DirectionCaption(session);
            var ansage = FahrAnsage.Active ? "\nAnsage läuft" : "";
            info.Text = string.IsNullOrWhiteSpace(session.RouteName)
                ? Sprache.T("Keine Route gewählt.", "Geen route gekozen.")
                : $"{session.RouteName}\n{trip.Status}{(PasInfo.Active ? "\nPas.-Info an" : "")}{ansage}";
            navInfo.Text = FahrKarte.Instruction(session);
            FahrKarte.Update(map, session);
        }

        trip.Changed = () => Avalonia.Threading.Dispatcher.UIThread.Post(Show);
        Show();
        return ItcsPause.Cover(session, FahrbetriebPages.Column(
            "ITCS", direction, routes, port, start, stop, now, reset, pas, thanks, simulate, nav, navInfo, symbolList, map, info, list));
    }

    private static Control StopRow(int index, RouteStopItem? stop, string line)
    {
        var nameText = stop is null ? line : $"{index + 1}. {stop.Name}";
        var rest = stop is null || line.Length <= nameText.Length
            ? ""
            : line[nameText.Length..].Trim();
        var name = new TextBlock
        {
            Text = nameText,
            Foreground = new SolidColorBrush(Color.Parse("#FFFF00")),
            FontSize = 32,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        row.Children.Add(name);
        if (rest.Length > 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = rest,
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Avalonia.Thickness(16, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
        }

        return row;
    }

    private static string DirectionCaption(FahrSession session)
    {
        if (string.IsNullOrWhiteSpace(session.RouteName))
        {
            return "Linie / Kurs";
        }

        var parsed = RouteDisplayHelper.Parse(session.RouteName);
        var line = string.IsNullOrWhiteSpace(parsed.PassengerDisplayLine) ? parsed.LineCourse : parsed.PassengerDisplayLine;
        return string.IsNullOrWhiteSpace(line) ? session.RouteName : line.Trim();
    }

    public static Control Bearbeiten(FahrSession session, FahrgeraetRouteSync sync)
    {
        var stops = session.Package is null || string.IsNullOrWhiteSpace(session.RouteName)
            ? new List<RouteStopItem>()
            : session.Package.GetStops(session.RouteName).ToList();
        var list = List();
        var name = FahrbetriebPages.Field("Name");
        var gps = FahrbetriebPages.Field("Ansage-GPS");
        var board = FahrbetriebPages.Field("Haltestellen-GPS");
        var radius = FahrbetriebPages.Field("Radius m");
        var line = FahrbetriebPages.Field("Linie");
        var destination = FahrbetriebPages.Field("Ziel");
        var time = FahrbetriebPages.Field("Uhrzeit");
        var sound = FahrbetriebPages.Field("Tondatei");
        var ibis = FahrbetriebPages.Field("IBIS-Ansagenummer");
        var nextRoute = FahrbetriebPages.Field("Nächste Fahrt");
        var announce = FahrbetriebPages.Check("Ansage", true);
        var waypoint = FahrbetriebPages.Check("Wegpunkt", false);
        var end = FahrbetriebPages.Check("Endhaltestelle", false);
        var change = FahrbetriebPages.Check("Routenwechsel", false);
        var info = FahrbetriebPages.Info(session.RouteName ?? "Keine Route gewählt.");
        var index = -1;

        void ShowList()
        {
            list.Items.Clear();
            foreach (var stop in stops)
            {
                list.Items.Add(stop.Name);
            }
        }

        void Load(int i)
        {
            if (i < 0 || i >= stops.Count)
            {
                return;
            }

            index = i;
            var stop = stops[i];
            name.Text = stop.Name;
            gps.Text = stop.GpsCoordinates;
            board.Text = stop.StopCoordinates;
            radius.Text = stop.Radius.ToString();
            line.Text = stop.LineNumber;
            destination.Text = stop.Destination;
            time.Text = stop.Time;
            sound.Text = stop.EmbeddedSoundFileName;
            ibis.Text = stop.IbisAnnouncementId;
            nextRoute.Text = stop.SelectedLineCourseTrip;
            announce.IsChecked = stop.IsAnnouncementEnabled;
            waypoint.IsChecked = stop.IsWaypoint;
            end.IsChecked = stop.IsEndStop;
            change.IsChecked = stop.RouteChangeEnabled;
        }

        void Store()
        {
            if (index < 0 || index >= stops.Count)
            {
                return;
            }

            var stop = stops[index];
            stop.Name = name.Text?.Trim() ?? "";
            stop.GpsCoordinates = gps.Text?.Trim() ?? "";
            stop.StopCoordinates = board.Text?.Trim() ?? "";
            stop.Radius = int.TryParse(radius.Text, out var meters) ? meters : 50;
            stop.LineNumber = line.Text?.Trim() ?? "";
            stop.Destination = destination.Text?.Trim() ?? "";
            stop.Time = time.Text?.Trim() ?? "";
            stop.EmbeddedSoundFileName = sound.Text?.Trim() ?? "";
            stop.IbisAnnouncementId = ibis.Text?.Trim() ?? "";
            stop.SelectedLineCourseTrip = nextRoute.Text?.Trim() ?? "";
            stop.IsAnnouncementEnabled = announce.IsChecked == true;
            stop.IsWaypoint = waypoint.IsChecked == true;
            stop.IsEndStop = end.IsChecked == true;
            stop.RouteChangeEnabled = change.IsChecked == true;
        }

        list.SelectionChanged += (_, _) =>
        {
            Store();
            Load(list.SelectedIndex);
        };
        var up = FahrbetriebPages.Touch("Nach oben");
        up.Click += (_, _) => Move(-1);
        var down = FahrbetriebPages.Touch("Nach unten");
        down.Click += (_, _) => Move(1);
        var add = FahrbetriebPages.Touch("Haltestelle anfügen");
        add.Click += (_, _) =>
        {
            Store();
            stops.Add(new RouteStopItem { Name = "Neue Haltestelle", Radius = 50, RouteName = session.RouteName ?? "" });
            ShowList();
            list.SelectedIndex = stops.Count - 1;
        };
        var remove = FahrbetriebPages.Touch("Haltestelle löschen");
        remove.Click += (_, _) =>
        {
            if (index < 0 || index >= stops.Count)
            {
                return;
            }

            stops.RemoveAt(index);
            index = -1;
            ShowList();
        };
        var save = FahrbetriebPages.Touch("Speichern");
        save.Click += (_, _) =>
        {
            if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
            {
                info.Text = "Keine Route gewählt.";
                return;
            }

            Store();
            session.Package.ReplaceStopsForRoute(session.RouteName, stops);
            File.WriteAllText(sync.LocalPackagePath, session.Package.ToJson(false, false, false));
            session.Trip.Reset();
            info.Text = $"{stops.Count} Haltestellen gespeichert.";
        };
        ShowList();
        if (stops.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        return FahrbetriebPages.Column(
            "Routenbearbeitung",
            info, list, up, down, add, remove,
            name, time, line, destination, gps, board, radius, sound, ibis, nextRoute,
            announce, waypoint, end, change, save);

        void Move(int delta)
        {
            Store();
            var target = index + delta;
            if (index < 0 || target < 0 || target >= stops.Count)
            {
                return;
            }

            (stops[index], stops[target]) = (stops[target], stops[index]);
            ShowList();
            list.SelectedIndex = target;
        }
    }

    public static Control Vrr(FahrSession session)
    {
        var query = FahrbetriebPages.Field("Haltestelle oder ID");
        var info = FahrbetriebPages.Info("VRR-Katalog");
        var results = List();
        var stops = List();
        var found = new List<VrrStopEntry>();
        var routeStops = session.Package is null || string.IsNullOrWhiteSpace(session.RouteName)
            ? new List<RouteStopItem>()
            : session.Package.GetStops(session.RouteName).ToList();
        foreach (var stop in routeStops)
        {
            var id = string.IsNullOrWhiteSpace(stop.VrrStopId) ? "" : $"  VRR {stop.VrrStopId}";
            stops.Items.Add(stop.Name + id);
        }

        var search = FahrbetriebPages.Touch("Suchen");
        search.Click += (_, _) =>
        {
            VrrStopCatalog.EnsureLoaded();
            found.Clear();
            results.Items.Clear();
            foreach (var entry in VrrStopCatalog.Suggest(query.Text ?? "", 30))
            {
                found.Add(entry);
                results.Items.Add($"{entry.DisplayLine}  {entry.Subtitle}");
            }

            info.Text = found.Count == 0 ? "Nichts gefunden." : $"{found.Count} Treffer, Katalog {VrrStopCatalog.Size}.";
        };
        var assign = FahrbetriebPages.Touch("VRR an Haltestelle übernehmen");
        assign.Click += (_, _) =>
        {
            if (results.SelectedIndex < 0 || stops.SelectedIndex < 0 || session.Package is null || session.RouteName is null)
            {
                info.Text = "Treffer und Haltestelle wählen.";
                return;
            }

            var entry = found[results.SelectedIndex];
            var stop = routeStops[stops.SelectedIndex];
            stop.VrrStopId = entry.Id;
            if (entry.Lat is not null && entry.Lon is not null && string.IsNullOrWhiteSpace(stop.StopCoordinates))
            {
                stop.StopCoordinates = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{entry.Lat:0.000000},{entry.Lon:0.000000}");
            }

            session.Package.ReplaceStopsForRoute(session.RouteName, routeStops);
            File.WriteAllText(new FahrgeraetRouteSync().LocalPackagePath, session.Package.ToJson(false, false, false));
            info.Text = $"{stop.Name}: VRR {entry.Id}";
            stops.Items[stops.SelectedIndex] = $"{stop.Name}  VRR {entry.Id}";
        };
        return FahrbetriebPages.Column("VRR", query, search, info, results, stops, assign);
    }

    private static ListBox List() => new()
    {
        FontSize = 16,
        Foreground = Brushes.White,
        Background = Brushes.Transparent
    };
}
