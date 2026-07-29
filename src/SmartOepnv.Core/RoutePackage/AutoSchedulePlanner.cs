using System.Globalization;
using System.Text;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;
/// <summary>Automatische Fahrplanerstellung wie GPSAnsagen <c>createAutoSchedule</c> / <c>updateSchedulePreview</c>.</summary>
public static class AutoSchedulePlanner
{
    public sealed record Request(
        string TemplateRouteKey,
        string StartTime,
        int TripCount,
        int IntervalMinutes,
        IReadOnlyList<string> TripNumbers);

    public static IReadOnlyList<string> SuggestTripNumbers(
        EditableRoutePackage editor,
        string templateRouteKey,
        int tripCount,
        bool isDirectionA)
    {
        if (tripCount <= 0)
        {
            return [];
        }

        return BuildSuggestedTripNumbers(editor, templateRouteKey, tripCount, isDirectionA)
            .Select(AutoScheduleTripNumber.Format)
            .ToList();
    }

    public static IReadOnlyList<string> GetSortedTemplateRoutes(EditableRoutePackage editor) =>
        RouteDisplayHelper.SortRoutesByLineCourseAndTrip(editor.RouteNames);

    public static int CountStopsForRoute(EditableRoutePackage editor, string routeKey)
    {
        var pureName = RouteDisplayHelper.ExtractPureName(routeKey);
        return editor.StopsByRoute.Values
            .SelectMany(stops => stops)
            .Count(stop =>
                !stop.IsWaypoint &&
                (string.Equals(stop.RouteName, routeKey, StringComparison.Ordinal) ||
                 string.Equals(RouteDisplayHelper.ExtractPureName(stop.RouteName), pureName, StringComparison.OrdinalIgnoreCase) ||
                 (routeKey.Contains("(Linie:", StringComparison.Ordinal) &&
                  stop.RouteName.Contains(pureName, StringComparison.OrdinalIgnoreCase))));
    }

    public static string? TryBuildPreview(EditableRoutePackage editor, Request request, int tripIndex)
    {
        if (!TryValidateRequest(editor, request, out var error))
        {
            return error;
        }

        var templateStops = GetTemplateStops(editor, request.TemplateRouteKey);
        if (templateStops.Count == 0)
        {
            return "Keine Haltestellen in der Vorlagen-Route gefunden";
        }

        if (tripIndex < 0 || tripIndex >= request.TripCount)
        {
            tripIndex = 0;
        }

        var tripNumbers = NormalizeTripNumbers(request.TripNumbers);
        var tripStartTime = RouteScheduleTimeCalculator.CalculateTripStartTime(
            request.StartTime,
            tripIndex * request.IntervalMinutes);
        var templateStartTime = ResolveTemplateStartTime(templateStops, request.StartTime);
        var tripNumber = tripNumbers[tripIndex];
        var (routeName, lineCourse) = ExtractRouteParts(request.TemplateRouteKey);
        var previewRoute = RouteDisplayHelper.ToDisplayString(new RouteDefinition(routeName, lineCourse, tripNumber));

        var preview = new StringBuilder();
        preview.AppendLine("Fahrplan-Vorschau:");
        preview.AppendLine();
        preview.AppendLine($"{previewRoute} (Start: {tripStartTime}):");
        for (var stopIndex = 0; stopIndex < templateStops.Count; stopIndex++)
        {
            var stop = templateStops[stopIndex];
            if (stopIndex == 0)
            {
                preview.AppendLine($"  {stop.Name}: {tripStartTime}");
                continue;
            }

            var calculated = CalculateStopTimeFromTemplate(
                tripStartTime,
                templateStartTime,
                stop.Time);
            preview.AppendLine($"  {stop.Name}: {calculated ?? "--:--"}");
        }

        return preview.ToString().TrimEnd();
    }

