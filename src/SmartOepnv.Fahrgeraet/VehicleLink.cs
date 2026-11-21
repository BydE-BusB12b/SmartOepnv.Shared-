using System.IO.Ports;
using System.Text;
using SmartOepnv.Core.RoutePackage;
namespace SmartOepnv.Fahrgeraet;

public static class VehicleLink
{
    public static void Send(
        string? portName,
        byte[] bytes,
        int baud,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("Kein serieller Anschluss eingetragen.");
        }

        using var port = new SerialPort(portName.Trim(), baud, parity, dataBits, stopBits)
        {
            WriteTimeout = 2000
        };
        port.Open();
        port.Write(bytes, 0, bytes.Length);
    }
}

public sealed class GpsNmea : IDisposable
{
    private SerialPort? _port;
    private readonly StringBuilder _buffer = new();

    public event Action<double, double>? Fix;
    public int? AccuracyMeters { get; private set; }

    public void Start(string portName)
    {
        Stop();
        _port = new SerialPort(portName.Trim(), 9600, Parity.None, 8, StopBits.One);
        _port.DataReceived += OnData;
        _port.Open();
    }

    public void Stop()
    {
        if (_port is null)
        {
            return;
        }

        _port.DataReceived -= OnData;
        if (_port.IsOpen)
        {
            _port.Close();
        }

        _port.Dispose();
        _port = null;
    }

    public void Dispose() => Stop();

    private void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var chunk = _port?.ReadExisting();
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _buffer.Append(chunk);
            var text = _buffer.ToString();
            var lines = text.Split('\n');
            _buffer.Clear();
            if (!text.EndsWith('\n'))
            {
                _buffer.Append(lines[^1]);
            }

            foreach (var line in lines)
            {
                if (TryParse(line.Trim(), out var lat, out var lon, out var accuracy))
                {
                    AccuracyMeters = accuracy;
                    Fix?.Invoke(lat, lon);
                }
            }
        }
        catch
        {
            // nächster Satz
        }
    }

    public static bool TryParse(string sentence, out double lat, out double lon) =>
        TryParse(sentence, out lat, out lon, out _);

    public static bool TryParse(string sentence, out double lat, out double lon, out int? accuracyMeters)
    {
        lat = lon = 0;
        accuracyMeters = null;
        if (!sentence.StartsWith("$G") || sentence.Length < 6)
        {
            return false;
        }

        var star = sentence.IndexOf('*');
        var body = star > 0 ? sentence[..star] : sentence;
        var parts = body.Split(',');
        var type = parts[0].Length >= 6 ? parts[0][3..] : "";
        if (type is "RMC" && parts.Length > 6 && parts[2] == "A")
        {
            return TryDeg(parts[3], parts[4], out lat) && TryDeg(parts[5], parts[6], out lon);
        }

        if (type is "GGA" && parts.Length > 5 && parts[6] != "0")
        {
            if (parts.Length > 8 &&
                double.TryParse(parts[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hdop) &&
                hdop > 0)
            {
                accuracyMeters = (int)Math.Round(hdop * 5);
            }

            return TryDeg(parts[2], parts[3], out lat) && TryDeg(parts[4], parts[5], out lon);
        }

        return false;
    }

    private static bool TryDeg(string raw, string hemi, out double degrees)
    {
        degrees = 0;
        if (raw.Length < 4 || !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nmea))
        {
            return false;
        }

        var deg = Math.Floor(nmea / 100);
        degrees = deg + (nmea - deg * 100) / 60;
        if (hemi is "S" or "W")
        {
            degrees = -degrees;
        }

        return true;
    }
}

public static class StopMatch
{
    public static (RouteStopItem Stop, double Meters)? Nearest(IEnumerable<RouteStopItem> stops, double lat, double lon)
    {
        RouteStopItem? best = null;
        var bestMeters = double.MaxValue;
        foreach (var stop in stops)
        {
            if (!TryCoord(stop, out var slat, out var slon))
            {
                continue;
            }

            var meters = Meters(lat, lon, slat, slon);
            if (meters < bestMeters)
            {
                bestMeters = meters;
                best = stop;
            }
        }

        return best is null ? null : (best, bestMeters);
    }

    public static bool TryCoord(RouteStopItem stop, out double lat, out double lon)
    {
        lat = lon = 0;
        var raw = string.IsNullOrWhiteSpace(stop.GpsCoordinates) ? stop.StopCoordinates : stop.GpsCoordinates;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
               && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat)
               && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon);
    }

    public static bool TryParse(string? raw, out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
               && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat)
               && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon);
    }

    public static double Meters(double lat1, double lon1, double lat2, double lon2) =>
        Haversine(lat1, lon1, lat2, lon2);

    public static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = lat1 * Math.PI / 180;
        var p2 = lat2 * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var y = Math.Sin(dLon) * Math.Cos(p2);
        var x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
