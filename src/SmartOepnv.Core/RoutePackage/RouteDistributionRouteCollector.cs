namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Entspricht <c>RouteDistributionManager.collectAllRoutesForDistribution</c> (GPSAnsagen).
/// </summary>
public static class RouteDistributionRouteCollector
{
    public static HashSet<string> CollectAllRoutesForDistribution(
        IEnumerable<string> initialRoutes,
        IEnumerable<RouteStopItem> allStops)
    {
        var collected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var routeName in initialRoutes)
        {
            CollectRecursive([routeName], allStops, collected);
        }

        return collected;
    }

    private static void CollectRecursive(
        IReadOnlyList<string> initialRoutes,
        IEnumerable<RouteStopItem> allStops,
        HashSet<string> collected)
    {
        var firstInitial = initialRoutes.Count > 0 ? initialRoutes[0] : null;
        foreach (var routeName in initialRoutes)
        {
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

                    CollectRecursive([targetRoute], allStops, collected);
                }
            }
        }
    }
}
