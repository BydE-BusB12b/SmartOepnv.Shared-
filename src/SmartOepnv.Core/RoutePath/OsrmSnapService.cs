using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePath;

public sealed class OsrmSnapService
{
    private const string OsrmHost = "router.project-osrm.org";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    public async Task<OsrmSnapResult> SnapSegmentAsync(RoutePathLatLng from, RoutePathLatLng to, CancellationToken ct = default)
        => await SnapPathAsync([from, to], ct);

    public async Task<OsrmSnapResult> SnapPathAsync(IReadOnlyList<RoutePathLatLng> path, CancellationToken ct = default)
    {
        var clean = path
            .Where(p => double.IsFinite(p.Lat) && double.IsFinite(p.Lon))
            .ToList();
        if (clean.Count < 2)
        {
            return OsrmSnapResult.Failed(clean, "Mindestens zwei gültige Koordinaten nötig.");
        }

        var coord = string.Join(";", clean.Select(p =>
            $"{p.Lon.ToString(CultureInfo.InvariantCulture)},{p.Lat.ToString(CultureInfo.InvariantCulture)}"));
        var radiuses = string.Join(";", Enumerable.Repeat("unlimited", clean.Count));
        var url =
            $"https://{OsrmHost}/route/v1/driving/{coord.Replace(";", "%3B")}" +
            $"?overview=full&geometries=geojson&steps=true&continue_straight=true&alternatives=false&radiuses={Uri.EscapeDataString(radiuses)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Smart-OEPNV-Planer OSRM-Client");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return OsrmSnapResult.Failed(clean,
                    $"OSRM HTTP {(int)response.StatusCode}: {TrimOsrmError(body)}");
            }

            return ParseOsrm(body, clean);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OsrmSnapResult.Failed(clean, $"OSRM nicht erreichbar: {ex.Message}");
        }
    }

    private static string TrimOsrmError(string body)
    {
        var t = body.Replace('\n', ' ').Trim();
        return t.Length > 180 ? t[..180] + "…" : t;
    }

    private static OsrmSnapResult ParseOsrm(string json, IReadOnlyList<RoutePathLatLng> endpoints)
    {
        if (json.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            return OsrmSnapResult.Failed(endpoints, "OSRM lieferte HTML statt JSON (Netzwerk/Proxy?).");
        }

        var root = JsonNode.Parse(json)?.AsObject();
        var code = root?["code"]?.GetValue<string>();
        if (!string.Equals(code, "Ok", StringComparison.OrdinalIgnoreCase))
        {
            var msg = root?["message"]?.GetValue<string>() ?? code ?? "Unbekannt";
            return OsrmSnapResult.Failed(endpoints, $"OSRM: {msg}");
        }

        var route = root!["routes"]?.AsArray()?.FirstOrDefault()?.AsObject();
        if (route is null)
        {
            return OsrmSnapResult.Failed(endpoints, "OSRM: keine Route in der Antwort.");
        }

        var points = ParseRouteGeometry(route);
        if (points.Count < 2)
        {
            return OsrmSnapResult.Failed(endpoints, "OSRM: keine Straßengeometrie (Polyline leer).");
        }

        var maneuvers = ParseManeuvers(route);
        return new OsrmSnapResult(points, maneuvers, IsRoadRoute: true, Error: null);
    }

    private static List<RoutePathLatLng> ParseRouteGeometry(JsonObject route)
    {
        var geometry = route["geometry"];
        if (geometry is JsonObject geoObj)
        {
            return ParseCoordinateArray(geoObj["coordinates"]?.AsArray());
        }

        if (geometry is JsonValue geoVal)
        {
            var encoded = geoVal.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(encoded))
            {
                return OsrmPolylineDecoder.Decode(encoded, precision: 5);
            }
        }

        return [];
    }

    private static List<RoutePathLatLng> ParseCoordinateArray(JsonArray? coordinates)
    {
        var points = new List<RoutePathLatLng>();
        if (coordinates is null) return points;
        foreach (var pair in coordinates)
        {
            if (pair is not JsonArray c || c.Count < 2) continue;
            var lon = c[0]?.GetValue<double>() ?? double.NaN;
            var lat = c[1]?.GetValue<double>() ?? double.NaN;
            if (double.IsFinite(lat) && double.IsFinite(lon))
            {
                points.Add(new RoutePathLatLng { Lat = lat, Lon = lon });
            }
        }
        return points;
    }

    private static List<RoutePathSnapManeuver> ParseManeuvers(JsonObject route)
    {
        var maneuvers = new List<RoutePathSnapManeuver>();
        double cumulative = 0;
        string? previousStreet = null;
        var legs = route["legs"]?.AsArray();
        if (legs is null) return maneuvers;

        foreach (var leg in legs.OfType<JsonObject>())
        {
            var steps = leg["steps"]?.AsArray();
            if (steps is null) continue;
            foreach (var step in steps.OfType<JsonObject>())
            {
                var stepStreet = step["name"]?.GetValue<string>()?.Trim();
                var maneuver = step["maneuver"]?.AsObject();
                var type = maneuver?["type"]?.GetValue<string>() ?? string.Empty;
                var modifier = maneuver?["modifier"]?.GetValue<string>() ?? string.Empty;
                var instruction = BuildInstruction(type, modifier, maneuver?["exit"]?.GetValue<int>() ?? -1);
                if (instruction is not null)
                {
                    maneuvers.Add(new RoutePathSnapManeuver
                    {
                        DistanceM = cumulative,
                        Instruction = instruction,
                        CurrentStreet = previousStreet,
                        NextStreet = string.IsNullOrWhiteSpace(stepStreet) ? null : stepStreet,
                        NavSymbolType = MapNavSymbol(type, modifier)
                    });
                }
                cumulative += Math.Max(0, step["distance"]?.GetValue<double>() ?? 0);
                if (!string.IsNullOrWhiteSpace(stepStreet)) previousStreet = stepStreet;
            }
        }

        return maneuvers;
    }

    private static string? BuildInstruction(string type, string modifier, int exit)
    {
        var t = type.ToLowerInvariant();
        var m = modifier.ToLowerInvariant();
        return t switch
        {
            "depart" => "Start",
            "arrive" => "Ziel",
            "roundabout" when exit > 0 => $"{exit}. Ausfahrt Kreisverkehr",
            "roundabout" => "Kreisverkehr",
            "turn" or "new name" or "merge" or "on ramp" or "off ramp" or "fork" or "end of road" => m switch
            {
                "left" => "Links abbiegen",
                "right" => "Rechts abbiegen",
                "slight left" => "Leicht links",
                "slight right" => "Leicht rechts",
                "sharp left" => "Scharf links",
                "sharp right" => "Scharf rechts",
                "uturn" => "Wenden",
                _ => "Geradeaus"
            },
            _ => null
        };
    }

    private static string MapNavSymbol(string type, string modifier)
    {
        var m = modifier.ToLowerInvariant();
        if (type.Equals("roundabout", StringComparison.OrdinalIgnoreCase))
        {
            return m.Contains('5') ? "roundabout_2_5" : "roundabout_2_4";
        }
        return m switch
        {
            "left" => "left",
            "right" => "right",
            "slight left" => "slight_left",
            "slight right" => "slight_right",
            "uturn" => "u_turn_custom",
            "straight" => "straight",
            _ => "straight"
        };
    }
}