    public static string CreateSchedule(EditableRoutePackage editor, Request request)
    {
        if (!TryValidateRequest(editor, request, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var templateStops = GetTemplateStops(editor, request.TemplateRouteKey);
        if (templateStops.Count == 0)
        {
            throw new InvalidOperationException("Keine Haltestellen in der Vorlagen-Route gefunden");
        }

        var tripNumbers = ResolveTripNumbers(editor, request);
        var (routeName, lineCourse) = ExtractRouteParts(request.TemplateRouteKey);
        // Leeres Set = Vorlage ohne Eintrag = alle Verkehrstage (wie RouteOperatingDaysEditor).
        var templateOperatingDays = RouteOperatingDaysEditor.EffectiveDaySet(
            editor.GetRouteOperatingDays(request.TemplateRouteKey));
        var templateTripNumber = RouteDisplayHelper.NormalizeTripNumber(
            RouteDisplayHelper.Parse(request.TemplateRouteKey).TripNumber);
        var templateInteriorDestination = editor.GetRouteInteriorDisplayDestination(request.TemplateRouteKey);
        var templateInItcsRouteList = editor.IsRouteInItcsRouteList(request.TemplateRouteKey);
        var templateMainDeviceOnly = editor.IsRouteMainDeviceOnly(request.TemplateRouteKey);
        var templateStartTime = ResolveTemplateStartTime(templateStops, request.StartTime);
        string? firstRouteKey = null;
        var createdCount = 0;

        for (var tripIndex = 0; tripIndex < request.TripCount; tripIndex++)
        {
            var tripStartTime = RouteScheduleTimeCalculator.CalculateTripStartTime(
                request.StartTime,
                tripIndex * request.IntervalMinutes);
            var tripNumber = RouteDisplayHelper.NormalizeTripNumber(tripNumbers[tripIndex]);
            if (!string.IsNullOrEmpty(templateTripNumber) &&
                string.Equals(tripNumber, templateTripNumber, StringComparison.Ordinal))
            {
                continue;
            }

            var definition = new RouteDefinition(routeName, lineCourse, tripNumber);

            if (!editor.TryAddRoute(
                    definition,
                    templateOperatingDays,
                    request.TemplateRouteKey,
                    out var displayKey,
                    out var addError,
                    inItcsRouteList: templateInItcsRouteList,
                    mainDeviceOnly: templateMainDeviceOnly))
            {
                throw new InvalidOperationException(addError ?? "Route konnte nicht angelegt werden.");
            }

            createdCount++;
            firstRouteKey ??= displayKey;
            var newStops = editor.GetStops(displayKey).ToList();
            for (var stopIndex = 0; stopIndex < newStops.Count; stopIndex++)
            {
                var stop = newStops[stopIndex];
                stop.RouteName = displayKey;
                stop.Time = stopIndex == 0
                    ? tripStartTime
                    : CalculateStopTimeFromTemplate(
                          tripStartTime,
                          templateStartTime,
                          templateStops[stopIndex].Time) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(templateInteriorDestination))
            {
                editor.SetRouteInteriorDisplayDestination(displayKey, templateInteriorDestination);
            }

            editor.SetAutoScheduleSourceRoute(displayKey, request.TemplateRouteKey);
        }

        if (createdCount == 0)
        {
            throw new InvalidOperationException(
                "Keine neue Fahrt angelegt – die Vorlagen-Fahrtnummer wird nicht erneut erstellt.");
        }

        return firstRouteKey ?? string.Empty;
    }

    private static bool TryValidateRequest(Request request, out string? error)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateRouteKey) ||
            string.Equals(request.TemplateRouteKey, "Route auswählen...", StringComparison.Ordinal))
        {
            error = "Bitte eine Vorlagen-Route wählen.";
            return false;
        }

        if (!RouteScheduleTimeCalculator.TryParseTime(request.StartTime, out _))
        {
            error = "Ungültige Startzeit (HH:mm).";
            return false;
        }

        if (request.TripCount <= 0 || request.IntervalMinutes <= 0)
        {
            error = "Ungültige Anzahl Fahrten oder Intervall.";
            return false;
        }

        if (!TryValidateTripNumbers(null, request.TemplateRouteKey, request.TripNumbers, request.TripCount, out error))
        {
            return false;
        }

