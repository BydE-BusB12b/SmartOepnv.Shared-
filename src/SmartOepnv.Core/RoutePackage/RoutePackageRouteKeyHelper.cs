namespace SmartOepnv.Core.RoutePackage;

/// <summary>Einheitliche Routenschlüssel – Alias mit/ohne Verkehrstags-Kennung, getrennte Tagesvarianten.</summary>
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
    /// StopsByRoute-Schlüssel für eine Route. Tagesvarianten (unterschiedliches „Verkehr:“)
    /// teilen sich keine Liste; nur echte Aliase (gleiche Verkehrskennung) werden zusammengeführt.
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
        if (stopsByRoute.ContainsKey(trimmed))
        {
            return trimmed;
        }

        var sameSchedule = stopsByRoute
            .Where(pair => RouteDisplayHelper.RouteKeysMatchSameSchedule(pair.Key, trimmed))
            .Select(pair => (Key: pair.Key, Count: pair.Value.Count))
            .OrderByDescending(pair => pair.Count)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(sameSchedule))
        {
            return sameSchedule;
        }

        var requestVerkehr = RouteDisplayHelper.GetVerkehrLabel(trimmed);
        if (!string.IsNullOrEmpty(requestVerkehr))
        {
            // Andere Tagesvariante oder gemeinsamer Legacy-Bucket – nicht automatisch teilen.
            var daySpecificKeys = stopsByRoute.Keys
                .Where(key =>
                    RouteDisplayHelper.RouteKeysMatch(key, trimmed) &&
                    !string.IsNullOrEmpty(RouteDisplayHelper.GetVerkehrLabel(key)))
                .ToList();
            if (daySpecificKeys.Count > 0)
            {
                return null;
            }

            // Noch kein getrennter Bucket: Legacy ohne Verkehr nutzen (Caller kann klonen).
            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(trimmed);
            if (stopsByRoute.ContainsKey(canonical))
            {
                return canonical;
            }

            var legacy = stopsByRoute.Keys.FirstOrDefault(key =>
                RouteDisplayHelper.RouteKeysMatch(key, trimmed) &&
                string.IsNullOrEmpty(RouteDisplayHelper.GetVerkehrLabel(key)));
            return legacy;
        }

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

        var fallbackCanonical = RouteDisplayHelper.ToCanonicalRouteKey(trimmed);
        return stopsByRoute.ContainsKey(fallbackCanonical) ? fallbackCanonical : null;
    }

    /// <summary>True, wenn <paramref name="storageKey"/> ein gemeinsamer Legacy-Bucket für eine Tagesvariante ist.</summary>
    public static bool IsLegacySharedStopBucket(string requestRouteKey, string storageKey)
    {
        var requestVerkehr = RouteDisplayHelper.GetVerkehrLabel(requestRouteKey);
        if (string.IsNullOrEmpty(requestVerkehr))
        {
            return false;
        }

        if (RouteDisplayHelper.RouteKeysMatchSameSchedule(requestRouteKey, storageKey))
        {
            return false;
        }

        return RouteDisplayHelper.RouteKeysMatch(requestRouteKey, storageKey);
    }
}
