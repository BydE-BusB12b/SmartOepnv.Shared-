using Avalonia.Controls;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.Fahrgeraet;

public static class FahrFahrtSeiten
{
    public static Control Fahrt(FahrSession session)
    {
        var trip = session.Trip;
        var port = FahrbetriebPages.Field("GPS-Anschluss");
        port.Text = session.GpsPort;
        var info = FahrbetriebPages.Info(trip.Status);
        var list = List();
        void Show()
        {
            list.Items.Clear();
            foreach (var line in trip.Lines(session))
            {
                list.Items.Add(line);
            }

            if (trip.NextIndex is int index && index < list.Items.Count)
            {
                list.SelectedIndex = index;
            }

            info.Text = string.IsNullOrWhiteSpace(session.RouteName)
                ? "Keine Route gewählt."
                : $"{session.RouteName}\n{trip.Status}";
        }

        trip.Changed = () => Avalonia.Threading.Dispatcher.UIThread.Post(Show);
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
        Show();
        return FahrbetriebPages.Column("Fahrt", port, start, stop, now, reset, info, list);
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
