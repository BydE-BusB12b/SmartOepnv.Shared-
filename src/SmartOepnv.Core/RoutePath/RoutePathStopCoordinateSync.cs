using System.Globalization;
using System.Text.RegularExpressions;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Übernimmt verschobene Halt-/Ansage-Marker aus dem Fahrweg-Entwurf
/// in die Fahrt-Haltestellen (<see cref="RouteStopItem.GpsCoordinates"/> /
/// <see cref="RouteStopItem.StopCoordinates"/>).
/// </summary>
public static partial class RoutePathStopCoordinateSync
{
    private static readonly Regex IndexIdRegex = IndexIdPattern();

    /// <summary>
    /// Schreibt Kartenpositionen der genannten Knoten in die Halteliste.
    /// </summary>
    /// <returns>Anzahl geänderter Koordinatenfelder.</returns>
    public static int ApplyMovedNodesToStops(
        RoutePathDraft draft,
        IList<RouteStopItem> stops,
        IReadOnlyCollection<string> movedNodeIds)
    {
        if (draft.Nodes.Count == 0 || stops.Count == 0 || movedNodeIds.Count == 0)
        {
            return 0;
        }

        var byId = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var changed = 0;
        foreach (var nodeId in movedNodeIds)
        {
            if (!byId.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            if (node.Type is not (RoutePathNodeType.STOP or RoutePathNodeType.ANNOUNCEMENT))
            {
                continue;
            }

            if (!double.IsFinite(node.Lat) || !double.IsFinite(node.Lon))
            {
                continue;
            }

            var stop = ResolveStop(stops, node);
            if (stop is null)
            {
                continue;
            }

            var formatted = CoordinateFormatting.Format(node.Lat, node.Lon);
            if (node.Type == RoutePathNodeType.ANNOUNCEMENT)
            {
                if (!CoordinatesEqual(stop.GpsCoordinates, formatted))
                {
                    stop.GpsCoordinates = formatted;
                    changed++;
                }
            }
            else
            {
                if (!CoordinatesEqual(stop.StopCoordinates, formatted))
                {
                    stop.StopCoordinates = formatted;
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Alle Halt-/Ansage-Knoten des Entwurfs in die Halteliste schreiben (z. B. beim Speichern).
    /// </summary>
    public static int ApplyAllStopNodesToStops(RoutePathDraft draft, IList<RouteStopItem> stops)
    {
        var ids = draft.Nodes
            .Where(n => n.Type is RoutePathNodeType.STOP or RoutePathNodeType.ANNOUNCEMENT)
            .Select(n => n.Id)
            .ToList();
        return ApplyMovedNodesToStops(draft, stops, ids);
    }

    private static RouteStopItem? ResolveStop(IList<RouteStopItem> stops, RoutePathNode node)
    {
        var match = IndexIdRegex.Match(node.Id);
        if (match.Success &&
            int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 &&
            index < stops.Count)
        {
            var byIndex = stops[index];
            var expectedName = (node.SourceStopName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(expectedName) ||
                string.Equals(byIndex.Name.Trim(), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return byIndex;
            }
        }

        var name = (node.SourceStopName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return stops.FirstOrDefault(s =>
            string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CoordinatesEqual(string? existing, string formatted)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        if (string.Equals(existing.Trim(), formatted, StringComparison.Ordinal))
        {
            return true;
        }

        if (!RouteCoordinateParser.TryParse(existing, out var lat, out var lon) ||
            !RouteCoordinateParser.TryParse(formatted, out var lat2, out var lon2))
        {
            return false;
        }

        return Math.Abs(lat - lat2) < 1e-7 && Math.Abs(lon - lon2) < 1e-7;
    }

    [GeneratedRegex(@"^(stop|announcement)_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexIdPattern();
}
