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
        var existing = existingRoutes == null
            ? null
            : new HashSet<string>(
                existingRoutes.Where(static r => !string.IsNullOrWhiteSpace(r)),
                StringComparer.Ordinal);

        var collected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var routeName in initialRoutes)
        {
            CollectRecursive([routeName], allStops, collected, existing);
        }

        return collected;
    }

    private static bool ExistsInPackage(HashSet<string>? existing, string routeKey)
    {
        if (existing == null)
        {
            return true;
        }

        if (existing.Contains(routeKey))
        {
            return true;
        }

        return existing.Any(name => RouteDisplayHelper.RouteKeysMatch(name, routeKey));
    }

    private static void CollectRecursive(
        IReadOnlyList<string> initialRoutes,
        IEnumerable<RouteStopItem> allStops,
        HashSet<string> collected,
        HashSet<string>? existing)
    {
        var firstInitial = initialRoutes.Count > 0 ? initialRoutes[0] : null;
        foreach (var routeName in initialRoutes)
        {
            if (!ExistsInPackage(existing, routeName))
            {
                continue;
            }

            if (!collected.Add(routeName))
            {
                continue;
            }

            foreach (var stop in allStops.Where(s => s.RouteName == routeName))
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
                    if (collected.Contains(targetRoute))
                    {
                        continue;
                    }

                    if (string.Equals(targetRoute, firstInitial, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!ExistsInPackage(existing, targetRoute))
                    {
                        continue;
                    }

                    CollectRecursive([targetRoute], allStops, collected, existing);
                }
            }
        }
    }
}