        return true;
    }

    public static bool TryValidateRequest(EditableRoutePackage editor, Request request, out string? error)
    {
        if (!TryValidateRequest(request, out error))
        {
            return false;
        }

        return TryValidateTripNumbers(editor, request.TemplateRouteKey, request.TripNumbers, request.TripCount, out error);
    }

    private static bool TryValidateTripNumbers(
        EditableRoutePackage? editor,
        string templateRouteKey,
        IReadOnlyList<string> tripNumbers,
        int expectedCount,
        out string? error)
    {
        error = null;
        if (tripNumbers.Count != expectedCount)
        {
            error = $"Bitte für alle {expectedCount} Fahrten eine 4-stellige Fahrtnummer eingeben.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var (routeName, lineCourse) = ExtractRouteParts(templateRouteKey);
        var templateTripNumber = RouteDisplayHelper.NormalizeTripNumber(
            RouteDisplayHelper.Parse(templateRouteKey).TripNumber);
        IReadOnlyCollection<DutyOperatingDay> templateOperatingDays = editor is null
            ? RouteOperatingDaysEditor.AllDays
            : RouteOperatingDaysEditor.EffectiveDaySet(editor.GetRouteOperatingDays(templateRouteKey));
        for (var i = 0; i < tripNumbers.Count; i++)
        {
            if (!AutoScheduleTripNumber.TryNormalize(tripNumbers[i], out var normalized))
            {
                error = $"Fahrt {i + 1}: 4-stellige Fahrtnummer erforderlich.";
                return false;
            }

            if (!seen.Add(normalized))
            {
                error = $"Fahrtnummer {normalized} ist doppelt vergeben.";
                return false;
            }

            if (editor is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(templateTripNumber) &&
                string.Equals(
                    RouteDisplayHelper.NormalizeTripNumber(normalized),
                    templateTripNumber,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var definition = new RouteDefinition(
                routeName,
                lineCourse,
                RouteDisplayHelper.NormalizeTripNumber(normalized));
            if (RouteDisplayHelper.HasRouteScheduleConflict(
                    editor.RouteNames,
                    editor.RouteOperatingDaysByRoute,
                    editor.RouteDateRangesByRoute,
                    definition,
                    templateOperatingDays,
                    null,
                    editor.RouteOperatingDatesByRoute,
                    null))
            {
                error = $"Fahrtnummer {normalized} existiert in dieser Linie/Kurs bereits.";
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> ResolveTripNumbers(EditableRoutePackage editor, Request request)
    {
        if (!TryValidateTripNumbers(editor, request.TemplateRouteKey, request.TripNumbers, request.TripCount, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return NormalizeTripNumbers(request.TripNumbers);
    }

    private static List<string> NormalizeTripNumbers(IReadOnlyList<string> tripNumbers) =>
        tripNumbers
            .Select(raw =>
            {
                AutoScheduleTripNumber.TryNormalize(raw, out var normalized);
                return normalized;
            })
            .ToList();

    private static List<RouteStopItem> GetTemplateStops(EditableRoutePackage editor, string templateRouteKey) =>
        editor.GetStops(templateRouteKey)
            .Where(s => !s.IsWaypoint)
            .ToList();

    private static string ResolveTemplateStartTime(IReadOnlyList<RouteStopItem> templateStops, string requestStartTime)
    {
        if (templateStops.Count == 0)
        {
            return RouteScheduleTimeCalculator.NormalizeTimeInput(requestStartTime);
        }

        var firstStopTime = RouteScheduleTimeCalculator.NormalizeTimeInput(templateStops[0].Time);
        return RouteScheduleTimeCalculator.TryParseTime(firstStopTime, out _)
            ? firstStopTime
            : RouteScheduleTimeCalculator.NormalizeTimeInput(requestStartTime);
    }

    private static string? CalculateStopTimeFromTemplate(
        string tripStartTime,
        string templateStartTime,
        string? templateStopTime) =>
        RouteScheduleTimeCalculator.CalculateStopTime(
            RouteScheduleTimeCalculator.NormalizeTimeInput(tripStartTime),
            templateStartTime,
            RouteScheduleTimeCalculator.NormalizeTimeInput(templateStopTime));

    private static (string RouteName, string LineCourse) ExtractRouteParts(string templateRouteKey)
    {
        var parsed = RouteDisplayHelper.Parse(templateRouteKey);
        var routeName = parsed.Name;
        var lineCourse = !string.IsNullOrWhiteSpace(parsed.LineCourse)
            ? RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse)
            : routeName;
        return (routeName, lineCourse);
    }

    private static IReadOnlyList<int> BuildSuggestedTripNumbers(
        EditableRoutePackage editor,
        string templateRouteKey,
        int tripCount,
        bool isDirectionA)
    {
        var existing = FindExistingTripNumbers(editor, templateRouteKey);
        var startTripNumber = isDirectionA
            ? existing.Count == 0
                ? 2
                : (existing.Where(n => n % 2 == 0).DefaultIfEmpty(0).Max() + 2)
            : existing.Count == 0
                ? 1
                : (existing.Where(n => n % 2 == 1).DefaultIfEmpty(-1).Max() + 2);

        return Enumerable.Range(0, tripCount)
            .Select(index => startTripNumber + index * 2)
            .ToList();
    }

    private static List<int> FindExistingTripNumbers(EditableRoutePackage editor, string templateRouteKey)
    {
        var template = RouteDisplayHelper.Parse(templateRouteKey);
        var pureName = template.Name;
        var lineCourse = !string.IsNullOrWhiteSpace(template.LineCourse)
            ? RouteDisplayHelper.NormalizeLineCourse(template.LineCourse)
            : pureName;

        var numbers = new List<int>();
        foreach (var routeKey in editor.RouteNames)
        {
            var def = RouteDisplayHelper.Parse(routeKey);
            if (!string.Equals(def.Name, pureName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var defLineCourse = !string.IsNullOrWhiteSpace(def.LineCourse)
                ? RouteDisplayHelper.NormalizeLineCourse(def.LineCourse)
                : def.Name;
            if (!string.Equals(defLineCourse, lineCourse, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(def.TripNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var trip) &&
                !numbers.Contains(trip))
            {
                numbers.Add(trip);
            }
        }

        return numbers.OrderBy(n => n).ToList();
    }
}
