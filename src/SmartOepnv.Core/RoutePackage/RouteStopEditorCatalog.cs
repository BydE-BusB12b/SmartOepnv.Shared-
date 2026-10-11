namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ziel- und Routenlisten für die Haltestellen-Bearbeitung (wie GPSAnsagen dialog_add_stop).</summary>
public static class RouteStopEditorCatalog
{
    public const string NoDestinationLabel = "Kein Ziel";
    public const string NoLineCourseTripLabel = "Keine Fahrt ausgewählt";

    /// <summary>Platzhalter-Ziel wie in GPSAnsagen, wenn Starthaltestelle ohne DS021T/Linie gesetzt wird.</summary>
    public const string StartStopPlaceholderDestination = "Starthaltestelle";

    /// <summary>
    /// Starthaltestelle (Routenstart): Ziel/Linie gesetzt und kein Zielwechsel.
    /// Zielwechsel-Halte haben ebenfalls ein Ziel, bleiben aber ansagepflichtig.
    /// </summary>
    public static bool IsStartStop(RouteStopItem? stop) =>
        stop is not null &&
        !stop.ZielwechselEnabled &&
        HasDestinationLink(stop);

    /// <summary>Ziel- oder Linienfelder gesetzt (Starthaltestelle oder Zielwechsel).</summary>
    public static bool HasDestinationLink(RouteStopItem? stop) =>
        stop is not null && (
            !string.IsNullOrWhiteSpace(stop.Destination) ||
            !string.IsNullOrWhiteSpace(stop.Ds021NeuDestination) ||
            !string.IsNullOrWhiteSpace(stop.FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(stop.Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(stop.ZielnummerDestination) ||
            !string.IsNullOrWhiteSpace(stop.MobitecDestination) ||
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
            !string.IsNullOrWhiteSpace(stop.ZielnummerDestination) ||
            !string.IsNullOrWhiteSpace(stop.MobitecDestination) ||
            !string.IsNullOrWhiteSpace(stop.LineNumber))
        {
            return;
        }

        stop.Destination = StartStopPlaceholderDestination;
    }

    public static void ClearStartStopFields(RouteStopItem stop)
    {
        stop.Destination = string.Empty;
        stop.DestinationId = string.Empty;
        stop.Ds021NeuDestination = string.Empty;
        stop.Ds021NeuDestinationId = string.Empty;
        stop.FmaS1Destination = string.Empty;
        stop.FmaS1DestinationId = string.Empty;
        stop.Ds003aDestination = string.Empty;
        stop.Ds003aDestinationId = string.Empty;
        stop.ZielnummerDestination = string.Empty;
        stop.ZielnummerDestinationId = string.Empty;
        stop.MobitecDestination = string.Empty;
        stop.MobitecDestinationId = string.Empty;
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
        LoadAllProtocolNames(editor).Ds021t;

    public static IReadOnlyList<string> LoadDs021NeuNames(EditableRoutePackage? editor) =>
        LoadAllProtocolNames(editor).Ds021Neu;

    public static IReadOnlyList<string> LoadFmaS1Names(EditableRoutePackage? editor) =>
        LoadAllProtocolNames(editor).FmaS1;

    public static IReadOnlyList<string> LoadDs003aNames(EditableRoutePackage? editor) =>
        LoadAllProtocolNames(editor).Ds003a;

    public static IReadOnlyList<string> LoadZielnummerNames(EditableRoutePackage? editor) =>
        LoadAllProtocolNames(editor).Zielnummer;

    public static IReadOnlyList<string> LoadMobitecNames(EditableRoutePackage? editor) =>
        LoadAllProtocolNames(editor).Mobitec;

    /// <summary>Ein Durchlauf über alle Außenanzeigen – für alle Protokoll-Combos im Stop-Editor.</summary>
    public static ProtocolNameLists LoadAllProtocolNames(EditableRoutePackage? editor)
    {
        var result = new ProtocolNameLists();
        if (editor is null)
        {
            return result;
        }

        var ds021t = new HashSet<string>(StringComparer.Ordinal);
        var ds021Neu = new HashSet<string>(StringComparer.Ordinal);
        var fmaS1 = new HashSet<string>(StringComparer.Ordinal);
        var ds003a = new HashSet<string>(StringComparer.Ordinal);
        var mobitec = new HashSet<string>(StringComparer.Ordinal);
        var ds003Programs = new List<OutsideDisplayProgram>();

        foreach (var entry in editor.OutsideDisplays)
        {
            var program = OutsideDisplayProgram.TryParse(entry);
            if (program is null || string.IsNullOrWhiteSpace(program.Name))
            {
                continue;
            }

            var name = program.Name.Trim();
            switch (program.Protocol)
            {
                case OutsideDisplayProtocolKind.Ds021T:
                    ds021t.Add(name);
                    break;
                case OutsideDisplayProtocolKind.Ds021Neu:
                    ds021Neu.Add(name);
                    break;
                case OutsideDisplayProtocolKind.FmaS1:
                    fmaS1.Add(name);
                    break;
                case OutsideDisplayProtocolKind.Ds003aKrefeld:
                    ds003a.Add(name);
                    break;
                case OutsideDisplayProtocolKind.Ds003:
                    ds003Programs.Add(program);
                    break;
                case OutsideDisplayProtocolKind.Mobitec:
                    mobitec.Add(name);
                    break;
            }
        }

        var zielnummerOrdered = new List<string>();
        var zielnummerSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in ds003Programs
                     .OrderBy(p => ParseZielnummerSortKey(p.FrontLine1))
                     .ThenBy(p => p.Ds001Value?.Trim() ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(p => p.Name.Trim(), Comparer<string>.Create(OutsideDisplayProgram.CompareZiellisteNames)))
        {
            var label = program.Ds003ListLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = program.Name.Trim();
            }

            if (zielnummerSeen.Add(label))
            {
                zielnummerOrdered.Add(label);
            }
        }

        var comparer = Comparer<string>.Create(OutsideDisplayProgram.CompareZiellisteNames);
        result.Ds021t.AddRange(ds021t.OrderBy(n => n, comparer));
        result.Ds021Neu.AddRange(ds021Neu.OrderBy(n => n, comparer));
        result.FmaS1.AddRange(fmaS1.OrderBy(n => n, comparer));
        result.Ds003a.AddRange(ds003a.OrderBy(n => n, comparer));
        result.Zielnummer.AddRange(zielnummerOrdered);
        result.Mobitec.AddRange(mobitec.OrderBy(n => n, comparer));
        return result;
    }

    private static int ParseZielnummerSortKey(string? frontLine1)
    {
        var digits = OutsideDisplayTelegramFactory.NormalizeZielnummer(frontLine1);
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    public sealed class ProtocolNameLists
    {
        public List<string> Ds021t { get; } = [];
        public List<string> Ds021Neu { get; } = [];
        public List<string> FmaS1 { get; } = [];
        public List<string> Ds003a { get; } = [];
        public List<string> Zielnummer { get; } = [];
        public List<string> Mobitec { get; } = [];
    }

    public static IReadOnlyList<string> LoadLineCourseTripRoutes(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return [];
        }

        // Voller Anzeigeschlüssel inkl. Verkehrstage – Tagesvarianten nicht zusammenlegen.
        var displays = editor.RouteNames
            .Where(route =>
            {
                var def = RouteDisplayHelper.Parse(route);
                return !string.IsNullOrWhiteSpace(def.LineCourse) ||
                       !string.IsNullOrWhiteSpace(def.TripNumber);
            })
            .Select(route =>
            {
                var days = editor.GetRouteOperatingDays(route);
                var definition = RouteDisplayHelper.Parse(route);
                return RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, days);
            })
            .Where(display =>
                !string.IsNullOrWhiteSpace(display) &&
                display != "()" &&
                display != " (Linie: , Fahrt: )")
            .Distinct(StringComparer.Ordinal);

        return RouteDisplayHelper.SortRoutesByLineCourseAndTrip(displays);
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
}
