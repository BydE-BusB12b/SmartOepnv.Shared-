using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Standort wie auf dem Tablet: ab der Fahreranmeldung, auch ohne gewählte Fahrt.
/// Admin 1995 sendet nicht. Die GPS-Quelle bleibt offen, bis sich der Fahrer abmeldet.
/// </summary>
public static class StandortSend
{
    private static FahrSession? _session;
    private static bool _hooked;
    private static long _lastMs;

    public static void Start(FahrSession session)
    {
        _session = session;
        if (!_hooked)
        {
            _hooked = true;
            GpsQuelle.Fix += OnFix;
        }

        Open(session);
    }

    public static void Refresh(FahrSession session)
    {
        if (_session is null)
        {
            return;
        }

        _session = session;
        Open(session);
    }

    public static void Stop()
    {
        if (_hooked)
        {
            GpsQuelle.Fix -= OnFix;
            _hooked = false;
        }

        GpsQuelle.Drop("standort");
        _session = null;
        _lastMs = 0;
    }

    private static void Open(FahrSession session)
    {
        try
        {
            GpsQuelle.Hold("standort", session.GpsPort);
            if (string.IsNullOrWhiteSpace(session.GpsPort))
            {
                session.Status?.Invoke("Standort: GPS-Anschluss fehlt.");
            }
        }
        catch (Exception ex)
        {
            session.Status?.Invoke(ex.Message);
        }
    }

    private static void OnFix(double lat, double lon)
    {
        var session = _session;
        var employee = session?.Employee;
        if (session is null || employee is null || BuiltinAdminEmployee.IsBuiltinAdmin(employee))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastMs < 15000)
        {
            return;
        }

        _lastMs = now;
        var phone = new string((session.DevicePhone ?? "").Where(char.IsDigit).ToArray());
        if (phone.Length == 0)
        {
            return;
        }

        var stops = Stops(session);
        var next = session.Trip.Running && session.Trip.NextIndex is int index && index < stops.Count
            ? stops[index].Name
            : "";
        var route = session.RouteName ?? "";
        var body = new JsonObject
        {
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["accuracy"] = 15,
            ["speed"] = 0,
            ["phoneNumber"] = phone,
            ["userName"] = employee.Name ?? "",
            ["driverName"] = employee.Name ?? "",
            ["driverPersonnelNumber"] = employee.PersonnelNumber ?? "",
            ["timestamp"] = now,
            ["route"] = route,
            ["lineCourse"] = route.Length == 0 ? "000/00" : "",
            ["stop"] = next,
            ["destination"] = ""
        };
        var fms = FmsLive.PlannerObject();
        if (fms is not null)
        {
            body["fms"] = fms;
        }

        var payload = body.ToJsonString();
        var file = JsonSerializer.Serialize(new
        {
            phoneNumber = phone,
            userName = employee.Name ?? "",
            timestamp = now,
            message = LocationChatCrypto.EncryptLocationUpdateMessage(payload)
        });
        _ = UploadAsync(session, phone, file);
    }

    private static IReadOnlyList<RouteStopItem> Stops(FahrSession session)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            return [];
        }

        return session.Package.GetStops(session.RouteName).ToList();
    }

    private static async Task UploadAsync(FahrSession session, string phone, string json)
    {
        try
        {
            var client = FahrbetriebPages.OpenDropbox();
            if (client is null)
            {
                return;
            }

            await client.UploadNamedFileAsync($"location_chat_{phone}.json", json);
        }
        catch (Exception ex)
        {
            session.Status?.Invoke(ex.Message);
        }
    }
}

internal static class GpsQuelle
{
    private static readonly HashSet<string> Owners = new(StringComparer.Ordinal);
    private static GpsNmea? _nmea;
    private static string? _port;

    public static event Action<double, double>? Fix;

    public static void Hold(string owner, string? portName)
    {
        Owners.Add(owner);
        if (string.IsNullOrWhiteSpace(portName))
        {
            return;
        }

        var port = portName.Trim();
        if (_nmea is not null && string.Equals(_port, port, StringComparison.Ordinal))
        {
            return;
        }

        _nmea?.Dispose();
        _nmea = new GpsNmea();
        _port = port;
        _nmea.Fix += (lat, lon) => Fix?.Invoke(lat, lon);
        _nmea.Start(port);
    }

    public static void Drop(string owner)
    {
        Owners.Remove(owner);
        if (Owners.Count > 0)
        {
            return;
        }

        _nmea?.Dispose();
        _nmea = null;
        _port = null;
    }
}
