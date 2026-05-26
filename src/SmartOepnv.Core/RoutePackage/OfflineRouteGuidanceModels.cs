using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public sealed record OfflineGuidancePoint(double Lat, double Lng);

public sealed record OfflineGuidanceManeuver(string Kind, string Text, double DistanceFromStartMeters);

public sealed class OfflineRouteGuidance
{
    public int Version { get; init; } = 1;
    public string RouteKey { get; init; } = string.Empty;
    public IList<OfflineGuidancePoint> Polyline { get; init; } = [];
    public IList<OfflineGuidanceManeuver> Maneuvers { get; init; } = [];
    public double TotalLengthMeters { get; init; }
}

public static class OfflineRouteGuidanceJson
{
    public static string ToJson(OfflineRouteGuidance g)
    {
        var root = new JsonObject
        {
            ["v"] = g.Version,
            ["route"] = g.RouteKey,
            ["totalM"] = g.TotalLengthMeters
        };
        var poly = new JsonArray();
        foreach (var p in g.Polyline)
        {
            poly.Add(new JsonObject { ["lat"] = p.Lat, ["lng"] = p.Lng });
        }

        root["poly"] = poly;
        var man = new JsonArray();
        foreach (var m in g.Maneuvers)
        {
            man.Add(new JsonObject
            {
                ["k"] = m.Kind,
                ["t"] = m.Text,
                ["d"] = m.DistanceFromStartMeters
            });
        }

        root["man"] = man;
        return root.ToJsonString();
    }

    public static OfflineRouteGuidance? FromJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null)
            {
                return null;
            }

            var route = JsonNodeReading.GetString(root["route"]);
            var poly = new List<OfflineGuidancePoint>();
            if (root["poly"] is JsonArray polyArr)
            {
                foreach (var node in polyArr)
                {
                    if (node is JsonObject o)
                    {
                        poly.Add(new OfflineGuidancePoint(
                            JsonNodeReading.GetDouble(o["lat"]),
                            JsonNodeReading.GetDouble(o["lng"])));
                    }
                }
            }

            var maneuvers = new List<OfflineGuidanceManeuver>();
            if (root["man"] is JsonArray manArr)
            {
                foreach (var node in manArr)
                {
                    if (node is JsonObject o)
                    {
                        maneuvers.Add(new OfflineGuidanceManeuver(
                            JsonNodeReading.GetString(o["k"]),
                            JsonNodeReading.GetString(o["t"]),
                            JsonNodeReading.GetDouble(o["d"])));
                    }
                }
            }

            return new OfflineRouteGuidance
            {
                Version = JsonNodeReading.GetInt32(root["v"], 1),
                RouteKey = route,
                Polyline = poly,
                Maneuvers = maneuvers,
                TotalLengthMeters = JsonNodeReading.GetDouble(root["totalM"])
            };
        }
        catch
        {
            return null;
        }
    }
}

public static class OfflineRouteGuidancePackageSync
{
    public static void Save(JsonObject root, string routeKey, OfflineRouteGuidance? guidance)
    {
        if (root["routeOfflineGuidance"] is not JsonObject block)
        {
            block = new JsonObject();
            root["routeOfflineGuidance"] = block;
        }

        if (guidance is null)
        {
            block.Remove(routeKey);
            if (block.Count == 0)
            {
                root.Remove("routeOfflineGuidance");
            }

            return;
        }

        block[routeKey] = JsonValue.Create(OfflineRouteGuidanceJson.ToJson(guidance));
    }

    public static bool HasGuidance(JsonObject root, string routeKey) =>
        root["routeOfflineGuidance"] is JsonObject block && block[routeKey] is not null;
}
