using System.Text.RegularExpressions;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Sev;

/// <summary>
/// Linie aus dem Routennamen, Ziel aus der Endhaltestelle, Haltestellen aus dem Verlauf.
/// „RE4 Düsseldorf Hbf &gt; Neuss Hbf“ ergibt die Linie RE4.
/// Das Ziel ist der Name der Endhaltestelle, weil dort der volle Name steht.
/// Der Sonderkurs 000/00 steht als Text im Feld „linie“ und schaltet den Bildschirm aus.
/// </summary>
public static class SevTftRouteBuilder
{
    public static bool TryBuild(
        string? routeDisplayKey,
        IEnumerable<RouteStopItem>? stops,
        string? betrieb,
        out SevTftRoutePayload? payload,
        out string error,
        int dropThroughIndex = -1)
    {
        payload = null;
        var key = (routeDisplayKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            error = "Keine Route ausgewählt.";
            return false;
        }

        if (IsStandbyCourse(key))
        {
            payload = new SevTftRoutePayload
            {
                Linie = "000/00",
                Ziel = string.Empty,
                Haltestellen = [],
                Standby = true
            };
            error = string.Empty;
            return true;
        }

        if (!TryReadLineAndDestination(key, out var linie, out _, out error))
        {
            return false;
        }

        var passenger = new List<RouteStopItem>();
        if (stops is not null)
        {
            foreach (var stop in stops)
            {
                if (stop.IsWaypoint || ResolveStopLabel(stop).Length == 0)
                {
                    continue;
                }

                passenger.Add(stop);
            }
        }

        if (passenger.Count == 0)
        {
            error = "Die Route hat keine Haltestellen im Verlauf.";
            return false;
        }

        var endStop = passenger.LastOrDefault(stop => stop.IsEndStop) ?? passenger[^1];
        var ziel = ResolveStopLabel(endStop);
        var from = dropThroughIndex < 0 ? 0 : dropThroughIndex + 1;
        var haltestellen = from >= passenger.Count
            ? new List<string>()
            : passenger.Skip(from).Select(ResolveStopLabel).ToList();

        payload = new SevTftRoutePayload
        {
            Linie = linie,
            Ziel = ziel,
            Haltestellen = haltestellen,
            Betrieb = (betrieb ?? string.Empty).Trim()
        };
        error = string.Empty;
        return true;
    }

    /// <summary>Sonderkurs 000/00: Bildschirm aus, Webserver bleibt erreichbar.</summary>
    public static bool IsStandbyCourse(string? routeDisplayKey) => IsZeroCourse(ReadCourse(routeDisplayKey));

    public static string ReadCourse(string? routeDisplayKey)
    {
        if (string.IsNullOrWhiteSpace(routeDisplayKey))
        {
            return string.Empty;
        }

        var match = Regex.Match(routeDisplayKey, @"Linie:\s*([^,)]+)");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    public static bool IsZeroCourse(string? course)
    {
        var text = (course ?? string.Empty).Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
        {
            return false;
        }

        return IsZero(text[..slash]) && IsZero(text[(slash + 1)..]);
    }

    private static bool IsZero(string part)
    {
        var digits = part.Trim();
        return digits.Length > 0 && digits.All(c => c == '0');
    }

    public static bool TryReadLineAndDestination(
        string routeDisplayKey,
        out string linie,
        out string ziel,
        out string error)
    {
        linie = string.Empty;
        ziel = string.Empty;
        var name = RouteDisplayHelper.StripLeadingCalendarDatePrefix(
            RouteDisplayHelper.Parse(routeDisplayKey).Name);
        var separator = name.IndexOf('>');
        if (separator < 0)
        {
            separator = name.IndexOf('→');
        }

        if (separator < 0)
        {
            error = "Im Routennamen fehlt das Ziel nach „>“. Erwartet wird zum Beispiel „RE4 Düsseldorf Hbf > Neuss Hbf“.";
            return false;
        }

        var left = name[..separator].Trim().TrimEnd('-', '–', '—').Trim();
        var right = name[(separator + 1)..].Trim();
        linie = ReadLineToken(left);
        ziel = right;
        if (linie.Length == 0)
        {
            error = "Im Routennamen fehlt die Linie vor dem Ziel. Erwartet wird zum Beispiel „RE4 Düsseldorf Hbf > Neuss Hbf“.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>ITCS-Verlaufsname, nicht der IBIS-Kurztext.</summary>
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

    private static string ReadLineToken(string leftSide)
    {
        var parts = leftSide.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length >= 2 &&
            parts[0].Length is >= 1 and <= 4 &&
            parts[0].All(char.IsLetter) &&
            parts[1].All(char.IsDigit))
        {
            return parts[0] + parts[1];
        }

        return parts[0];
    }
}
