using System.Text.Json.Nodes;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// GPS-Fahrt wie in der App: nur die nächste Haltestelle in Listenreihenfolge,
/// Radius, Richtungserkennung, Ansage, Ziel und automatischer Routenwechsel.
/// </summary>
public sealed class FahrTrip
{
    private readonly HashSet<int> _played = [];
    private readonly HashSet<int> _zielwechsel = [];
    private bool _listening;
    private FahrSession? _listenSession;
    private double? _previousLat;
    private double? _previousLon;
    private string? _route;
    private string? _shapeRoute;
    private List<RoutePathLatLng>? _shape;
    private string? _sevRoute;
    private int? _entwerterIndex;
    private double _entwerterLat;
    private double _entwerterLon;
    private bool _entwerterLeft;
    private readonly HashSet<int> _hints = [];

    public bool Running { get; private set; }
    public string Status { get; private set; } = "Fahrt nicht gestartet.";
    public int? NextIndex { get; private set; }
    public Action? Changed { get; set; }

    public void Reset()
    {
        _played.Clear();
        _zielwechsel.Clear();
        _hints.Clear();
        _entwerterIndex = null;
        _sevRoute = null;
        _previousLat = _previousLon = null;
        NextIndex = null;
        _route = null;
        _shapeRoute = null;
        _shape = null;
        Status = "Haltestellenfolge zurückgesetzt.";
        Changed?.Invoke();
    }

    public void Start(FahrSession session)
    {
        _listenSession = session;
        if (!_listening)
        {
            _listening = true;
            GpsQuelle.Fix += OnSharedFix;
        }

        try
        {
            GpsQuelle.Hold("fahrt", session.GpsPort);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Changed?.Invoke();
            return;
        }

        Running = true;
        Status = "Warte auf GPS.";
        Changed?.Invoke();
    }

    public void Stop()
    {
        Running = false;
        if (_listening)
        {
            GpsQuelle.Fix -= OnSharedFix;
            _listening = false;
        }

        GpsQuelle.Drop("fahrt");
        Status = "Fahrt gestoppt.";
        Changed?.Invoke();
    }

    private void OnSharedFix(double lat, double lon)
    {
        if (!Running || _listenSession is null)
        {
            return;
        }

        OnFix(_listenSession, lat, lon);
    }

    public void AnnounceNow(FahrSession session)
    {
        var stops = Stops(session);
        var index = NextPlayable(stops);
        if (index < 0)
        {
            Status = "Keine weitere Haltestelle.";
            Changed?.Invoke();
            return;
        }

        Fire(session, stops, index, "Manuell");
    }

    public IReadOnlyList<string> Lines(FahrSession session)
    {
        var stops = Stops(session);
        var next = NextPlayable(stops);
        NextIndex = next < 0 ? null : next;
        var lines = new List<string>(stops.Count);
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            var mark = _played.Contains(i) ? "erledigt" : i == next ? "nächste" : stop.IsWaypoint ? "Wegpunkt" : "";
            var time = string.IsNullOrWhiteSpace(stop.Time) ? "" : "  " + stop.Time;
            lines.Add($"{i + 1}. {stop.Name}{time}  {mark}".Trim());
        }

