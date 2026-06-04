using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Route-Anzeigenamen wie in GPSAnsagen (<see cref="Route.kt"/> / dialog_add_route).</summary>
public static class RouteDisplayHelper
{
    private static readonly Regex LegacyLineCourseSuffix = new(
        @"\(Linie:\s*([^,]+),\s*Fahrt:\s*([^)]+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ToDisplayString(RouteDefinition route)
    {
        var name = (route.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var lineCourse = (route.LineCourse ?? string.Empty).Trim();
        var tripNumber = (route.TripNumber ?? string.Empty).Trim();
        var passengerLine = (route.PassengerDisplayLine ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(lineCourse) &&
            string.IsNullOrEmpty(tripNumber) &&
            string.IsNullOrEmpty(passengerLine))
        {
            return name;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(lineCourse))
        {
            parts.Add($"Linie: {lineCourse}");
        }

        if (!string.IsNullOrEmpty(tripNumber))
        {
            parts.Add($"Fahrt: {tripNumber}");
        }

        if (!string.IsNullOrEmpty(passengerLine))
        {
            parts.Add($"PassengerLine: {passengerLine}");
        }

        return $"{name} ({string.Join(", ", parts)})";
    }

    public static RouteDefinition Parse(string displayString)
    {
        var text = (displayString ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text) || !text.Contains('('))
        {
            return new RouteDefinition(text);
        }

        var nameEndIndex = text.IndexOf('(');
        var name = text[..nameEndIndex].Trim();
        var infoStartIndex = nameEndIndex + 1;
        var infoEndIndex = text.LastIndexOf(')');
        var info = infoEndIndex > infoStartIndex
            ? text[infoStartIndex..infoEndIndex]
            : string.Empty;

        var lineCourse = string.Empty;
        var tripNumber = string.Empty;
        var passengerLine = string.Empty;
        foreach (var part in info.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Linie:", StringComparison.OrdinalIgnoreCase))
            {
                lineCourse = trimmed["Linie:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Fahrt:", StringComparison.OrdinalIgnoreCase))
            {
                tripNumber = trimmed["Fahrt:".Length..].Trim();
            }
            else if (trimmed.StartsWith("PassengerLine:", StringComparison.OrdinalIgnoreCase))
            {
                passengerLine = trimmed["PassengerLine:".Length..].Trim();
            }
        }

        return new RouteDefinition(name, lineCourse, tripNumber, passengerLine);
    }

    public static string ExtractPureName(string displayString) => Parse(displayString).Name;

    /// <summary>Automatisches „/“ nach den letzten zwei Ziffern (Linie/Kurs-Feld in der App).</summary>
    public static string FormatLineCourseInput(string? input)
    {
        var digitsOnly = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 2)
        {
            return digitsOnly;
        }

        var slashPosition = digitsOnly.Length - 2;
        return digitsOnly[..slashPosition] + "/" + digitsOnly[slashPosition..];
    }

    public static string NormalizeLineCourse(string? lineCourse)
    {
        var value = (lineCourse ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value) || !value.Contains('/'))
        {
            return value;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return value;
        }

        return parts[0].PadLeft(3, '0') + "/" + parts[1].PadLeft(2, '0');
    }

    public static bool HasDuplicateTripInLineCourse(IEnumerable<string> routeKeys, RouteDefinition candidate)
    {
        var lineCourse = NormalizeLineCourse(candidate.LineCourse);
        var trip = (candidate.TripNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(lineCourse) || string.IsNullOrEmpty(trip))
        {
            return false;
        }

        foreach (var key in routeKeys)
        {
            var existing = Parse(key);
            if (NormalizeLineCourse(existing.LineCourse) == lineCourse &&
                string.Equals((existing.TripNumber ?? string.Empty).Trim(), trip, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryExtractLegacyLineCourseParts(string routeName, out string pureName, out string lineCourse, out string tripNumber)
    {
        pureName = routeName;
        lineCourse = string.Empty;
        tripNumber = string.Empty;
        var match = LegacyLineCourseSuffix.Match(routeName);
        if (!match.Success)
        {
            return false;
        }

        pureName = routeName[..match.Index].TrimEnd();
        lineCourse = match.Groups[1].Value.Trim();
        tripNumber = match.Groups[2].Value.Trim();
        return true;
    }
}

public sealed record RouteDefinition(
    string Name,
    string LineCourse = "",
    string TripNumber = "",
    string PassengerDisplayLine = "");
