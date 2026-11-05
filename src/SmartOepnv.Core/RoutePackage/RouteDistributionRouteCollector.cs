namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Entspricht <c>RouteDistributionManager.collectAllRoutesForDistribution</c> (GPSAnsagen).
/// </summary>
public static class RouteDistributionRouteCollector
{
    /// <param name="existingRoutes">
    /// Wenn gesetzt, werden Routenwechsel-Ziele nur aufgenommen, wenn sie noch im Paket existieren.
    /// Verhindert leere Routen-Hüllen nach dem Löschen (Speichern schrieb tote Verweise sonst zurück).
    /// </param>
    public static HashSet<string> CollectAllRoutesForDistribution(
        IEnumerable<string> initialRoutes,
        IEnumerable<RouteStopItem> allStops,
        IEnumerable<string>? existingRoutes = null)
    {
        HashSet<string>? existingExact = null;
        HashSet<string>? existingCanonical = null;
        if (existingRoutes != null)
        {
            existingExact = new HashSet<string>(StringComparer.Ordinal);
            existingCanonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in existingRoutes)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                existingExact.Add(name);
                existingCanonical.Add(RouteDisplayHelper.ToCanonicalRouteKey(name));
            }
        }

        var stopsByRoute = new Dictionary<string, List<RouteStopItem>>(StringComparer.Ordinal);
        foreach (var stop in allStops)
        {
            var key = stop.RouteName ?? string.Empty;
            if (!stopsByRoute.TryGetValue(key, out var bucket))
            {
                bucket = [];
                stopsByRoute[key] = bucket;
            }

            bucket.Add(stop);
        }

        var collected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var routeName in initialRoutes)
        {
            CollectRecursive(routeName, routeName, stopsByRoute, collected, existingExact, existingCanonical);
        }

        return collected;
    }

    private static bool ExistsInPackage(
        HashSet<string>? exact,
        HashSet<string>? canonical,
        string routeKey)
    {
        if (exact == null)
        {
            return true;
        }

        if (exact.Contains(routeKey))
        {
            return true;
        }

        return canonical != null &&
               canonical.Contains(RouteDisplayHelper.ToCanonicalRouteKey(routeKey));
    }

    private static void CollectRecursive(
        string routeName,
        string firstInitial,
        Dictionary<string, List<RouteStopItem>> stopsByRoute,
        HashSet<string> collected,
        HashSet<string>? existingExact,
        HashSet<string>? existingCanonical)
    {
        if (!ExistsInPackage(existingExact, existingCanonical, routeName) ||
            !collected.Add(routeName) ||
            !stopsByRoute.TryGetValue(routeName, out var stops))
        {
            return;
        }

        foreach (var stop in stops)
        {
            if (!stop.RouteChangeEnabled)
            {
                continue;
            }

            var targets = new List<string>();
            if (!string.IsNullOrWhiteSpace(stop.SelectedLineCourseTrip))
            {
                targets.Add(stop.SelectedLineCourseTrip.Trim());
            }

            foreach (var entry in stop.RouteChangeTargetsByDate)
            {
                if (!string.IsNullOrWhiteSpace(entry.SelectedLineCourseTrip))
                {
                    targets.Add(entry.SelectedLineCourseTrip.Trim());
                }
            }

            foreach (var targetRoute in targets.Distinct(StringComparer.Ordinal))
            {
                if (collected.Contains(targetRoute) ||
                    string.Equals(targetRoute, firstInitial, StringComparison.Ordinal) ||
                    !ExistsInPackage(existingExact, existingCanonical, targetRoute))
                {
                    continue;
                }

                CollectRecursive(
                    targetRoute,
                    firstInitial,
                    stopsByRoute,
                    collected,
                    existingExact,
                    existingCanonical);
            }
        }
    }
}
