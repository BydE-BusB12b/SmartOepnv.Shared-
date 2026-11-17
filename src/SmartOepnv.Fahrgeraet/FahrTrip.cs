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
    private string? _destinationRoute;
    private string? _shapeRoute;
    private List<RoutePathLatLng>? _shape;
    private string? _sevRoute;
    private int? _entwerterIndex;
    private double _entwerterLat;
    private double _entwerterLon;
    private bool _entwerterLeft;
    private readonly HashSet<int> _hints = [];
    private static readonly object PlayGate = new();
    public double LastLat { get; private set; }
    public double LastLon { get; private set; }
    public double LastSpeedMps { get; private set; }
    public bool HasFix { get; private set; }
    public DateTime LastFixUtc { get; private set; } = DateTime.MinValue;
    public double ShownAlong { get; set; }
    public bool HasShownAlong { get; set; }
    private bool _simulating;
    private DateTime _previousFixUtc = DateTime.MinValue;
    private double _holdLat;
    private double _holdLon;
    private bool _hasHold;

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
        _hasHold = false;
        _previousFixUtc = DateTime.MinValue;
        NextIndex = null;
        _route = null;
        _destinationRoute = null;
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
        Abfahrtston.Arm(session);
        IbisIpDienst.Ensure(session);
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
        Abfahrtston.Disarm();
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
        var now = DateTime.UtcNow;
        var speed = 0.0;
        if (_previousLat is not null && _previousLon is not null)
        {
            var seconds = (now - _previousFixUtc).TotalSeconds;
            if (seconds > 0.05)
            {
                speed = StopMatch.Meters(_previousLat.Value, _previousLon.Value, lat, lon) / seconds;
            }
        }

        if (_hasHold && speed <= 0.3 && StopMatch.Meters(lat, lon, _holdLat, _holdLon) > 20)
        {
            lat = _holdLat;
            lon = _holdLon;
        }
        else
        {
            _holdLat = lat;
            _holdLon = lon;
            _hasHold = true;
        }

        _previousFixUtc = now;
        LastLat = lat;
        LastLon = lon;
        HasFix = true;
        LastSpeedMps = speed;
        LastFixUtc = now;
        GpsSpur.Note(session, lat, lon, speed);
        IbisIpDienst.NoteGnss(lat, lon, speed);
        Abfahrtston.Tick(session, this, lat, lon);
        var stops = Stops(session);
        if (!string.Equals(_route, session.RouteName, StringComparison.Ordinal))
        {
            var routeChanged = _route is not null;
            _played.Clear();
            _zielwechsel.Clear();
            _hints.Clear();
            if (routeChanged)
            {
                _destinationRoute = null;
            }

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

        var greeting = stop.PlayStartStopGreeting;
        Task.Run(() =>
        {
            lock (PlayGate)
            {
                Play(session, stop);
                if (greeting)
                {
                    PlayGreeting(session);
                }
            }
        });

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

    /// <summary>
    /// Doppeltipp auf eine Haltestelle: Ansage und Zielversand wie in der Android-App.
    /// Der erste Versand einer Fahrt nimmt Linie und Ziel der Starthaltestelle.
    /// Ein Dreifachtipp spielt die Begrüßung, wenn der Halt sie hat.
    /// </summary>
    public void PlayClicked(FahrSession session, int index, bool greeting)
    {
        var stops = Stops(session);
        if (index < 0 || index >= stops.Count)
        {
            return;
        }

        var stop = stops[index];
        if (greeting && !stop.PlayStartStopGreeting)
        {
            greeting = false;
        }

        for (var i = 0; i <= index; i++)
        {
            _played.Add(i);
        }

        NextIndex = NextPlayable(stops);
        var target = stop;
        if (!greeting && !string.Equals(_destinationRoute, session.RouteName, StringComparison.Ordinal))
        {
            target = stops.FirstOrDefault(item => !item.IsWaypoint && IsStartStop(item))
                ?? stops.FirstOrDefault(item => !item.IsWaypoint)
                ?? stop;
        }

        var choice = ChooseDisplay(session, target);
        Status = choice.Send
            ? $"Linie {choice.Line} · {choice.Destination}"
            : greeting
                ? $"Begrüßung: {stop.Name}"
                : $"Manuell: {stop.Name}";
        if (!choice.Send && !string.Equals(_destinationRoute, session.RouteName, StringComparison.Ordinal))
        {
            Status = $"Kein Ziel für {target.Name}";
        }

        Changed?.Invoke();
        Task.Run(() =>
        {
            lock (PlayGate)
            {
                if (greeting)
                {
                    PlayGreeting(session);
                }
                else
                {
                    Play(session, stop);
                }
            }
        });
        if (choice.Send)
        {
            _ = SendDisplay(session, target, stops);
        }
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

    private async Task SendDisplay(FahrSession session, RouteStopItem stop, IReadOnlyList<RouteStopItem> stops)
    {
        var choice = ChooseDisplay(session, stop);
        if (!choice.Send)
        {
            return;
        }

        try
        {
            _destinationRoute = session.RouteName;
            var course = stops.Select(item => item.Name).ToList();
            IbisIpDienst.NoteStop(session, stop, choice.Destination);
            IbisIpDienst.NoteManual(choice.Line, choice.Destination);
            await DisplayTelegrams.SendAsync(session, choice.Line, choice.Destination, stop.Name, course);
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

        AnsageSequenz.PlayStop(session, stop);
        IbisIpDienst.NoteAnnouncement(stop.Name);
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
        _destinationRoute = null;
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

    private readonly record struct DisplayChoice(string Line, string Destination, bool Send);

    /// <summary>Gleiche Zielwahl wie die Android-App: Start sendet Linie und Ziel, Zielwechsel und Endhalt das Ziel.</summary>
    private static DisplayChoice ChooseDisplay(FahrSession session, RouteStopItem stop)
    {
        var protocol = session.OutsideProtocol ?? "";
        var start = DestinationName(protocol, stop, end: false);
        var end = DestinationName(protocol, stop, end: true);
        if (IsStartStop(stop) && start.Length > 0)
        {
            return new DisplayChoice(LineOf(session, stop), start, true);
        }

        if (stop.ZielwechselEnabled && start.Length > 0)
        {
            return new DisplayChoice(LineOf(session, stop), start, true);
        }

        if ((stop.IsEndStop || stop.PlayEndStopAnnouncement) && end.Length > 0)
        {
            return new DisplayChoice(LineOf(session, stop), end, true);
        }

        return new DisplayChoice("", "", false);
    }

    private static bool IsStartStop(RouteStopItem stop)
    {
        if (stop.ZielwechselEnabled)
        {
            return false;
        }

        return HasText(stop.Destination) || HasText(stop.Ds021NeuDestination) || HasText(stop.FmaS1Destination) ||
               HasText(stop.Ds003aDestination) || HasText(stop.ZielnummerDestination) || HasText(stop.MobitecDestination) ||
               HasText(stop.LineNumber);
    }

    private static string DestinationName(string protocol, RouteStopItem stop, bool end)
    {
        var name = protocol switch
        {
            "DS003a_Krefeld" => end ? stop.Ds003aEndDestination : stop.Ds003aDestination,
            "DS021neu" => end ? stop.Ds021NeuEndDestination : stop.Ds021NeuDestination,
            "FMA-S1" => end ? stop.FmaS1EndDestination : stop.FmaS1Destination,
            "DS003" or "Zielnummer" => end ? stop.ZielnummerEndDestination : stop.ZielnummerDestination,
            "Mobitec" => end ? stop.MobitecEndDestination : stop.MobitecDestination,
            _ => end ? stop.EndDestination : stop.Destination
        };
        if (!HasText(name) || name.Equals("Kein Ziel", StringComparison.OrdinalIgnoreCase))
        {
            if (protocol == "IBIS-IP")
            {
                name = end ? stop.MobitecEndDestination : stop.MobitecDestination;
            }
        }

        if (!HasText(name) || name.Equals("Kein Ziel", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return name.Trim();
    }

    private static string LineOf(FahrSession session, RouteStopItem stop)
    {
        if (HasText(stop.LineNumber))
        {
            return stop.LineNumber.Trim();
        }

        var parsed = RouteDisplayHelper.Parse(session.RouteName ?? "");
        var line = HasText(parsed.PassengerDisplayLine) ? parsed.PassengerDisplayLine : parsed.LineCourse;
        return line?.Trim() ?? "";
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

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

        var templates = session.Package.AnnouncementTemplates;
        if (StartStopGreetingResolver.Part1UsesSequencePlayback(templates))
        {
            PlayGreetingSequence(session, StartStopGreetingResolver.TryGetPart1Sequence(templates));
            Thread.Sleep(150);
        }
        else
        {
            var root = session.Package.PackageRoot;
            PlayNamed(session, root[StartStopGreetingResolver.Part1RootJsonFieldName]?.GetValue<string>());
        }

        var part2 = session.Package.PackageRoot[StartStopGreetingResolver.Part2RootJsonFieldName]?.GetValue<string>();
        PlayNamed(session, part2);
    }

    /// <summary>Doppeltipp auf Linie/Kurs: Linie, Nach, Ziel, nur rechts (außen).</summary>
    public void PlayOutsideDirection(FahrSession session)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            Status = "Keine Route für die Außenansage.";
            Changed?.Invoke();
            return;
        }

        _ = Task.Run(() =>
        {
            lock (PlayGate)
            {
                var voice2 = AnsageWahl.RouteUsesVoice2(session);
                var steps = new List<string?>
                {
                    LineSound(session),
                    PlanerNachSoundResolver.FileName,
                    EndDestinationSound(session)
                };
                var files = steps.Where(file => !string.IsNullOrWhiteSpace(file)).Cast<string>().ToList();
                if (files.Count == 0)
                {
                    Status = "Außenansage nicht verfügbar.";
                    Changed?.Invoke();
                    return;
                }

                FahrAnsage.Active = true;
                Changed?.Invoke();
                try
                {
                    for (var i = 0; i < files.Count; i++)
                    {
                        if (i > 0)
                        {
                            Thread.Sleep(150);
                        }

                        PlayNamed(session, files[i], forceVoice2: voice2, zone: AnnouncementAudioOutput.Outside);
                    }
                }
                finally
                {
                    FahrAnsage.Active = false;
                    Changed?.Invoke();
                }
            }
        });
    }

    /// <summary>Normale Bausteinlücke 150 ms. Direkt vor und nach der Linie 75 ms.</summary>
    private static void PlayGreetingSequence(FahrSession session, IReadOnlyList<AnnouncementSequenceEntry> sequence)
    {
        var voice2 = AnsageWahl.RouteUsesVoice2(session);
        var previousAudio = false;
        foreach (var entry in sequence)
        {
            if (entry.Kind == AnnouncementExportEntryKind.Pause)
            {
                continue;
            }

            var file = entry.Kind switch
            {
                AnnouncementExportEntryKind.Audio => entry.FileName,
                AnnouncementExportEntryKind.Line => LineSound(session),
                AnnouncementExportEntryKind.Nach => PlanerNachSoundResolver.FileName,
                AnnouncementExportEntryKind.RouteEndDestination => EndDestinationSound(session),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            if (entry.Kind == AnnouncementExportEntryKind.Line)
            {
                if (previousAudio)
                {
                    Thread.Sleep(75);
                }

                PlayNamed(session, file, forceVoice2: voice2);
                Thread.Sleep(75);
                previousAudio = false;
                continue;
            }

            if (previousAudio)
            {
                Thread.Sleep(150);
            }

            PlayNamed(session, file, forceVoice2: voice2);
            previousAudio = true;
        }
    }

    private static string? LineSound(FahrSession session)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            return null;
        }

        var parsed = RouteDisplayHelper.Parse(session.RouteName);
        var fromName = LineFromRouteName(parsed.Name);
        var passenger = string.IsNullOrWhiteSpace(parsed.PassengerDisplayLine) ? null : parsed.PassengerDisplayLine.Trim();
        var keys = new List<string>();
        if (fromName is not null && PreferNameLine(fromName, passenger))
        {
            keys.Add(fromName);
        }

        if (passenger is not null)
        {
            keys.Add(passenger);
        }

        if (fromName is not null)
        {
            keys.Add(fromName);
        }

        if (!string.IsNullOrWhiteSpace(parsed.LineCourse))
        {
            keys.Add(parsed.LineCourse);
        }

        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var template = session.Package.AnnouncementTemplates.FirstOrDefault(item =>
                string.Equals(item.DisplayName?.Trim(), key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.EmbeddedSoundFileName));
            if (template is not null)
            {
                return template.EmbeddedSoundFileName;
            }
        }

        return keys.FirstOrDefault();
    }

    private static readonly System.Text.RegularExpressions.Regex ProductLine =
        new(@"\b(IRE|ICE|IC|RE|RB|S|EC|NJ|EN|TGV|RJX|RJ)\s*(\d+)([A-Za-z])?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string? LineFromRouteName(string? routeName)
    {
        var trimmed = (routeName ?? "").Trim();
        var match = ProductLine.Match(trimmed);
        if (match.Success)
        {
            var raw = match.Value.Trim();
            var spaced = match.Groups[1].Value + " " + match.Groups[2].Value + match.Groups[3].Value;
            return raw.Contains(' ') ? spaced : match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value;
        }

        var token = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
        {
            return null;
        }

        return token.Any(char.IsLetter) && token.Any(char.IsDigit) ? token : null;
    }

    private static bool PreferNameLine(string fromName, string? passenger)
    {
        if (string.IsNullOrWhiteSpace(passenger))
        {
            return true;
        }

        var nameKey = fromName.Replace(" ", "");
        var passengerKey = passenger.Replace(" ", "");
        if (nameKey.Equals(passengerKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (nameKey.Length > passengerKey.Length &&
            nameKey.StartsWith(passengerKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return passengerKey.All(ch => char.IsDigit(ch) || ch == '/');
    }

    private static string? EndDestinationSound(FahrSession session)
    {
        var stops = Stops(session);
        for (var i = stops.Count - 1; i >= 0; i--)
        {
            if (stops[i].IsWaypoint)
            {
                continue;
            }

            var name = DestinationOf(stops[i]);
            return string.IsNullOrWhiteSpace(name) ? stops[i].Name : name;
        }

        return null;
    }

    private static void PlayNamed(
        FahrSession session,
        string? fileName,
        bool forceVoice2 = false,
        string zone = AnnouncementAudioOutput.Inside)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var voice2 = forceVoice2 || AnsageWahl.RouteUsesVoice2(session);
        var wanted = fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".wav";
        AnsageSequenz.PlayClip(session, wanted, voice2, zone);
    }

    /// <summary>Pas.-Info: bereits passierte Haltestellen gelten als erledigt, die nächste liegt voraus.</summary>
    public void AlignPas(FahrSession session)
    {
        if (!HasFix)
        {
            Status = "Pas.-Info wartet auf GPS.";
            Changed?.Invoke();
            return;
        }

        var stops = Stops(session);
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < stops.Count; i++)
        {
            if (stops[i].IsWaypoint)
            {
                continue;
            }

            var located = StopMatch.TryCoord(stops[i], out var slat, out var slon) ||
                          StopMatch.TryParse(stops[i].StopCoordinates, out slat, out slon) ||
                          StopMatch.TryParse(stops[i].GpsCoordinates, out slat, out slon);
            if (!located)
            {
                continue;
            }

            var distance = StopMatch.Meters(LastLat, LastLon, slat, slon);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return;
        }

        if (!Towards(session, LastLat, LastLon, stops[best]) && best + 1 < stops.Count)
        {
            best++;
        }

        _played.Clear();
        for (var i = 0; i < best; i++)
        {
            _played.Add(i);
        }

        NextIndex = best;
        Status = $"Pas.-Info: {stops[best].Name}";
        GpsSpur.Event(session, "pas_on", stops[best].Name);
        Changed?.Invoke();
    }

    public void Simulate(FahrSession session)
    {
        if (_simulating)
        {
            return;
        }

        var shape = FahrKarte.Shape(session);
        var total = FahrKarte.Length(shape);
        if (shape.Count < 2 || total < 10)
        {
            Status = "Kein Fahrweg für diese Route.";
            Changed?.Invoke();
            return;
        }

        _simulating = true;
        const double speed = 50.0 / 3.6;
        _ = Task.Run(async () =>
        {
            try
            {
                var along = 0.0;
                while (_simulating && along <= total)
                {
                    var (lat, lon) = FahrKarte.At(shape, along);
                    OnFix(session, lat, lon);
                    along += speed * 0.5;
                    await Task.Delay(500);
                }
            }
            finally
            {
                _simulating = false;
            }
        });
    }

    public void StopSimulation() => _simulating = false;

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
