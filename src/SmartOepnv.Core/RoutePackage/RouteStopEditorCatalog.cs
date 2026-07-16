namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ziel- und Routenlisten für die Haltestellen-Bearbeitung (wie GPSAnsagen dialog_add_stop).</summary>
public static class RouteStopEditorCatalog
{
    public const string NoDestinationLabel = "Kein Ziel";
    public const string NoLineCourseTripLabel = "Keine Fahrt ausgewählt";

    /// <summary>Platzhalter-Ziel wie in GPSAnsagen, wenn Starthaltestelle ohne DS021T/Linie gesetzt wird.</summary>
    public const string StartStopPlaceholderDestination = "Starthaltestelle";

    /// <summary>Wie GPSAnsagen: Ziel (inkl. Platzhalter), DS003a-Ziel oder Liniennummer gesetzt.</summary>
    public static bool IsStartStop(RouteStopItem? stop) =>
        stop is not null && (
            !string.IsNullOrWhiteSpace(stop.Destination) ||
            !string.IsNullOrWhiteSpace(stop.Ds021NeuDestination) ||
            !string.IsNullOrWhiteSpace(stop.FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(stop.Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(stop.LineNumber));

    public static bool IsStartStopPlaceholder(string? destination) =>
        string.Equals(destination?.Trim(), StartStopPlaceholderDestination, StringComparison.OrdinalIgnoreCase);

    public static bool HasStartStopDestination(string? destination) =>
        !string.IsNullOrWhiteSpace(destination) && !IsStartStopPlaceholder(destination);

    /// <summary>
    /// Starthaltestelle ohne Ziel/Linie: Marker setzen, damit die Haltestelle nicht als „Ansage aus“ gilt.
    /// Entspricht GPSAnsagen dialog_add_stop.
    /// </summary>
    public static void EnsureStartStopMarker(RouteStopItem stop)
    {
        if (HasStartStopDestination(stop.Destination) ||
            HasStartStopDestination(stop.Ds021NeuDestination) ||
            HasStartStopDestination(stop.FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(stop.Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(stop.LineNumber))
        {
            return;
        }

        stop.Destination = StartStopPlaceholderDestination;
    }

    public static void ClearStartStopFields(RouteStopItem stop)
    {
        if (IsStartStopPlaceholder(stop.Destination))
        {
            stop.Destination = string.Empty;
        }
        else
        {
            stop.Destination = string.Empty;
        }

        stop.Destination = string.Empty;
        stop.Ds021NeuDestination = string.Empty;
        stop.FmaS1Destination = string.Empty;
        stop.Ds003aDestination = string.Empty;
        stop.LineNumber = string.Empty;
    }

    /// <summary>Haltestelle in der Liste durchstreichen (Ansage deaktiviert, keine Starthaltestelle).</summary>
    public static bool ShouldStrikeThroughDisplay(RouteStopItem? stop) =>
        stop is not null && !IsStartStop(stop) && !stop.IsAnnouncementEnabled;

    public static string ToComboLabel(string? value, string emptyLabel)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, "Starthaltestelle", StringComparison.OrdinalIgnoreCase))
        {
            return emptyLabel;
        }

        return trimmed;
    }

    public static string FromComboLabel(string? value, string emptyLabel) =>
        string.Equals(value?.Trim(), emptyLabel, StringComparison.Ordinal) ? string.Empty : value?.Trim() ?? string.Empty;

    public static IReadOnlyList<string> LoadDs021tNames(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, OutsideDisplayProtocolKind.Ds021T);

    public static IReadOnlyList<string> LoadDs021NeuNames(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, OutsideDisplayProtocolKind.Ds021Neu);

    public static IReadOnlyList<string> LoadFmaS1Names(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, OutsideDisplayProtocolKind.FmaS1);

    public static IReadOnlyList<string> LoadDs003aNames(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, OutsideDisplayProtocolKind.Ds003aKrefeld);

    public static IReadOnlyList<string> LoadLineCourseTripRoutes(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.RouteNames
            .Select(route => RouteDisplayHelper.Parse(route))
            .Where(def =>
                !string.IsNullOrWhiteSpace(def.LineCourse) ||
                !string.IsNullOrWhiteSpace(def.TripNumber))
            .Select(RouteDisplayHelper.ToDisplayString)
            .Where(display =>
                !string.IsNullOrWhiteSpace(display) &&
                display != "()" &&
                display != " (Linie: , Fahrt: )")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryResolveLineCourseTripByTripNumber(
        IEnumerable<string> routes,
        string tripNumberInput,
        string? contextRouteKey,
        out string? matchedRoute,
        out string? error)
    {
        matchedRoute = null;
        error = null;
        var normalizedTrip = RouteDisplayHelper.NormalizeTripNumber(tripNumberInput);
        if (string.IsNullOrEmpty(normalizedTrip))
        {
            error = "Bitte Fahrtnummer eingeben.";
            return false;
        }

        var candidates = routes
            .Where(route => !string.Equals(route, NoLineCourseTripLabel, StringComparison.Ordinal))
            .Where(route =>
                string.Equals(
                    RouteDisplayHelper.NormalizeTripNumber(RouteDisplayHelper.Parse(route).TripNumber),
                    normalizedTrip,
                    StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            error = $"Keine Route mit Fahrt {tripNumberInput.Trim()} gefunden.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(contextRouteKey))
        {
            var contextLineCourse = RouteDisplayHelper.NormalizeLineCourse(
                RouteDisplayHelper.Parse(contextRouteKey).LineCourse);
            if (!string.IsNullOrEmpty(contextLineCourse))
            {
                var sameLine = candidates
                    .Where(route =>
                        string.Equals(
                            RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(route).LineCourse),
                            contextLineCourse,
                            StringComparison.Ordinal))
                    .ToList();
                if (sameLine.Count == 1)
                {
                    matchedRoute = sameLine[0];
                    return true;
                }

                if (sameLine.Count > 1)
                {
                    error = $"Mehrere Fahrten mit Nummer {tripNumberInput.Trim()} auf Linie/Kurs {contextLineCourse}.";
                    return false;
                }
            }
        }

        if (candidates.Count == 1)
        {
            matchedRoute = candidates[0];
            return true;
        }

        error = $"Fahrtnummer {tripNumberInput.Trim()} ist mehrfach vorhanden – bitte Linie/Kurs prüfen.";
        return false;
    }

    private static IReadOnlyList<string> LoadProtocolNames(
        EditableRoutePackage? editor,
        OutsideDisplayProtocolKind protocol)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.OutsideDisplays
            .Select(OutsideDisplayProgram.TryParse)
            .Where(p => p is not null && p.IsListEnabled && !string.IsNullOrWhiteSpace(p.Name) && p.Protocol == protocol)
            .Select(p => p!.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
