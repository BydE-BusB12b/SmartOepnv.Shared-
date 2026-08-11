using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Kopiert eine komplette Routenschnur (Fahrten + Routenwechsel-Verknüpfungen).</summary>
public static class RouteChainCopyPlanner
{
    public sealed record Request(
        IReadOnlyList<string> SourceRouteKeys,
        string TargetLineCourse,
        IReadOnlyCollection<DutyOperatingDay> OperatingDays,
        RouteDateRange? DateRange,
        IReadOnlyCollection<DateOnly>? OperatingDates);

    public sealed record Result(
        IReadOnlyList<string> CreatedRouteKeys,
        int CopiedCount);

    public static Result CopyChain(EditableRoutePackage editor, Request request)
    {
        if (request.SourceRouteKeys.Count == 0)
        {
            throw new InvalidOperationException("Keine Routenschnur zum Kopieren ausgewählt.");
        }

        if (!RouteDisplayHelper.TryParseLineCourseUserInput(request.TargetLineCourse, out var targetLineCourse))
        {
            throw new InvalidOperationException("Bitte Ziel-Linie/Kurs eingeben (z. B. 128/01).");
        }

        var days = request.OperatingDays?.Distinct().ToList() ?? [];
        if (days.Count == 0)
        {
            throw new InvalidOperationException("Bitte mindestens einen Verkehrstag auswählen.");
        }

        var dateRange = request.DateRange is { IsRestricted: true } ? request.DateRange : null;
        var operatingDates = RouteOperatingDatesEditor.IsRestricted(request.OperatingDates)
            ? request.OperatingDates
            : null;

        var keyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();

        foreach (var sourceKey in request.SourceRouteKeys)
        {
            var source = RouteDisplayHelper.Parse(sourceKey);
            var definition = new RouteDefinition(
                source.Name,
                targetLineCourse,
                RouteDisplayHelper.NormalizeTripNumber(source.TripNumber),
                source.PassengerDisplayLine);

            if (!editor.TryAddRoute(
                    definition,
                    days,
                    sourceKey,
                    out var displayKey,
                    out var addError,
                    inItcsRouteList: editor.IsRouteInItcsRouteList(sourceKey),
                    mainDeviceOnly: editor.IsRouteMainDeviceOnly(sourceKey),
                    dateRange: dateRange,
                    operatingDates: operatingDates))
            {
                throw new InvalidOperationException(
                    addError ?? $"Fahrt konnte nicht kopiert werden: {sourceKey}");
            }

            var interior = editor.GetRouteInteriorDisplayDestination(sourceKey);
            if (!string.IsNullOrWhiteSpace(interior))
            {
                editor.SetRouteInteriorDisplayDestination(displayKey, interior);
            }

            keyMap[sourceKey] = displayKey;
            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(sourceKey);
            if (!keyMap.ContainsKey(canonical))
            {
                keyMap[canonical] = displayKey;
            }

            var displayOnly = RouteDisplayHelper.ToDisplayString(source);
            if (!keyMap.ContainsKey(displayOnly))
            {
                keyMap[displayOnly] = displayKey;
            }

            created.Add(displayKey);
        }

        RemapRouteChangeTargets(editor, created, keyMap);
        return new Result(created, created.Count);
    }

    private static void RemapRouteChangeTargets(
        EditableRoutePackage editor,
        IReadOnlyList<string> createdKeys,
        IReadOnlyDictionary<string, string> keyMap)
    {
        foreach (var newKey in createdKeys)
        {
            foreach (var stop in editor.GetStops(newKey))
            {
                if (!stop.RouteChangeEnabled)
                {
                    continue;
                }

                if (TryMapReference(stop.SelectedLineCourseTrip, keyMap, out var mappedDefault))
                {
                    stop.SelectedLineCourseTrip = mappedDefault;
                }

                foreach (var entry in stop.RouteChangeTargetsByDate)
                {
                    if (TryMapReference(entry.SelectedLineCourseTrip, keyMap, out var mappedDated))
                    {
                        entry.SelectedLineCourseTrip = mappedDated;
                    }
                }
            }
        }
    }

    private static bool TryMapReference(
        string? reference,
        IReadOnlyDictionary<string, string> keyMap,
        out string mapped)
    {
        mapped = string.Empty;
        var trimmed = reference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (keyMap.TryGetValue(trimmed, out var direct))
        {
            mapped = direct;
            return true;
        }

        foreach (var (oldKey, newKey) in keyMap)
        {
            if (RouteDisplayHelper.RouteKeysMatch(trimmed, oldKey))
            {
                mapped = newKey;
                return true;
            }
        }

        // Fallback: gleiche Fahrtnummer → kopierte Ziel-Fahrt (Kurs im Verweis kann noch Quell-Kurs sein)
        var tripNumber = RouteDisplayHelper.NormalizeTripNumber(
            RouteDisplayHelper.Parse(trimmed).TripNumber);
        if (string.IsNullOrEmpty(tripNumber))
        {
            return false;
        }

        foreach (var newKey in keyMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    RouteDisplayHelper.NormalizeTripNumber(RouteDisplayHelper.Parse(newKey).TripNumber),
                    tripNumber,
                    StringComparison.Ordinal))
            {
                mapped = newKey;
                return true;
            }
        }

        return false;
    }
}
