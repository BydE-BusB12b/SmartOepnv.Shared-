using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;

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

    /// <summary>Anzeigename inkl. Verkehrstags-Kennung bei Teiltages-Routen.</summary>
    public static string ToDisplayStringWithOperatingDays(
        RouteDefinition route,
        IReadOnlyCollection<DutyOperatingDay> operatingDays)
    {
        var baseDisplay = ToDisplayString(route);
        if (string.IsNullOrEmpty(baseDisplay) ||
            RouteOperatingDaysEditor.IsConfiguredForAllDays(operatingDays))
        {
            return baseDisplay;
        }

        var label = DutyOperatingDayHelper.FormatDisplay(operatingDays);
        if (string.IsNullOrEmpty(label))
        {
            return baseDisplay;
        }

        if (!baseDisplay.Contains('('))
        {
            return $"{baseDisplay} (Verkehr: {label})";
        }

        var closeIndex = baseDisplay.LastIndexOf(')');
        return closeIndex < 0
            ? $"{baseDisplay} (Verkehr: {label})"
            : baseDisplay[..closeIndex] + $", Verkehr: {label})";
    }

    /// <summary>
    /// Anzeige mit Linie/Kurs und Fahrt vorne, danach der Name
    /// (z. B. „Linie: 002/01, Fahrt: 1  002 / Bettrath…“). Speicherschlüssel unverändert lassen.
    /// </summary>
    public static string ToLineCourseTripFirstDisplayString(string? routeDisplayKey)
    {
        var text = (routeDisplayKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var parsed = Parse(text);
        var lineCourse = (parsed.LineCourse ?? string.Empty).Trim();
        var tripNumber = (parsed.TripNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(lineCourse) && string.IsNullOrEmpty(tripNumber))
        {
            return text;
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

        var traffic = ExtractVerkehrLabel(text);
        if (!string.IsNullOrEmpty(traffic))
        {
            parts.Add($"Verkehr: {traffic}");
        }

        var name = (parsed.Name ?? string.Empty).Trim();
        var prefix = string.Join(", ", parts);
        return string.IsNullOrEmpty(name) ? prefix : $"{prefix}  {name}";
    }

    private static string ExtractVerkehrLabel(string displayString)
    {
        var text = (displayString ?? string.Empty).Trim();
        var nameEndIndex = text.IndexOf('(');
        if (nameEndIndex < 0)
        {
            return string.Empty;
        }

        var infoStartIndex = nameEndIndex + 1;
        var infoEndIndex = text.LastIndexOf(')');
        var info = infoEndIndex > infoStartIndex
            ? text[infoStartIndex..infoEndIndex]
            : string.Empty;

        foreach (var part in info.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Verkehr:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["Verkehr:".Length..].Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>Fahrtnummer wie in der App (ohne führende Nullen: „01“ → „1“).</summary>
    public static string NormalizeTripNumber(string? tripNumber)
    {
        var value = (tripNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>
    /// Routenname für <c>routes_export.json</c> / Handy-zu-Handy (wie <c>RouteDistributionManager</c>):
    /// technische Linie/Kurs, Fahrt ohne führende Nullen, ohne <c>PassengerLine</c> im Schlüssel.
    /// </summary>
    public static string ToDistributionDisplayString(RouteDefinition route)
    {
        var distributionRoute = new RouteDefinition(
            route.Name,
            NormalizeLineCourse(route.LineCourse),
            NormalizeTripNumber(route.TripNumber),
            string.Empty);
        return ToDisplayString(distributionRoute);
    }

    public static string ToDistributionDisplayString(string displayString) =>
        ToDistributionDisplayString(Parse(displayString));

    /// <summary>Einheitlicher Schlüssel für <c>routeStops</c> / Haltestellen-Zuordnung (ohne PassengerLine).</summary>
    public static string ToCanonicalRouteKey(string routeKey) =>
        ToDistributionDisplayString(routeKey);

    public static bool RouteKeysMatch(string? left, string? right) =>
        string.Equals(
            ToCanonicalRouteKey(left ?? string.Empty),
            ToCanonicalRouteKey(right ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);

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
            else if (trimmed.StartsWith("Verkehr:", StringComparison.OrdinalIgnoreCase))
            {
                // Nur Anzeige-Kennung – Linie/Kurs/Fahrt bleiben unverändert.
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

    /// <summary>
    /// True wenn <paramref name="existing"/> und <paramref name="incoming"/> dieselbe Fahrt sind,
    /// aber der Name nur um ein Datumspräfix ergänzt/entfernt wurde
    /// (z. B. „Leerfahrt…“ → „13.07.2026Leerfahrt…“). Zwei Wochenvarianten mit je eigenem Präfix: false.
    /// </summary>
    public static bool IsLikelyRenamedRoute(RouteDefinition existing, RouteDefinition incoming)
    {
        if (!string.Equals(
                NormalizeLineCourse(existing.LineCourse),
                NormalizeLineCourse(incoming.LineCourse),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
                NormalizeTripNumber(existing.TripNumber),
                NormalizeTripNumber(incoming.TripNumber),
                StringComparison.Ordinal))
        {
            return false;
        }

        return IsLikelyRenamedRouteName(existing.Name, incoming.Name);
    }

    public static bool IsLikelyRenamedRouteName(string? existingName, string? incomingName)
    {
        var existing = (existingName ?? string.Empty).Trim();
        var incoming = (incomingName ?? string.Empty).Trim();
        if (existing.Length == 0 || incoming.Length == 0)
        {
            return false;
        }

        if (string.Equals(existing, incoming, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var strippedExisting = StripLeadingCalendarDatePrefix(existing);
        var strippedIncoming = StripLeadingCalendarDatePrefix(incoming);
        if (!string.Equals(strippedExisting, strippedIncoming, StringComparison.OrdinalIgnoreCase) ||
            strippedExisting.Length == 0)
        {
            return false;
        }

        var existingHadPrefix = !string.Equals(existing, strippedExisting, StringComparison.Ordinal);
        var incomingHadPrefix = !string.Equals(incoming, strippedIncoming, StringComparison.Ordinal);
        // Beide mit Datumspräfix = parallele Wochenfahrten, keine Umbenennung.
        if (existingHadPrefix && incomingHadPrefix)
        {
            return false;
        }

        return existingHadPrefix || incomingHadPrefix;
    }

    public static string StripLeadingCalendarDatePrefix(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var stripped = LeadingCalendarDatePrefix.Replace(trimmed, string.Empty, 1).Trim();
        return string.IsNullOrEmpty(stripped) ? trimmed : stripped;
    }

    private static readonly Regex LeadingCalendarDatePrefix = new(
        @"^\d{1,2}\.\d{1,2}\.\d{2,4}\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    /// <summary>Linie/Kurs aus App-Eingabe (Ziffernblock oder mit „/“) – wie GPSAnsagen <c>normalizeLineCourse</c>.</summary>
    public static bool TryParseLineCourseUserInput(string? input, out string normalizedLineCourse)
    {
        normalizedLineCourse = string.Empty;
        var raw = (input ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (raw.Contains('/'))
        {
            normalizedLineCourse = NormalizeLineCourse(raw);
            return normalizedLineCourse.Contains('/') &&
                   normalizedLineCourse.Length >= 6;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        normalizedLineCourse = digits.Length switch
        {
            3 => NormalizeLineCourse($"{digits.PadLeft(3, '0')}/00"),
            4 => NormalizeLineCourse(
                $"{digits[..3].PadLeft(3, '0')}/{digits[3..].PadLeft(2, '0')}"),
            5 => NormalizeLineCourse($"{digits[..3]}/{digits[3..]}"),
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(normalizedLineCourse);
    }

    public static bool HasDuplicateTripInLineCourse(IEnumerable<string> routeKeys, RouteDefinition candidate) =>
        HasRouteScheduleConflict(routeKeys, null, null, candidate, RouteOperatingDaysEditor.AllDays, null);

    /// <summary>Gleiche Linie/Kurs + Fahrt nur verboten bei überschneidenden Verkehrstagen und Datumsbereich.</summary>
    public static bool HasOperatingDayConflict(
        IEnumerable<string> routeKeys,
        IDictionary<string, HashSet<DutyOperatingDay>>? operatingDaysByRoute,
        RouteDefinition candidate,
        IReadOnlyCollection<DutyOperatingDay> candidateDays) =>
        HasRouteScheduleConflict(routeKeys, operatingDaysByRoute, null, candidate, candidateDays, null);

    public static bool HasRouteScheduleConflict(
        IEnumerable<string> routeKeys,
        IDictionary<string, HashSet<DutyOperatingDay>>? operatingDaysByRoute,
        IDictionary<string, RouteDateRange>? dateRangesByRoute,
        RouteDefinition candidate,
        IReadOnlyCollection<DutyOperatingDay> candidateDays,
        RouteDateRange? candidateDateRange,
        IDictionary<string, HashSet<DateOnly>>? operatingDatesByRoute = null,
        IReadOnlyCollection<DateOnly>? candidateOperatingDates = null)
    {
        var lineCourse = NormalizeLineCourse(candidate.LineCourse);
        var trip = NormalizeTripNumber(candidate.TripNumber);
        if (string.IsNullOrEmpty(lineCourse) || string.IsNullOrEmpty(trip))
        {
            return false;
        }

        var candidateDaySet = RouteOperatingDaysEditor.EffectiveDaySet(candidateDays);
        foreach (var key in routeKeys)
        {
            var existing = Parse(key);
            if (NormalizeLineCourse(existing.LineCourse) != lineCourse ||
                !string.Equals(NormalizeTripNumber(existing.TripNumber), trip, StringComparison.Ordinal))
            {
                continue;
            }

            var existingDaySet = RouteOperatingDaysEditor.EffectiveDaySet(
                operatingDaysByRoute is null
                    ? []
                    : RouteOperatingDaysEditor.GetDaysForRoute(operatingDaysByRoute, key));
            if (!RouteOperatingDaysEditor.DaysOverlap(existingDaySet, candidateDaySet))
            {
                continue;
            }

            var existingRange = dateRangesByRoute is null
                ? RouteDateRange.Unrestricted
                : RouteDateRangeEditor.GetRangeForRoute(dateRangesByRoute, key);
            if (!RouteDateRange.RangesOverlap(existingRange, candidateDateRange))
            {
                continue;
            }

            var existingDates = operatingDatesByRoute is null
                ? null
                : RouteOperatingDatesEditor.GetDatesForRoute(operatingDatesByRoute, key);
            if (!RouteOperatingDatesEditor.DateListsOverlap(existingDates, candidateOperatingDates))
            {
                continue;
            }

            return true;
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

    /// <summary>Sortierung wie GPSAnsagen <c>sortRoutesByLineCourseAndTrip</c>.</summary>
    public static List<string> SortRoutesByLineCourseAndTrip(IEnumerable<string> routes) =>
        routes
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, Comparer<string>.Create(CompareRoutesByLineCourseAndTrip))
            .ToList();

    private static int CompareRoutesByLineCourseAndTrip(string route1, string route2)
    {
        var (lineCourse1, trip1) = ExtractLineCourseAndTrip(route1);
        var (lineCourse2, trip2) = ExtractLineCourseAndTrip(route2);
        var lineComparison = CompareLineCourse(lineCourse1, lineCourse2);
        if (lineComparison != 0)
        {
            return lineComparison;
        }

        var tripComparison = CompareTripNumber(trip1, trip2);
        if (tripComparison != 0)
        {
            return tripComparison;
        }

        var name1 = (Parse(route1).Name ?? route1).Trim();
        var name2 = (Parse(route2).Name ?? route2).Trim();
        return string.Compare(name1, name2, StringComparison.OrdinalIgnoreCase);
    }

    private static (string LineCourse, string TripNumber) ExtractLineCourseAndTrip(string route)
    {
        var parsed = Parse(route);
        return (NormalizeLineCourse(parsed.LineCourse), (parsed.TripNumber ?? string.Empty).Trim());
    }

    private static int CompareLineCourse(string line1, string line2)
    {
        if (line1.Length == 0 && line2.Length == 0)
        {
            return 0;
        }

        if (line1.Length == 0)
        {
            return 1;
        }

        if (line2.Length == 0)
        {
            return -1;
        }

        var (lineNum1, course1) = SplitLineCourse(line1);
        var (lineNum2, course2) = SplitLineCourse(line2);
        var lineComparison = CompareNumericString(lineNum1, lineNum2);
        return lineComparison != 0 ? lineComparison : CompareNumericString(course1, course2);
    }

    private static (string Line, string Course) SplitLineCourse(string lineCourse)
    {
        var parts = lineCourse.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? (parts[0], parts[1]) : (lineCourse.Trim(), string.Empty);
    }

    private static int CompareTripNumber(string trip1, string trip2)
    {
        if (trip1.Length == 0 && trip2.Length == 0)
        {
            return 0;
        }

        if (trip1.Length == 0)
        {
            return 1;
        }

        if (trip2.Length == 0)
        {
            return -1;
        }

        return CompareNumericString(trip1, trip2);
    }

    private static int CompareNumericString(string str1, string str2)
    {
        var num1 = int.TryParse(str1, out var n1) ? n1 : 0;
        var num2 = int.TryParse(str2, out var n2) ? n2 : 0;
        return num1.CompareTo(num2);
    }
}

public sealed record RouteDefinition(
    string Name,
    string LineCourse = "",
    string TripNumber = "",
    string PassengerDisplayLine = "");
