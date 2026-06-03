using System.Text.RegularExpressions;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.AppShared.Sev;

public static partial class SevRouteImportHelper
{
    public sealed record RouteImportResult(
        string? Line,
        string? Destination,
        IReadOnlyList<string> Stops,
        string Summary);

    public static RouteImportResult BuildFromRoute(string routeName, IEnumerable<RouteStopItem> stops)
    {
        var stopList = stops
            .Where(s => !s.IsWaypoint)
            .Select(ResolveStopLabel)
            .Where(n => n.Length > 0)
            .ToList();

        var rawStops = stops.Where(s => !s.IsWaypoint).ToList();
        var line = ResolveLine(rawStops, routeName);
        var destination = ResolveDestination(rawStops);

        var summary = stopList.Count == 0
            ? $"Route „{routeName}“ hat keine Haltestellen."
            : $"{stopList.Count} Haltestelle(n) aus „{routeName}“ übernommen.";

        return new RouteImportResult(line, destination, stopList, summary);
    }

    /// <summary>ITCS-Verlaufsname (RouteStopItem.Name), nicht StopDisplay/IBIS-Kurztext.</summary>
    public static string ResolveStopLabel(RouteStopItem stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.Name))
        {
            return stop.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(stop.StopDisplay))
        {
            return stop.StopDisplay.Trim();
        }

        return string.Empty;
    }

    private static string? ResolveLine(IReadOnlyList<RouteStopItem> stops, string routeName)
    {
        var fromStop = stops
            .Select(s => s.LineNumber.Trim())
            .FirstOrDefault(v => v.Length > 0);

        if (!string.IsNullOrWhiteSpace(fromStop))
        {
            return SevSignData.FormatLine(fromStop);
        }

        return TryParseLineFromRouteName(routeName);
    }

    private static string? ResolveDestination(IReadOnlyList<RouteStopItem> stops)
    {
        if (stops.Count == 0)
        {
            return null;
        }

        var last = stops[^1];
        foreach (var candidate in new[] { last.EndDestination, last.Destination })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return ResolveStopLabel(last);
    }

    public static string? TryParseLineFromRouteName(string routeName)
    {
        var match = RouteLinePattern().Match(routeName);
        if (!match.Success)
        {
            return null;
        }

        return SevSignData.FormatLine($"{match.Groups[1].Value} {match.Groups[2].Value}");
    }

    [GeneratedRegex(@"\b(RE|S|RB|IC|ICE|EC)\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouteLinePattern();
}