public sealed record OsrmSnapResult(
    IReadOnlyList<RoutePathLatLng> Points,
    IReadOnlyList<RoutePathSnapManeuver> Maneuvers,
    bool IsRoadRoute,
    string? Error)
{
    public static OsrmSnapResult Failed(IReadOnlyList<RoutePathLatLng> endpoints, string error) =>
        new(endpoints, [], false, error);
}

internal static class OsrmPolylineDecoder
{
    public static List<RoutePathLatLng> Decode(string encoded, int precision)
    {
        if (string.IsNullOrEmpty(encoded)) return [];
        var factor = Math.Pow(10, precision);
        var index = 0;
        var lat = 0;
        var lng = 0;
        var outList = new List<RoutePathLatLng>();
        while (index < encoded.Length)
        {
            var result = 0;
            var shift = 0;
            int b;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index < encoded.Length);

            var dLat = (result & 1) != 0 ? ~(result >> 1) : result >> 1;
            lat += dLat;

            result = 0;
            shift = 0;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index < encoded.Length);

            var dLng = (result & 1) != 0 ? ~(result >> 1) : result >> 1;
            lng += dLng;

            outList.Add(new RoutePathLatLng
            {
                Lat = lat / factor,
                Lon = lng / factor
            });
        }
        return outList;
    }
}