        return lines;
    }

    private void OnFix(FahrSession session, double lat, double lon)
    {
        var stops = Stops(session);
        if (!string.Equals(_route, session.RouteName, StringComparison.Ordinal))
        {
            _played.Clear();
            _zielwechsel.Clear();
            _hints.Clear();
            _route = session.RouteName;
            _ = SendSevAsync(session, stops);
        }

        ScanHints(session, stops, lat, lon);
        LeaveEntwerter(session, lat, lon);
        var index = NextPlayable(stops);
        NextIndex = index < 0 ? null : index;
        if (index >= 0 && InRadius(session, stops, index, lat, lon) && Towards(session, lat, lon, stops[index]))
        {
            Fire(session, stops, index, "GPS");
            MaybeOpenNextRoute(session, stops[index]);
        }
        else if (index >= 0)
        {
            var meters = Distance(stops[index], lat, lon);
            Status = meters is null
                ? $"{stops[index].Name}: keine Koordinaten"
                : $"{stops[index].Name}: {meters:0} m";
            Changed?.Invoke();
        }

        ScanZielwechsel(session, stops, lat, lon);
        _previousLat = lat;
        _previousLon = lon;
    }

    private void Fire(FahrSession session, IReadOnlyList<RouteStopItem> stops, int index, string reason)
    {
        if (_played.Contains(index))
        {
            return;
        }

        _played.Add(index);
        var stop = stops[index];
        NextIndex = NextPlayable(stops);
        Status = $"{reason}: {stop.Name}";
        Changed?.Invoke();
        if (stop.Name.StartsWith("Hinweis:", StringComparison.OrdinalIgnoreCase))
        {
            Status = stop.Name;
            Changed?.Invoke();
            return;
        }

        Play(session, stop);
        if (stop.PlayStartStopGreeting)
        {
            PlayGreeting(session);
        }

        if (stop.StopHintEnabled &&
            RouteStopHintTrigger.Normalize(stop.StopHintTriggerMode) == RouteStopHintTrigger.WithAnnouncement &&
            !string.IsNullOrWhiteSpace(stop.StopHintText))
        {
            Status = stop.StopHintText;
            Changed?.Invoke();
        }

        if (stop.EntwerterEnabled)
        {
            SendEntwerter(session, stop);
            if (StopMatch.TryCoord(stop, out var slat, out var slon) || StopMatch.TryParse(stop.StopCoordinates, out slat, out slon))
            {
                _entwerterIndex = index;
                _entwerterLat = slat;
                _entwerterLon = slon;
                _entwerterLeft = false;
            }
        }

        _ = SendDisplay(session, stop, stops);
    }

    private void ScanZielwechsel(FahrSession session, IReadOnlyList<RouteStopItem> stops, double lat, double lon)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (!stop.ZielwechselEnabled || _zielwechsel.Contains(i))
            {
                continue;
            }

            var raw = string.IsNullOrWhiteSpace(stop.ZielwechselGpsCoordinates)
                ? stop.GpsCoordinates
                : stop.ZielwechselGpsCoordinates;
            if (!StopMatch.TryParse(raw, out var slat, out var slon))
            {
                continue;
            }

            var radius = stop.ZielwechselRadius <= 0 ? 40 : stop.ZielwechselRadius;
            if (StopMatch.Meters(lat, lon, slat, slon) > radius)
            {
                continue;
            }

            if (!Towards(session, lat, lon, slat, slon))
            {
                Status = $"Zielwechsel Gegenrichtung: {stop.Name}";
                Changed?.Invoke();
                continue;
            }

            _zielwechsel.Add(i);
            Status = $"Zielwechsel: {DestinationOf(stop)}";
            Changed?.Invoke();
            _ = SendDisplay(session, stop, stops);
        }
    }

    private static async Task SendDisplay(FahrSession session, RouteStopItem stop, IReadOnlyList<RouteStopItem> stops)
    {
        try
        {
            var course = stops.Select(item => item.Name).ToList();
            await DisplayTelegrams.SendAsync(session, stop.LineNumber, DestinationOf(stop), stop.Name, course);
        }
        catch (Exception ex)
        {
            session.Status?.Invoke(ex.Message);
        }
    }

    private static void Play(FahrSession session, RouteStopItem stop)
    {
        if (session.Package is null)
        {
            return;
        }

        var id = IbisAnnouncementIds.Normalize(stop.IbisAnnouncementId);
        if (GeraetEinstellungen.Text("announcement_delivery", "bluetooth") == "ibis")
        {
            if (id.Length > 0)
            {
                VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("hP" + id), 9600);
            }

            return;
        }

        if (!stop.IsAnnouncementEnabled && string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))
        {
            return;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        var key = sounds.Keys.FirstOrDefault(k =>
            string.Equals(k, stop.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            return;
        }

        var zones = GeraetEinstellungen.Bool("announcement_stereo_zones_enabled", true);
        var bytes = Convert.FromBase64String(sounds[key].Base64);
        StereoWav.Play(bytes, zones ? true : null);
    }

    private void MaybeOpenNextRoute(FahrSession session, RouteStopItem stop)
    {
        if (!stop.IsEndStop || !stop.RouteChangeEnabled || !GeraetEinstellungen.Bool("auto_open_next_route", true))
        {
            return;
        }

        var next = NextRoute(stop);
        if (string.IsNullOrWhiteSpace(next) || session.Package?.GetStops(next).Count == 0)
        {
            return;
        }

        session.RouteName = next;
        _played.Clear();
        _zielwechsel.Clear();
        _route = next;
        Status = $"Nächste Fahrt: {next}";
        Changed?.Invoke();
    }

    private static string NextRoute(RouteStopItem stop)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dated = stop.RouteChangeTargetsByDate.FirstOrDefault(entry =>
            entry.OperatingDates.Contains(today) && !string.IsNullOrWhiteSpace(entry.SelectedLineCourseTrip));
        return string.IsNullOrWhiteSpace(dated?.SelectedLineCourseTrip)
            ? stop.SelectedLineCourseTrip
            : dated.SelectedLineCourseTrip;
    }

    private bool Towards(FahrSession session, double lat, double lon, RouteStopItem stop)
    {
        if (!StopMatch.TryCoord(stop, out var slat, out var slon) &&
            !StopMatch.TryParse(stop.StopCoordinates, out slat, out slon))
        {
            return true;
        }

        return Towards(session, lat, lon, slat, slon);
    }

    private bool Towards(FahrSession session, double lat, double lon, double slat, double slon)
    {
        if (!GeraetEinstellungen.Bool("direction_detection_enabled", true) || _previousLat is null || _previousLon is null)
        {
            return true;
        }

        var moved = StopMatch.Meters(_previousLat.Value, _previousLon.Value, lat, lon);
        if (moved < 10)
        {
            return true;
        }

        var toStop = StopMatch.Bearing(lat, lon, slat, slon);
        var movement = StopMatch.Bearing(_previousLat.Value, _previousLon.Value, lat, lon);
        var difference = Math.Abs(toStop - movement);
        if (difference > 180)
        {
            difference = 360 - difference;
        }

        if (difference <= 45)
        {
            return true;
        }

        Status = $"Gegenrichtung ({difference:0}°)";
        Changed?.Invoke();
        return false;
    }

    private int NextPlayable(IReadOnlyList<RouteStopItem> stops)
    {
        var from = 0;
        while (from < stops.Count && _played.Contains(from))
        {
            from++;
        }

        for (var i = from; i < stops.Count; i++)
        {
            var name = stops[i].Name ?? "";
            if (_played.Contains(i) || stops[i].IsWaypoint ||
                name.StartsWith("Hinweis:", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Weg-Punkt", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Zwischenhalt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private bool InRadius(FahrSession session, IReadOnlyList<RouteStopItem> stops, int index, double lat, double lon)
    {
        var stop = stops[index];
        var meters = Distance(stop, lat, lon);
        var radius = stop.Radius <= 0 ? 50 : stop.Radius;
        if (index > 0)
        {
            var along = DistanceOnApproach(stops[index - 1], stop, lat, lon, radius);
            if (along is not null)
            {
                meters = meters is null ? along : Math.Min(meters.Value, along.Value);
            }
        }

        var road = DistanceOnRoad(session, stops, stop, lat, lon, radius);
        if (road is not null)
        {
            meters = meters is null ? road : Math.Min(meters.Value, road.Value);
        }

        return meters is not null && meters <= radius;
    }

    private double? DistanceOnRoad(
        FahrSession session,
        IReadOnlyList<RouteStopItem> stops,
        RouteStopItem stop,
        double lat,
        double lon,
        int radius)
    {
        var shape = Shape(session, stops);
        if (shape is null || !Point(stop, out var slat, out var slon))
        {
            return null;
        }

        var vehicle = new RoutePathLatLng { Lat = lat, Lon = lon };
        var target = new RoutePathLatLng { Lat = slat, Lon = slon };
        if (!BildfahrplanStopAxis.TryDistanceAlongPolyline(shape, vehicle, out var vehicleAlong, out var offRoad) ||
            !BildfahrplanStopAxis.TryDistanceAlongPolyline(shape, target, out var stopAlong, out _))
        {
            return null;
        }

        if (Math.Abs(vehicleAlong - stopAlong) > radius)
        {
            return null;
        }

        return offRoad;
    }

    private List<RoutePathLatLng>? Shape(FahrSession session, IReadOnlyList<RouteStopItem> stops)
    {
        if (string.Equals(_shapeRoute, session.RouteName, StringComparison.Ordinal))
        {
            return _shape;
        }

        _shapeRoute = session.RouteName;
        _shape = null;
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            return null;
        }

        var draft = RoutePathDraftRepository.LoadOrCreate(session.RouteName, stops.ToList(), session.Package.PackageRoot);
        if (draft.SnappedShape.Count >= 2)
        {
            _shape = draft.SnappedShape.ToList();
        }

        return _shape;
    }

    private static double? Distance(RouteStopItem stop, double lat, double lon)
    {
        double? best = null;
        void Consider(string? raw)
        {
            if (!StopMatch.TryParse(raw, out var slat, out var slon))
            {
                return;
            }

            var meters = StopMatch.Meters(lat, lon, slat, slon);
            best = best is null ? meters : Math.Min(best.Value, meters);
        }

        Consider(stop.GpsCoordinates);
        Consider(stop.StopCoordinates);
        return best;
    }

    private static string DestinationOf(RouteStopItem stop) =>
        First(stop.Destination, stop.EndDestination, stop.Ds021NeuDestination, stop.FmaS1Destination, stop.Ds003aDestination);

    private static string First(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static List<RouteStopItem> Stops(FahrSession session) =>
        session.Package is null || string.IsNullOrWhiteSpace(session.RouteName)
            ? []
            : session.Package.GetStops(session.RouteName).ToList();

    private static double? DistanceOnApproach(
        RouteStopItem previous,
        RouteStopItem stop,
        double lat,
        double lon,
        int radius)
    {
        if (!Point(previous, out var alat, out var alon) || !Point(stop, out var blat, out var blon))
        {
            return null;
        }

        var closest = Closest(alat, alon, blat, blon, lat, lon);
        if (StopMatch.Meters(closest.Lat, closest.Lon, blat, blon) > radius)
        {
            return null;
        }

        return StopMatch.Meters(lat, lon, closest.Lat, closest.Lon);
    }

    private static bool Point(RouteStopItem stop, out double lat, out double lon) =>
        StopMatch.TryCoord(stop, out lat, out lon) || StopMatch.TryParse(stop.StopCoordinates, out lat, out lon) ||
        StopMatch.TryParse(stop.GpsCoordinates, out lat, out lon);

    private static (double Lat, double Lon) Closest(
        double alat, double alon, double blat, double blon, double lat, double lon)
    {
        var mean = (alat + blat) / 2 * Math.PI / 180;
        var x1 = alon * Math.Cos(mean);
        var y1 = alat;
        var x2 = blon * Math.Cos(mean);
        var y2 = blat;
        var x = lon * Math.Cos(mean);
        var y = lat;
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((x - x1) * dx + (y - y1) * dy) / length, 0, 1);
        var scale = Math.Cos(mean);
        return (y1 + t * dy, scale == 0 ? alon : (x1 + t * dx) / scale);
    }

    private static void PlayGreeting(FahrSession session)
    {
        if (session.Package is null)
        {
            return;
        }

        var root = session.Package.PackageRoot;
        PlayNamed(session, root[StartStopGreetingResolver.Part1RootJsonFieldName]?.GetValue<string>());
        PlayNamed(session, root[StartStopGreetingResolver.Part2RootJsonFieldName]?.GetValue<string>());
    }

    private static void PlayNamed(FahrSession session, string? fileName)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        var key = sounds.Keys.FirstOrDefault(k => string.Equals(k, fileName, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            return;
        }

        StereoWav.Play(Convert.FromBase64String(sounds[key].Base64), true);
    }

    private void ScanHints(FahrSession session, IReadOnlyList<RouteStopItem> stops, double lat, double lon)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            var namedHint = stop.Name.StartsWith("Hinweis:", StringComparison.OrdinalIgnoreCase);
            if (_hints.Contains(i) ||
                (!namedHint &&
                 (!stop.StopHintEnabled ||
                  RouteStopHintTrigger.Normalize(stop.StopHintTriggerMode) != RouteStopHintTrigger.OwnGps)))
            {
                continue;
            }

            var raw = string.IsNullOrWhiteSpace(stop.StopHintGpsCoordinates) ? stop.GpsCoordinates : stop.StopHintGpsCoordinates;
            if (!StopMatch.TryParse(raw, out var slat, out var slon))
            {
                continue;
            }

            var radius = stop.StopHintRadius <= 0 ? 40 : stop.StopHintRadius;
            if (StopMatch.Meters(lat, lon, slat, slon) > radius)
            {
                continue;
            }

            _hints.Add(i);
            Status = namedHint || string.IsNullOrWhiteSpace(stop.StopHintText) ? stop.Name : stop.StopHintText;
            Changed?.Invoke();
        }
    }

    private void SendEntwerter(FahrSession session, RouteStopItem stop)
    {
        try
        {
            var now = DateTime.Now;
            var line = new string((stop.LineNumber ?? "").Where(char.IsDigit).Take(3).ToArray()).PadLeft(3, '0');
            var six = new string(GeraetEinstellungen.Text("entwerter_ds004", "000000").Where(char.IsDigit).Take(6).ToArray()).PadLeft(6, '0');
            var four = new string(GeraetEinstellungen.Text("entwerter_ds004a", "0000").Where(char.IsDigit).Take(4).ToArray()).PadLeft(4, '0');
            if (GeraetEinstellungen.Bool("itcs_razzia_button_enabled", false) && four.Length == 4)
            {
                four = four[..3] + "0";
            }

            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l" + line), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis($"u{now:HHmm}"), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis($"d{now:ddMM}{now.Year % 10}"), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("e" + six), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("eA" + four), 9600);
        }
        catch (Exception ex)
        {
            session.Status?.Invoke(ex.Message);
        }
    }

    private void LeaveEntwerter(FahrSession session, double lat, double lon)
    {
        if (_entwerterLeft || _entwerterIndex is null)
        {
            return;
        }

        if (StopMatch.Meters(lat, lon, _entwerterLat, _entwerterLon) < 120)
        {
            return;
        }

        _entwerterLeft = true;
        var stops = Stops(session);
        if (_entwerterIndex.Value < stops.Count)
        {
            SendEntwerter(session, stops[_entwerterIndex.Value]);
        }
    }

    private async Task SendSevAsync(FahrSession session, IReadOnlyList<RouteStopItem> stops)
    {
        if (!GeraetEinstellungen.Bool("sev_tft_enabled", false) ||
            !GeraetEinstellungen.Bool("sev_tft_auto_send", true) ||
            string.Equals(_sevRoute, session.RouteName, StringComparison.Ordinal))
        {
            return;
        }

        _sevRoute = session.RouteName;
        if (!SevTftRouteBuilder.TryBuild(session.RouteName, stops, null, out var payload, out _) || payload is null)
        {
            return;
        }

        var host = GeraetEinstellungen.Text("sev_tft_host", "");
        var port = GeraetEinstellungen.Int("sev_tft_port", 0);
        if (string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            session.Status?.Invoke("SEV-TFT: Host oder Port fehlt.");
            return;
        }

        var result = await new SevTftClient().PostRouteAsync(
            new SevTftEndpoint(host, port, GeraetEinstellungen.Text("sev_tft_api_token", "")),
            payload);
        session.Status?.Invoke(result.Message);
    }
}
