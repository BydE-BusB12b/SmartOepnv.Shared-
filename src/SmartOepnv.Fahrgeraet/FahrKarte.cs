using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Linienbild der gewählten Fahrt. Der Punkt folgt der Haltestellenfolge,
/// nicht einem Kartenkachel-Dienst.
/// </summary>
public static class FahrKarte
{
    public static Canvas Create() => new()
    {
        Width = 640,
        Height = 220,
        Background = new SolidColorBrush(Color.Parse("#1565C0"))
    };

    public static void Update(Canvas canvas, FahrSession session)
    {
        canvas.Children.Clear();
        var stops = session.Package?.GetStops(session.RouteName ?? "")?.ToList() ?? [];
        var points = new List<(double Lat, double Lon)>();
        foreach (var stop in stops)
        {
            if (StopMatch.TryCoord(stop, out var lat, out var lon) ||
                StopMatch.TryParse(stop.GpsCoordinates, out lat, out lon))
            {
                points.Add((lat, lon));
            }
        }

        if (points.Count == 0)
        {
            return;
        }

        var minLat = points.Min(point => point.Lat);
        var maxLat = points.Max(point => point.Lat);
        var minLon = points.Min(point => point.Lon);
        var maxLon = points.Max(point => point.Lon);
        var latSpan = Math.Max(0.0001, maxLat - minLat);
        var lonSpan = Math.Max(0.0001, maxLon - minLon);
        Avalonia.Point Place(double lat, double lon) => new(
            16 + (lon - minLon) / lonSpan * (canvas.Width - 32),
            canvas.Height - 16 - (lat - minLat) / latSpan * (canvas.Height - 32));

        var drawn = new Avalonia.Points();
        foreach (var point in points)
        {
            drawn.Add(Place(point.Lat, point.Lon));
        }

        var line = new Polyline
        {
            Stroke = Brushes.White,
            StrokeThickness = 3,
            Points = drawn
        };
        canvas.Children.Add(line);
        if (!session.Trip.HasFix)
        {
            return;
        }

        var (dotLat, dotLon) = Follow(session, points);
        var here = Place(dotLat, dotLon);
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = Brushes.OrangeRed
        };
        Canvas.SetLeft(dot, here.X - 7);
        Canvas.SetTop(dot, here.Y - 7);
        canvas.Children.Add(dot);
    }

    public static readonly string[] Symbols =
    [
        "Geradeaus",
        "Links abbiegen",
        "Rechts abbiegen",
        "T-Kreuzung links",
        "T-Kreuzung rechts",
        "Vorfahrtsstraße folgen",
        "Autobahn-Ausfahrt",
        "Kreisverkehr",
        "U-Turn",
        "Linienweg verlassen"
    ];

    public static string Instruction(FahrSession session)
    {
        var shape = Shape(session);
        if (shape.Count < 2 || !session.Trip.HasFix)
        {
            return Sprache.T("Navigation wartet auf den Fahrweg.", "Navigatie wacht op de route.");
        }

        var along = session.Trip.HasShownAlong ? session.Trip.ShownAlong : Project(shape, session.Trip.LastLat, session.Trip.LastLon);
        var total = Length(shape);
        var remain = Math.Max(0, total - along);
        var turn = Turn(shape, along);
        return $"{turn} · {remain:0} m";
    }

    public static List<(double Lat, double Lon)> Shape(FahrSession session)
    {
        var points = new List<(double Lat, double Lon)>();
        foreach (var stop in session.Package?.GetStops(session.RouteName ?? "") ?? [])
        {
            if (StopMatch.TryCoord(stop, out var lat, out var lon) ||
                StopMatch.TryParse(stop.GpsCoordinates, out lat, out lon))
            {
                points.Add((lat, lon));
            }
        }

        return points;
    }

    public static double Length(IReadOnlyList<(double Lat, double Lon)> points)
    {
        var sum = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            sum += StopMatch.Meters(points[i - 1].Lat, points[i - 1].Lon, points[i].Lat, points[i].Lon);
        }

        return sum;
    }

    public static (double Lat, double Lon) At(IReadOnlyList<(double Lat, double Lon)> points, double meters)
    {
        if (points.Count == 0)
        {
            return (0, 0);
        }

        var left = Math.Max(0, meters);
        for (var i = 1; i < points.Count; i++)
        {
            var span = StopMatch.Meters(points[i - 1].Lat, points[i - 1].Lon, points[i].Lat, points[i].Lon);
            if (left <= span || i == points.Count - 1)
            {
                var t = span <= 0 ? 0 : Math.Clamp(left / span, 0, 1);
                return (points[i - 1].Lat + (points[i].Lat - points[i - 1].Lat) * t,
                    points[i - 1].Lon + (points[i].Lon - points[i - 1].Lon) * t);
            }

            left -= span;
        }

        return points[^1];
    }

    private static (double Lat, double Lon) Follow(FahrSession session, List<(double Lat, double Lon)> points)
    {
        var total = Length(points);
        var snap = Project(points, session.Trip.LastLat, session.Trip.LastLon);
        var since = session.Trip.LastFixUtc == DateTime.MinValue
            ? 0
            : (DateTime.UtcNow - session.Trip.LastFixUtc).TotalSeconds;
        var shown = session.Trip.HasShownAlong ? session.Trip.ShownAlong : snap;
        var next = Advance(shown, snap, session.Trip.LastSpeedMps, Math.Clamp(since, 0, 0.25), since, total);
        session.Trip.ShownAlong = next;
        session.Trip.HasShownAlong = true;
        return At(points, next);
    }

    private static double Advance(double shown, double snapped, double speedMps, double dtSec, double sinceFixSec, double total)
    {
        var snap = Math.Clamp(snapped, 0, total);
        var moving = speedMps > 0.3;
        if (!moving)
        {
            return Math.Abs(snap - shown) <= 20 ? snap : Math.Clamp(shown, 0, total);
        }

        if (snap < shown)
        {
            var crept = sinceFixSec < 4 ? shown + speedMps * dtSec : shown;
            return Math.Clamp(crept, 0, total);
        }

        if (Math.Abs(snap - shown) > 500)
        {
            return snap;
        }

        var along = shown;
        if (sinceFixSec < 4)
        {
            along += speedMps * dtSec;
        }

        var ahead = snap - along;
        if (ahead > 0.5)
        {
            var catchUp = Math.Max(Math.Max(0, speedMps) * dtSec, ahead * (dtSec / 0.45));
            along += Math.Min(ahead, catchUp);
        }

        return Math.Clamp(along, 0, total);
    }

    private static double Project(IReadOnlyList<(double Lat, double Lon)> points, double lat, double lon)
    {
        var best = 0.0;
        var bestDistance = double.MaxValue;
        var walked = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var span = StopMatch.Meters(a.Lat, a.Lon, b.Lat, b.Lon);
            var vx = b.Lon - a.Lon;
            var vy = b.Lat - a.Lat;
            var length = vx * vx + vy * vy;
            var t = length <= 0 ? 0 : Math.Clamp(((lon - a.Lon) * vx + (lat - a.Lat) * vy) / length, 0, 1);
            var plat = a.Lat + vy * t;
            var plon = a.Lon + vx * t;
            var distance = StopMatch.Meters(lat, lon, plat, plon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = walked + span * t;
            }

            walked += span;
        }

        return best;
    }

    private static string Turn(IReadOnlyList<(double Lat, double Lon)> points, double along)
    {
        var here = At(points, along);
        var next = At(points, along + 40);
        var after = At(points, along + 120);
        var first = Bearing(here, next);
        var second = Bearing(next, after);
        var delta = second - first;
        while (delta > 180) delta -= 360;
        while (delta < -180) delta += 360;
        if (delta > 135 || delta < -135) return "U-Turn";
        if (delta > 25) return "Rechts abbiegen";
        if (delta < -25) return "Links abbiegen";
        return "Geradeaus";
    }

    private static double Bearing((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        var y = Math.Sin(Rad(b.Lon - a.Lon)) * Math.Cos(Rad(b.Lat));
        var x = Math.Cos(Rad(a.Lat)) * Math.Sin(Rad(b.Lat)) -
                Math.Sin(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Cos(Rad(b.Lon - a.Lon));
        return Math.Atan2(y, x) * 180 / Math.PI;
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;
}
