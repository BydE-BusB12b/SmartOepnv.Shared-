using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Auftragsnummer und fortlaufende Nummer einer Fahrt. Verknüpfte Fahrten einer
/// Routenschnur teilen sich beide Werte.
/// </summary>
public readonly record struct RouteChainIdentity(string OrderNumber, int SequenceNumber)
{
    public static RouteChainIdentity Empty { get; } = new(string.Empty, 0);

    public bool HasValue =>
        SequenceNumber > 0 || !string.IsNullOrWhiteSpace(OrderNumber);
}

public static class RouteChainIdentityEditor
{
    public const string RootFieldName = "routeChainIdentities";
    private const string OrderFieldName = "orderNumber";
    private const string SequenceFieldName = "sequenceNumber";

    public static Dictionary<string, RouteChainIdentity> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, RouteChainIdentity>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is not JsonObject item)
            {
                continue;
            }

            var order = item[OrderFieldName]?.GetValue<string>()?.Trim() ?? string.Empty;
            var sequence = 0;
            if (item[SequenceFieldName] is JsonValue sequenceNode &&
                sequenceNode.TryGetValue<int>(out var parsed) &&
                parsed > 0)
            {
                sequence = parsed;
            }

            if (sequence <= 0 && string.IsNullOrEmpty(order))
            {
                continue;
            }

            result[entry.Key.Trim()] = new RouteChainIdentity(order, sequence);
        }

        return result;
    }

    public static RouteChainIdentity GetForRoute(
        IDictionary<string, RouteChainIdentity> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var identity))
        {
            return identity;
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out identity))
        {
            return identity;
        }

        return RouteChainIdentity.Empty;
    }

    public static void SetForRoute(
        IDictionary<string, RouteChainIdentity> map,
        string routeDisplayKey,
        string? orderNumber,
        int sequenceNumber)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(routeDisplayKey.Trim());
        var order = orderNumber?.Trim() ?? string.Empty;
        if (sequenceNumber < 0)
        {
            sequenceNumber = 0;
        }

        if (sequenceNumber <= 0 && string.IsNullOrEmpty(order))
        {
            map.Remove(key);
            return;
        }

        map[key] = new RouteChainIdentity(order, sequenceNumber);
    }

    public static void RemoveRoute(IDictionary<string, RouteChainIdentity> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
    }

    public static void RenameRouteKey(
        IDictionary<string, RouteChainIdentity> map,
        string oldRouteDisplayKey,
        string newRouteDisplayKey)
    {
        var identity = GetForRoute(map, oldRouteDisplayKey);
        RemoveRoute(map, oldRouteDisplayKey);
        if (!identity.HasValue)
        {
            return;
        }

        SetForRoute(map, newRouteDisplayKey, identity.OrderNumber, identity.SequenceNumber);
    }

    public static string FormatSequenceNumber(int sequenceNumber) =>
        sequenceNumber > 0
            ? sequenceNumber.ToString("00000", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    public static int PeekNextSequenceNumber(IDictionary<string, RouteChainIdentity> map)
    {
        var max = 0;
        foreach (var identity in map.Values)
        {
            if (identity.SequenceNumber > max)
            {
                max = identity.SequenceNumber;
            }
        }

        return max + 1;
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, RouteChainIdentity> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!entry.Value.HasValue)
            {
                continue;
            }

            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key))
            {
                continue;
            }

            var item = new JsonObject();
            if (!string.IsNullOrEmpty(entry.Value.OrderNumber))
            {
                item[OrderFieldName] = entry.Value.OrderNumber;
            }

            if (entry.Value.SequenceNumber > 0)
            {
                item[SequenceFieldName] = entry.Value.SequenceNumber;
            }

            obj[key] = item;
        }

        if (obj.Count == 0)
        {
            root.Remove(RootFieldName);
        }
        else
        {
            root[RootFieldName] = obj;
        }
    }
}
