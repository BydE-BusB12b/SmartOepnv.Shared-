namespace SmartOepnv.Core.RoutePackage;

/// <summary>Einheitliche Routenschlüssel – verhindert Duplikate mit/ohne Verkehrstags-Kennung.</summary>
public static class RoutePackageRouteKeyHelper
{
    public static bool IsRouteKeyAllowed(string key, IEnumerable<string> allowedRouteKeys)
    {
        foreach (var allowed in allowedRouteKeys)
        {
            if (RouteDisplayHelper.RouteKeysMatch(allowed, key))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<string> DistinctCanonicalKeys(IEnumerable<string> routeKeys) =>
        routeKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(RouteDisplayHelper.ToCanonicalRouteKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static string SelectPrimaryDisplayKey(
        IReadOnlyList<string> aliases,
        IDictionary<string, IList<RouteStopItem>> stopsByRoute)
    {
        if (aliases.Count == 0)
        {
            return string.Empty;
        }

        if (aliases.Count == 1)
        {
            return aliases[0];
        }

        var withStops = aliases
            .Where(alias => stopsByRoute.TryGetValue(alias, out var stops) && stops.Count > 0)
            .ToList();
        if (withStops.Count > 0)
        {
            return withStops.FirstOrDefault(ContainsOperatingDaySuffix) ?? withStops[0];
        }

        return aliases.FirstOrDefault(ContainsOperatingDaySuffix) ?? aliases[0];
    }

    private static bool ContainsOperatingDaySuffix(string routeKey) =>
        routeKey.Contains("Verkehr:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Liefert den StopsByRoute-Schlüssel mit den meisten Haltestellen (bei Alias-Duplikaten).
    /// </summary>
    public static string? ResolveRouteKeyWithStops(
        string routeKey,
        IDictionary<string, IList<RouteStopItem>> stopsByRoute)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return null;
        }

        var trimmed = routeKey.Trim();
        var bestMatch = stopsByRoute
            .Where(pair => RouteDisplayHelper.RouteKeysMatch(pair.Key, trimmed))
            .Select(pair => (Key: pair.Key, Count: pair.Value.Count))
            .OrderByDescending(pair => pair.Count)
            .ThenByDescending(pair => ContainsOperatingDaySuffix(pair.Key) ? 1 : 0)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(bestMatch.Key))
        {
            return bestMatch.Key;
        }

        if (stopsByRoute.ContainsKey(trimmed))
        {
            return trimmed;
        }

        var canonical = RouteDisplayHelper.ToCanonicalRouteKey(trimmed);
        return stopsByRoute.ContainsKey(canonical) ? canonical : null;
    }
}
