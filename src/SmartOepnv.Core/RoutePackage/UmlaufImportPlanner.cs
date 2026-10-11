using System.Text;
using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Umlaufkarte → Routen: Haltestellenkette aus Vorlage, Zeiten per Startzeit-Offset
/// (optional Ankerzeiten an benannten Halten), Fahrtnummern mit automatischem Suffix bei Konflikt.
/// </summary>
public static class UmlaufImportPlanner
{
    public const string SwsAusfahrtStopName = "SWS Ausfahrt";
    public const string SwsEinfahrtStopName = "SWS Einfahrt";

    public enum Direction
    {
        Hin,
        Rueck
    }

    public sealed record TripSpec(
        Direction Direction,
        string TripNumber,
        string StartTime,
        IReadOnlyDictionary<string, string>? AnchorTimes = null,
        string? TemplateRouteKey = null,
        string? RouteNameOverride = null,
        string? LineCourseOverride = null,
        string? EndTime = null,
        /// <summary>Vorbereitete Halte (Zeiten + ggf. Ziele) aus dem Import-Dialog.</summary>
        IReadOnlyList<RouteStopItem>? PreparedStops = null,
        /// <summary>Verkehrstage dieser Fahrt; sonst Request.OperatingDays.</summary>
        IReadOnlyCollection<DutyOperatingDay>? OperatingDays = null);

    public sealed record Request(
        string HinTemplateRouteKey,
        string? RueckTemplateRouteKey,
        string? RouteNameOverride,
        string? LineCourseOverride,
        IReadOnlyCollection<DutyOperatingDay> OperatingDays,
        IReadOnlyList<TripSpec> Trips,
        bool LinkRouteChanges = true,
        /// <summary>Vor der ersten Fahrt (roter Pfeil) – Depot-Ausfahrtzeit, z. B. 06:46.</summary>
        string? AusfahrtTime = null,
        /// <summary>Einfahrtzeit Betr.Hof; wenn gesetzt oder Vorlage Einfahrt hat → letzte Hst = SWS Einfahrt.</summary>
        string? EinfahrtTime = null,
        bool AddAusfahrt = true);

    public sealed record Result(
        int CreatedCount,
        string? FirstRouteKey,
        IReadOnlyList<string> CreatedRouteKeys,
        IReadOnlyList<string> Warnings,
        int LinkedRouteChangeCount = 0);

    private static readonly Regex AnchorPairRegex = new(
        @"^\s*(?<name>.+?)\s*=\s*(?<time>\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> GetSortedTemplateRoutes(EditableRoutePackage editor) =>
        AutoSchedulePlanner.GetSortedTemplateRoutes(editor);

    public static bool TryParseTripsText(string? text, out List<TripSpec> trips, out string? error)
    {
        trips = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Bitte mindestens eine Fahrt einfügen (Richtung;Fahrt;Startzeit).";
            return false;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseTripLine(line, out var trip, out var lineError))
            {
                error = $"Zeile {i + 1}: {lineError}";
                return false;
            }

            trips.Add(trip);
        }

        if (trips.Count == 0)
        {
            error = "Keine gültigen Fahrten gefunden.";
            return false;
        }

        return true;
    }

    public static bool TryParseTripLine(string line, out TripSpec trip, out string? error)
    {
        trip = null!;
        error = null;
        var raw = (line ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            error = "Leere Zeile.";
            return false;
        }

        string main;
        string? anchorsPart = null;
        var pipe = raw.IndexOf('|');
        if (pipe >= 0)
        {
            main = raw[..pipe].Trim();
            anchorsPart = raw[(pipe + 1)..].Trim();
        }
        else
        {
            main = raw;
        }

        var parts = SplitMainParts(main);
        if (parts.Count < 3)
        {
            error = "Format: Richtung;Fahrt;Startzeit (optional |Halt=HH:mm|…).";
            return false;
        }

        if (!TryParseDirection(parts[0], out var direction))
        {
            error = $"Unbekannte Richtung „{parts[0]}“ (Hin/Rück).";
            return false;
        }

        var tripNumber = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(tripNumber))
        {
            error = "Fahrtnummer fehlt.";
            return false;
        }

        var startTime = RouteScheduleTimeCalculator.NormalizeTimeInput(parts[2]);
        if (!RouteScheduleTimeCalculator.TryParseTime(startTime, out _))
        {
            error = $"Ungültige Startzeit „{parts[2]}“ (HH:mm oder HHmm).";
            return false;
        }

        Dictionary<string, string>? anchors = null;
        if (!string.IsNullOrWhiteSpace(anchorsPart))
        {
            anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in anchorsPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = AnchorPairRegex.Match(chunk);
                if (!match.Success)
                {
                    error = $"Anker ungültig: „{chunk}“ (erwartet Halt=HH:mm).";
                    return false;
                }

                var name = match.Groups["name"].Value.Trim();
                var time = RouteScheduleTimeCalculator.NormalizeTimeInput(match.Groups["time"].Value);
                if (string.IsNullOrWhiteSpace(name) ||
                    !RouteScheduleTimeCalculator.TryParseTime(time, out _))
                {
                    error = $"Anker ungültig: „{chunk}“.";
                    return false;
                }

                anchors[name] = time;
            }
        }

        trip = new TripSpec(direction, tripNumber.Trim(), startTime, anchors);
        return true;
    }

    public static string BuildPreview(EditableRoutePackage editor, Request request)
    {
        if (!TryValidateRequest(editor, request, out var error))
        {
            return error ?? "Eingabe ungültig.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Fahrten: {request.Trips.Count}");
        if (request.OperatingDays is { Count: > 0 })
        {
            sb.AppendLine($"Verkehr (Standard): {DutyOperatingDayHelper.FormatDisplay(request.OperatingDays)}");
        }

        if (request.AddAusfahrt)
        {
            var aus = string.IsNullOrWhiteSpace(request.AusfahrtTime)
                ? "(Zeit fehlt)"
                : RouteScheduleTimeCalculator.NormalizeTimeInput(request.AusfahrtTime);
            sb.AppendLine($"Ausfahrt: {SwsAusfahrtStopName} {aus} → vor 1. Fahrt");
        }

        var lastTemplate = ResolveTemplateKey(request, request.Trips[^1]);
        var willEinfahrt = ShouldAddEinfahrt(editor, request, lastTemplate);
        if (willEinfahrt)
        {
            var ein = string.IsNullOrWhiteSpace(request.EinfahrtTime)
                ? "(aus Vorlage / ohne Zeit)"
                : RouteScheduleTimeCalculator.NormalizeTimeInput(request.EinfahrtTime);
            sb.AppendLine($"Einfahrt: {SwsEinfahrtStopName} {ein} → letzte Hst der letzten Fahrt");
        }

        if (request.LinkRouteChanges && request.Trips.Count >= 2)
        {
            sb.AppendLine($"Routenwechsel: {request.Trips.Count - 1}× Folgefahrt (Reihenfolge der Liste)");
        }

        sb.AppendLine();

        for (var i = 0; i < request.Trips.Count; i++)
        {
            var trip = request.Trips[i];
            var templateKey = ResolveTemplateKey(request, trip);
            var (routeName, lineCourse) = ResolveRouteParts(templateKey, request, trip);
            var tripNumber = RouteDisplayHelper.NormalizeTripNumber(trip.TripNumber);
            var display = RouteDisplayHelper.ToDisplayString(new RouteDefinition(routeName, lineCourse, tripNumber));
            var stopCount = AutoSchedulePlanner.CountStopsForRoute(editor, templateKey);
            var dirLabel = trip.Direction == Direction.Hin ? "Hin" : "Rück";
            var endPart = string.IsNullOrWhiteSpace(trip.EndTime) ? string.Empty : $"–{trip.EndTime}";
            var days = ResolveOperatingDays(request, trip);
            sb.AppendLine(
                $"{i + 1}. {dirLabel}  Fahrt {tripNumber}  {trip.StartTime}{endPart}  → {display}  ({stopCount} Hst)");
            sb.AppendLine($"    Verkehr: {DutyOperatingDayHelper.FormatDisplay(days)}");
            if (trip.AnchorTimes is { Count: > 0 })
            {
                sb.AppendLine($"    Anker: {string.Join(", ", trip.AnchorTimes.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }

            if (request.LinkRouteChanges && i < request.Trips.Count - 1)
            {
                var next = request.Trips[i + 1];
                var nextTrip = RouteDisplayHelper.NormalizeTripNumber(next.TripNumber);
                sb.AppendLine($"    → Folgefahrt {nextTrip}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static Result Import(EditableRoutePackage editor, Request request)
    {
        if (!TryValidateRequest(editor, request, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var warnings = new List<string>();
        var createdKeys = new List<string>();
        string? firstKey = null;
        var anyTemplateHadEinfahrt = false;

        // Bereits in diesem Lauf vergebene (Linie/Kurs, Fahrt, Verkehrstage) merken
        var reservedTripKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var trip in request.Trips)
        {
            var operatingDays = RouteOperatingDaysEditor.EffectiveDaySet(ResolveOperatingDays(request, trip));
            var templateKey = ResolveTemplateKey(request, trip);
            var templateStopsRaw = GetTemplateStops(editor, templateKey);
            if (templateStopsRaw.Count == 0)
            {
                throw new InvalidOperationException($"Vorlage ohne Haltestellen: {templateKey}");
            }

            if (HasDepotEinfahrt(templateStopsRaw))
            {
                anyTemplateHadEinfahrt = true;
            }

            var templateStops = StripDepotStops(templateStopsRaw);
            if (templateStops.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Vorlage nur Depot-Halte (keine Fahrgast-Hst): {templateKey}");
            }

            var (routeName, lineCourse) = ResolveRouteParts(templateKey, request, trip);
            var uniqueTrip = ResolveUniqueTripNumber(
                editor,
                routeName,
                lineCourse,
                trip.TripNumber,
                operatingDays,
                reservedTripKeys,
                out var suffixUsed);
            if (suffixUsed)
            {
                warnings.Add(
                    $"Fahrt {trip.TripNumber} → {uniqueTrip} (Konflikt, Suffix gesetzt).");
            }

            reservedTripKeys.Add(BuildTripReserveKey(lineCourse, uniqueTrip, operatingDays));

            var definition = new RouteDefinition(routeName, lineCourse, uniqueTrip);
            var templateDateRange = editor.GetRouteDateRange(templateKey);
            var templateDateRangeArg = templateDateRange.IsRestricted ? templateDateRange : null;
            var templateOperatingDates = editor.GetRouteOperatingDates(templateKey);
            var templateOperatingDatesArg = RouteOperatingDatesEditor.IsRestricted(templateOperatingDates)
                ? templateOperatingDates
                : null;
            var templateInteriorDestination = editor.GetRouteInteriorDisplayDestination(templateKey);
            var templateInItcsRouteList = editor.IsRouteInItcsRouteList(templateKey);
            var templateMainDeviceOnly = editor.IsRouteMainDeviceOnly(templateKey);

            if (!editor.TryAddRoute(
                    definition,
                    operatingDays,
                    templateKey,
                    out var displayKey,
                    out var addError,
                    inItcsRouteList: templateInItcsRouteList,
                    mainDeviceOnly: templateMainDeviceOnly,
                    dateRange: templateDateRangeArg,
                    operatingDates: templateOperatingDatesArg))
            {
                throw new InvalidOperationException(addError ?? $"Route konnte nicht angelegt werden ({trip.TripNumber}).");
            }

            // Depot-Hst der Vorlage entfernen – SWS Aus-/Einfahrt werden gesondert gesetzt.
            if (trip.PreparedStops is { Count: > 0 })
            {
                var prepared = trip.PreparedStops
                    .Select(s =>
                    {
                        var clone = s.Clone();
                        clone.RouteName = displayKey;
                        return clone;
                    })
                    .ToList();
                editor.ReplaceStopsForRoute(displayKey, StripDepotStops(prepared));
            }
            else
            {
                editor.ReplaceStopsForRoute(displayKey, StripDepotStops(editor.GetStops(displayKey).ToList()));
                ApplyTimes(editor, displayKey, templateStops, trip);
            }

            if (!string.IsNullOrWhiteSpace(templateInteriorDestination))
            {
                editor.SetRouteInteriorDisplayDestination(displayKey, templateInteriorDestination);
            }

            editor.SetAutoScheduleSourceRoute(displayKey, templateKey);

            // Von der Vorlage geerbte Folgefahrten entfernen – werden ggf. neu gesetzt.
            ClearRouteChangeOnRoute(editor, displayKey);

            createdKeys.Add(displayKey);
            firstKey ??= displayKey;
        }

        if (request.AddAusfahrt && createdKeys.Count > 0)
        {
            ApplySwsAusfahrt(editor, createdKeys[0], request.AusfahrtTime, warnings);
        }

        // Einfahrt aus Umlaufkarte (Betr.Hof-Endzeit) oder wenn Vorlage bereits Einfahrt hatte
        var addEinfahrt = !string.IsNullOrWhiteSpace(request.EinfahrtTime) || anyTemplateHadEinfahrt;
        if (addEinfahrt && createdKeys.Count > 0)
        {
            ApplySwsEinfahrt(editor, createdKeys[^1], request.EinfahrtTime, warnings);
        }

        var linkedCount = 0;
        if (request.LinkRouteChanges)
        {
            linkedCount = LinkSequentialRouteChanges(editor, createdKeys);
        }

        return new Result(createdKeys.Count, firstKey, createdKeys, warnings, linkedCount);
    }

    /// <summary>
    /// Setzt am Endhalt jeder Fahrt die nächste Route der Import-Liste als Standard-Folgefahrt.
    /// IDs am Wechselpunkt dürfen unterschiedlich sein – Verknüpfung läuft über die Routen-Reihenfolge.
    /// </summary>
    public static int LinkSequentialRouteChanges(
        EditableRoutePackage editor,
        IReadOnlyList<string> createdRouteKeys)
    {
        if (createdRouteKeys.Count < 2)
        {
            return 0;
        }

        var linked = 0;
        for (var i = 0; i < createdRouteKeys.Count - 1; i++)
        {
            var fromKey = createdRouteKeys[i];
            var toKey = createdRouteKeys[i + 1];
            var endStop = ResolveEndStop(editor, fromKey);
            if (endStop is null)
            {
                continue;
            }

            endStop.IsEndStop = true;
            endStop.RouteChangeEnabled = true;
            endStop.SelectedLineCourseTrip = toKey;
            endStop.RouteChangeTargetsByDate = [];
            linked++;
        }

        var lastEnd = ResolveEndStop(editor, createdRouteKeys[^1]);
        if (lastEnd is not null)
        {
            lastEnd.IsEndStop = true;
        }

        return linked;
    }

    private static void ClearRouteChangeOnRoute(EditableRoutePackage editor, string routeKey)
    {
        foreach (var stop in editor.GetStops(routeKey))
        {
            stop.RouteChangeEnabled = false;
            stop.SelectedLineCourseTrip = string.Empty;
            stop.RouteChangeTargetsByDate = [];
        }
    }

    private static RouteStopItem? ResolveEndStop(EditableRoutePackage editor, string routeKey)
    {
        var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
        if (stops.Count == 0)
        {
            return null;
        }

        return stops.LastOrDefault(s => s.IsEndStop) ?? stops[^1];
    }

    public static bool TryValidateRequest(EditableRoutePackage editor, Request request, out string? error)
    {
        error = null;

        if (request.Trips is null || request.Trips.Count == 0)
        {
            error = "Keine Fahrten angegeben.";
            return false;
        }

        for (var i = 0; i < request.Trips.Count; i++)
        {
            var trip = request.Trips[i];
            var days = ResolveOperatingDays(request, trip);
            if (days.Count == 0)
            {
                error = $"Fahrt {i + 1}: bitte mindestens einen Verkehrstag wählen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(trip.TripNumber))
            {
                error = $"Fahrt {i + 1}: Fahrtnummer fehlt.";
                return false;
            }

            if (!RouteScheduleTimeCalculator.TryParseTime(trip.StartTime, out _))
            {
                error = $"Fahrt {i + 1}: ungültige Startzeit.";
                return false;
            }

            string templateKey;
            try
            {
                templateKey = ResolveTemplateKey(request, trip);
            }
            catch (Exception ex)
            {
                error = $"Fahrt {i + 1}: {ex.Message}";
                return false;
            }

            if (GetTemplateStops(editor, templateKey).Count == 0)
            {
                error = $"Fahrt {i + 1}: Vorlage ohne Haltestellen.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AusfahrtTime) &&
            !RouteScheduleTimeCalculator.TryParseTime(request.AusfahrtTime, out _))
        {
            error = "Ungültige Ausfahrtzeit (HH:mm).";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.EinfahrtTime) &&
            !RouteScheduleTimeCalculator.TryParseTime(request.EinfahrtTime, out _))
        {
            error = "Ungültige Einfahrtzeit (HH:mm).";
            return false;
        }

        return true;
    }

    private static bool ShouldAddEinfahrt(
        EditableRoutePackage editor,
        Request request,
        string lastTemplateKey)
    {
        if (!string.IsNullOrWhiteSpace(request.EinfahrtTime))
        {
            return true;
        }

        return HasDepotEinfahrt(GetTemplateStops(editor, lastTemplateKey));
    }

    private static void ApplySwsAusfahrt(
        EditableRoutePackage editor,
        string routeKey,
        string? ausfahrtTime,
        List<string> warnings)
    {
        var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
        while (stops.Count > 0 && IsDepotAusfahrt(stops[0].Name))
        {
            stops.RemoveAt(0);
        }

        var stop = CreateSwsDepotStop(editor, routeKey, ausfahrtTime, ausfahrt: true, warnings);
        stops.Insert(0, stop);
        editor.ReplaceStopsForRoute(routeKey, stops);

        if (string.IsNullOrWhiteSpace(ausfahrtTime))
        {
            warnings.Add("SWS Ausfahrt ohne Zeit (Ausfahrtzeit leer).");
        }
    }

    private static void ApplySwsEinfahrt(
        EditableRoutePackage editor,
        string routeKey,
        string? einfahrtTime,
        List<string> warnings)
    {
        var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
        while (stops.Count > 0 && IsDepotEinfahrt(stops[^1].Name))
        {
            stops.RemoveAt(stops.Count - 1);
        }

        var stop = CreateSwsDepotStop(editor, routeKey, einfahrtTime, ausfahrt: false, warnings);
        stops.Add(stop);
        EnsureDienstfahrtEndBeforeEinfahrt(stops);
        editor.ReplaceStopsForRoute(routeKey, stops);

        if (string.IsNullOrWhiteSpace(einfahrtTime))
        {
            warnings.Add("SWS Einfahrt ohne Zeit (Einfahrtzeit leer, Vorlage hatte Einfahrt).");
        }
    }

    /// <summary>
    /// Letzter Fahrgasthalt vor SWS Einfahrt = Endhaltestelle mit Ziel „Dienstfahrt“.
    /// SWS Einfahrt selbst ist nur Depot-Halt danach.
    /// </summary>
    public static void EnsureDienstfahrtEndBeforeEinfahrt(IList<RouteStopItem> stops)
    {
        if (stops.Count < 2 || !IsDepotEinfahrt(stops[^1].Name))
        {
            return;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            if (!IsSwsDepotStopName(stops[i].Name))
            {
                stops[i].IsEndStop = false;
            }
        }

        var passengerEnd = stops[^2];
        if (IsSwsDepotStopName(passengerEnd.Name))
        {
            return;
        }

        passengerEnd.IsEndStop = true;
        ApplyDienstfahrtEndDestination(passengerEnd);

        // Einfahrt: letzter Listeneintrag, aber kein Fahrgast-Endziel
        stops[^1].IsEndStop = false;
    }

    private static void ApplyDienstfahrtEndDestination(RouteStopItem stop)
    {
        const string dienstfahrt = "Dienstfahrt";
        stop.EndDestination = dienstfahrt;
        stop.Ds021NeuEndDestination = dienstfahrt;
        stop.FmaS1EndDestination = dienstfahrt;
        if (string.IsNullOrWhiteSpace(stop.Ds003aEndDestination))
        {
            stop.Ds003aEndDestination = dienstfahrt;
        }

        if (string.IsNullOrWhiteSpace(stop.ZielnummerEndDestination))
        {
            stop.ZielnummerEndDestination = dienstfahrt;
        }

        if (string.IsNullOrWhiteSpace(stop.MobitecEndDestination))
        {
            stop.MobitecEndDestination = dienstfahrt;
        }

        // Fallback, falls Anzeige nur Destination (ohne End*-Felder) liest
        if (!RouteStopEditorCatalog.HasStartStopDestination(stop.Destination))
        {
            stop.Destination = dienstfahrt;
        }
    }

    /// <summary>SWS Ausfahrt/Einfahrt aus Bibliothek oder als Minimal-Halt (für Dialog/Import).</summary>
    public static RouteStopItem CreateSwsDepotStop(
        EditableRoutePackage editor,
        string routeKey,
        string? time,
        bool ausfahrt,
        List<string>? warnings = null)
    {
        var preferredName = ausfahrt ? SwsAusfahrtStopName : SwsEinfahrtStopName;
        var library = FindLibraryStop(editor, preferredName, preferAusfahrt: ausfahrt);
        RouteStopItem stop;
        if (library is not null)
        {
            stop = library.ToRouteStop(routeKey);
        }
        else
        {
            warnings?.Add(
                $"Haltestelle „{preferredName}“ nicht in der Bibliothek – Minimal-Halt mit Name gesetzt.");
            stop = new RouteStopItem
            {
                Name = preferredName,
                RouteName = routeKey,
                PlannerStopCode = string.Empty,
                IsAnnouncementEnabled = false
            };
        }

        stop.Name = preferredName;
        stop.Time = string.IsNullOrWhiteSpace(time)
            ? string.Empty
            : RouteScheduleTimeCalculator.NormalizeTimeInput(time);
        if (ausfahrt)
        {
            stop.PlayStartStopGreeting = true;
            // Depot-Ausfahrt: Außenanzeige „Dienstfahrt“, falls Bibliothek kein Ziel liefert
            if (!RouteStopEditorCatalog.HasStartStopDestination(stop.Destination) &&
                !RouteStopEditorCatalog.HasStartStopDestination(stop.Ds021NeuDestination) &&
                !RouteStopEditorCatalog.HasStartStopDestination(stop.FmaS1Destination) &&
                string.IsNullOrWhiteSpace(stop.Ds003aDestination) &&
                string.IsNullOrWhiteSpace(stop.ZielnummerDestination) &&
                string.IsNullOrWhiteSpace(stop.MobitecDestination))
            {
                stop.Destination = "Dienstfahrt";
            }

            RouteStopEditorCatalog.EnsureStartStopMarker(stop);
        }
        // Einfahrt: IsEndStop setzt EnsureDienstfahrtEndBeforeEinfahrt am Fahrgasthalt davor
        return stop;
    }

    public static bool IsSwsDepotStopName(string? name) =>
        IsDepotAusfahrt(name) || IsDepotEinfahrt(name);

    private static ManagedStopTemplateItem? FindLibraryStop(
        EditableRoutePackage editor,
        string preferredName,
        bool preferAusfahrt)
    {
        var templates = editor.StopTemplates;
        var exact = templates.FirstOrDefault(t =>
            string.Equals(t.StopNameItcs?.Trim(), preferredName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Fallback: Name enthält SWS + Ausfahrt/Einfahrt
        var keyword = preferAusfahrt ? "ausfahrt" : "einfahrt";
        return templates.FirstOrDefault(t =>
        {
            var name = t.StopNameItcs ?? string.Empty;
            return name.Contains("SWS", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }) ?? templates.FirstOrDefault(t =>
        {
            var name = t.StopNameItcs ?? string.Empty;
            return preferAusfahrt ? IsDepotAusfahrt(name) : IsDepotEinfahrt(name);
        });
    }

    private static List<RouteStopItem> StripDepotStops(IReadOnlyList<RouteStopItem> stops)
    {
        var list = stops.Where(s => !s.IsWaypoint).ToList();
        while (list.Count > 0 && IsDepotAusfahrt(list[0].Name))
        {
            list.RemoveAt(0);
        }

        while (list.Count > 0 && IsDepotEinfahrt(list[^1].Name))
        {
            list.RemoveAt(list.Count - 1);
        }

        return list;
    }

    private static bool HasDepotEinfahrt(IReadOnlyList<RouteStopItem> stops) =>
        stops.Any(s => !s.IsWaypoint && IsDepotEinfahrt(s.Name));

    private static bool IsDepotAusfahrt(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(n))
        {
            return false;
        }

        if (string.Equals(n, SwsAusfahrtStopName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lower = n.ToLowerInvariant();
        return lower.Contains("ausfahrt", StringComparison.Ordinal) &&
               (lower.Contains("sws", StringComparison.Ordinal) ||
                lower.Contains("depot", StringComparison.Ordinal) ||
                lower.Contains("betriebshof", StringComparison.Ordinal) ||
                lower.Contains("betr.hof", StringComparison.Ordinal) ||
                lower.Contains("betr hof", StringComparison.Ordinal));
    }

    private static bool IsDepotEinfahrt(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(n))
        {
            return false;
        }

        if (string.Equals(n, SwsEinfahrtStopName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lower = n.ToLowerInvariant();
        return lower.Contains("einfahrt", StringComparison.Ordinal) &&
               (lower.Contains("sws", StringComparison.Ordinal) ||
                lower.Contains("depot", StringComparison.Ordinal) ||
                lower.Contains("betriebshof", StringComparison.Ordinal) ||
                lower.Contains("betr.hof", StringComparison.Ordinal) ||
                lower.Contains("betr hof", StringComparison.Ordinal));
    }

    /// <summary>
    /// Baut Haltestellen aus der Vorlage.
    /// 1) Planzeiten aus der Umlaufkarte als Anker,
    /// 2) Zwischenhalte aus Vorlagen-/Lern-Minuten auffüllen,
    /// 3) mindestens 1 Minute Abstand zwischen aufeinanderfolgenden Halten.
    /// </summary>
    public static List<RouteStopItem> BuildStopsFromTemplate(
        EditableRoutePackage editor,
        string templateRouteKey,
        string startTime,
        string? endTime = null,
        IReadOnlyList<RouteStopItem>? preserveFrom = null,
        string? startStopHint = null,
        string? endStopHint = null,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? planTimingPoints = null,
        IReadOnlyDictionary<(string FromCode, string ToCode), int>? typicalTravelMinutes = null)
    {
        var fullTemplate = StripDepotStops(GetTemplateStops(editor, templateRouteKey));
        if (fullTemplate.Count == 0)
        {
            return [];
        }

        // Volle Vorlagenkette Start→Ende; PDF-Zeiten nur als Anker (Taktpunkte)
        var templateStops = SelectTemplateStopsForTrip(
            editor,
            templateRouteKey,
            fullTemplate,
            startStopHint,
            endStopHint,
            planTimingPoints);

        if (!RouteScheduleTimeCalculator.TryParseTime(startTime, out _))
        {
            return templateStops.Select(s => s.Clone()).ToList();
        }

        var tripStartTime = RouteScheduleTimeCalculator.NormalizeTimeInput(startTime);
        var templateStartTime = ResolveTemplateStartTime(templateStops, tripStartTime);
        var preserveByCode = (preserveFrom ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.PlannerStopCode))
            .GroupBy(s => PlannerStopCode.Normalize(s.PlannerStopCode), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new List<RouteStopItem>();
        for (var i = 0; i < templateStops.Count; i++)
        {
            var stop = templateStops[i].Clone();
            // Doppelte Namensvarianten (z. B. „Höhscheid Brockenberg“ / „Höhscheid-Brockenberg“)
            // teilen dieselbe Minute — nicht aus Vorlagen-Offset +1 Min. ableiten.
            if (i > 0 && AreEquivalentStopNames(result[^1].Name, stop.Name) &&
                !string.IsNullOrWhiteSpace(result[^1].Time))
            {
                stop.Time = result[^1].Time;
            }
            else
            {
                stop.Time = i == 0
                    ? tripStartTime
                    : RouteScheduleTimeCalculator.CalculateStopTime(
                          tripStartTime,
                          templateStartTime,
                          templateStops[i].Time) ?? string.Empty;
            }

            var code = PlannerStopCode.Normalize(stop.PlannerStopCode);
            if (!string.IsNullOrEmpty(code) && preserveByCode.TryGetValue(code, out var prev))
            {
                CopyDestinationFields(prev, stop);
            }

            result.Add(stop);
        }

        if (!string.IsNullOrWhiteSpace(endTime) &&
            RouteScheduleTimeCalculator.TryParseTime(endTime, out _) &&
            result.Count > 0)
        {
            result[^1].Time = RouteScheduleTimeCalculator.NormalizeTimeInput(endTime);
            result[^1].IsEndStop = true;
        }

        var typical = typicalTravelMinutes ??
                      RouteTravelTimeLearner.BuildTypicalMinutes(editor.RouteNames.Select(editor.GetStops));
        ApplyPlanTimingAndFillGaps(result, templateStops, planTimingPoints, typical, tripStartTime, endTime);

        return result;
    }

    /// <summary>
    /// Vorlagenkette Start→Ende inkl. aller Zwischenhalte (PDF listet oft nur Taktpunkte).
    /// Optionale Halte (Mangenberg, Schleife Krahenhöhe) nur wenn in der PDF eine Zeit steht.
    /// Fehlende Spuren (Mangenberg auf 683) aus Donor-Linie (682) einspleißen.
    /// </summary>
    private static List<RouteStopItem> SelectTemplateStopsForTrip(
        EditableRoutePackage editor,
        string primaryRouteKey,
        List<RouteStopItem> fullTemplate,
        string? startStopHint,
        string? endStopHint,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? planTimingPoints)
    {
        // Start = erster Treffer, Ende = letzter (Rundkurs Burger ab → … → Burger an)
        var startIdx = FindStopIndexByHint(fullTemplate, startStopHint);
        var endIdx = FindLastStopIndexByHint(fullTemplate, endStopHint);
        if (startIdx < 0 && planTimingPoints is { Count: > 0 })
        {
            var firstPoint = planTimingPoints
                .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _))
                .OrderBy(p => ToMinutes(p.Time))
                .FirstOrDefault();
            if (firstPoint is not null)
            {
                startIdx = FindStopIndexByHint(fullTemplate, firstPoint.StopHint);
            }
        }

        if (endIdx < 0 && planTimingPoints is { Count: > 0 })
        {
            var lastPoint = planTimingPoints
                .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _))
                .OrderBy(p => ToMinutes(p.Time))
                .LastOrDefault();
            if (lastPoint is not null)
            {
                endIdx = FindLastStopIndexByHint(fullTemplate, lastPoint.StopHint);
            }
        }

        if (startIdx < 0)
        {
            startIdx = 0;
        }

        if (endIdx < 0)
        {
            endIdx = fullTemplate.Count - 1;
        }

        List<RouteStopItem> oriented;
        if (endIdx == startIdx && planTimingPoints is { Count: >= 2 })
        {
            // Gleicher Name Start/Ende ohne zweiten Vorlagen-Halt → ganze Kette, Richtung aus PDF
            oriented = OrientFullTemplateByTimingPoints(fullTemplate, planTimingPoints);
        }
        else if (endIdx < startIdx)
        {
            oriented = fullTemplate
                .Skip(endIdx)
                .Take(startIdx - endIdx + 1)
                .Reverse()
                .Select(s => s.Clone())
                .ToList();
        }
        else
        {
            oriented = fullTemplate
                .Skip(startIdx)
                .Take(endIdx - startIdx + 1)
                .Select(s => s.Clone())
                .ToList();
        }

        if (planTimingPoints is not { Count: > 0 })
        {
            return oriented;
        }

        var orderedPoints = planTimingPoints
            .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _) &&
                        !string.IsNullOrWhiteSpace(p.StopHint))
            .OrderBy(p => ToMinutes(p.Time))
            .ToList();
        if (orderedPoints.Count == 0)
        {
            return oriented;
        }

        // 683 ohne Mangenberg → Segment Rathaus↔Mangenberg aus 682-Vorlage
        oriented = EnrichTemplateWithDonorSpurs(
            editor, primaryRouteKey, oriented, orderedPoints);

        // Volle Vorlagenkette behalten; nur optionale PDF-Halte ohne Zeit streichen
        return PruneOptionalStopsWithoutPlanTime(oriented, orderedPoints);
    }

    /// <summary>
    /// Mangenberg / Schleife Krahenhöhe u. ä.: ohne PDF-Zeit nicht mitnehmen.
    /// Normale Zwischenhalte der Vorlage bleiben (PDF zeigt oft nur Taktpunkte).
    /// </summary>
    private static List<RouteStopItem> PruneOptionalStopsWithoutPlanTime(
        List<RouteStopItem> oriented,
        IReadOnlyList<UmlaufPdfReader.TimingPoint> orderedPoints)
    {
        return oriented
            .Where(stop =>
            {
                if (!IsOptionalUmlaufStop(stop.Name))
                {
                    return true;
                }

                return orderedPoints.Any(p => FindStopIndexByHint([stop], p.StopHint) >= 0);
            })
            .Select(s => s.Clone())
            .ToList();
    }

    private static bool IsOptionalUmlaufStop(string? name)
    {
        var tokens = TokenizeStopHint(name);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Any(t => t.Equals("mangenberg", StringComparison.Ordinal)))
        {
            return true;
        }

        // Schleife Krahenhöhe / Krahenhöhe nur als optionaler Ast (nicht jede Krahenhöhe-Durchfahrt)
        if (tokens.Any(t => t.Equals("schleife", StringComparison.Ordinal)) &&
            tokens.Any(t => t.StartsWith("krahen", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fehlende Planzeit-Halte aus einer anderen Linie einspleißen
    /// (typisch 683: Rathaus→Mangenberg aus 682).
    /// </summary>
    private static List<RouteStopItem> EnrichTemplateWithDonorSpurs(
        EditableRoutePackage editor,
        string primaryRouteKey,
        List<RouteStopItem> oriented,
        IReadOnlyList<UmlaufPdfReader.TimingPoint> orderedPoints)
    {
        var primaryPrefix = LinePrefix(primaryRouteKey);
        var result = oriented.Select(s => s.Clone()).ToList();
        var minIdx = 0;
        string? prevHint = null;

        for (var p = 0; p < orderedPoints.Count; p++)
        {
            var point = orderedPoints[p];
            var used = new HashSet<int>();
            var idx = FindStopIndexByHintFrom(result, point.StopHint, minIdx, used);
            if (idx >= 0)
            {
                minIdx = idx + 1;
                prevHint = point.StopHint;
                continue;
            }

            // Nächster Halt, der schon in der Primärkette liegt (für Start an der Spur)
            string? nextPrimaryHint = null;
            for (var q = p + 1; q < orderedPoints.Count; q++)
            {
                if (FindStopIndexByHintFrom(result, orderedPoints[q].StopHint, minIdx, used) >= 0)
                {
                    nextPrimaryHint = orderedPoints[q].StopHint;
                    break;
                }
            }

            List<RouteStopItem>? segment = null;
            var junctionHint = prevHint;
            if (!string.IsNullOrWhiteSpace(junctionHint))
            {
                // Rathaus → Mangenberg
                segment = TryExtractDonorSegment(
                    editor, primaryPrefix, junctionHint!, point.StopHint);
            }

            if ((segment is null || segment.Count == 0) &&
                !string.IsNullOrWhiteSpace(nextPrimaryHint))
            {
                // Mangenberg → Rathaus (Fahrt beginnt an der Spur)
                segment = TryExtractDonorSegment(
                    editor, primaryPrefix, point.StopHint, nextPrimaryHint!);
                junctionHint = null;
            }

            if (segment is null || segment.Count == 0)
            {
                continue;
            }

            int insertAt;
            if (!string.IsNullOrWhiteSpace(junctionHint))
            {
                var jIdx = -1;
                for (var i = Math.Min(minIdx, result.Count) - 1; i >= 0; i--)
                {
                    if (FindStopIndexByHint([result[i]], junctionHint) >= 0)
                    {
                        jIdx = i;
                        break;
                    }
                }

                if (jIdx < 0)
                {
                    jIdx = Math.Max(0, minIdx - 1);
                }

                // Junction nicht doppelt; Spurhalte inkl. Ziel einfügen
                var toInsert = segment
                    .Skip(1)
                    .Select(s => s.Clone())
                    .ToList();
                if (toInsert.Count == 0)
                {
                    continue;
                }

                result.InsertRange(jIdx + 1, toInsert);
                insertAt = jIdx + toInsert.Count;
            }
            else
            {
                // Am Anfang bzw. vor dem nächsten Primärhalt einfügen
                var nextIdx = FindStopIndexByHintFrom(result, nextPrimaryHint, minIdx, used);
                insertAt = nextIdx >= 0 ? nextIdx : minIdx;
                // Segment endet am nächsten Primärhalt → letzten Skip wenn doppelt
                var toInsert = segment.Select(s => s.Clone()).ToList();
                if (nextIdx >= 0 &&
                    toInsert.Count > 0 &&
                    FindStopIndexByHint([toInsert[^1]], nextPrimaryHint) >= 0)
                {
                    toInsert.RemoveAt(toInsert.Count - 1);
                }

                if (toInsert.Count == 0)
                {
                    continue;
                }

                result.InsertRange(insertAt, toInsert);
                insertAt += toInsert.Count - 1;
            }

            minIdx = Math.Max(minIdx, insertAt + 1);
            prevHint = point.StopHint;
        }

        return result;
    }

    private static List<RouteStopItem>? TryExtractDonorSegment(
        EditableRoutePackage editor,
        string excludeLinePrefix,
        string fromHint,
        string toHint)
    {
        var preferred = PreferredDonorLinePrefixes(excludeLinePrefix);
        List<RouteStopItem>? best = null;
        var bestScore = int.MinValue;

        foreach (var key in editor.RouteNames)
        {
            var prefix = LinePrefix(key);
            if (prefix.Equals(excludeLinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stops = StripDepotStops(GetTemplateStops(editor, key));
            if (stops.Count < 2)
            {
                continue;
            }

            var fromIdx = FindStopIndexByHint(stops, fromHint);
            var toIdx = FindLastStopIndexByHint(stops, toHint);
            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx)
            {
                // ggf. umgekehrte Reihenfolge in derselben Vorlage
                fromIdx = FindLastStopIndexByHint(stops, fromHint);
                toIdx = FindStopIndexByHint(stops, toHint);
            }

            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx)
            {
                continue;
            }

            List<RouteStopItem> segment;
            if (toIdx > fromIdx)
            {
                segment = stops.Skip(fromIdx).Take(toIdx - fromIdx + 1).Select(s => s.Clone()).ToList();
            }
            else
            {
                segment = stops
                    .Skip(toIdx)
                    .Take(fromIdx - toIdx + 1)
                    .Reverse()
                    .Select(s => s.Clone())
                    .ToList();
            }

            // Kurze Spur bevorzugen (Rathaus–Mangenberg, nicht die ganze Linie)
            var score = 100 - segment.Count;
            if (preferred.Any(p => p.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                score += 200;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = segment;
            }
        }

        return best;
    }

    private static string LinePrefix(string routeKey)
    {
        var lc = RouteDisplayHelper.NormalizeLineCourse(
            RouteDisplayHelper.Parse(routeKey).LineCourse);
        if (string.IsNullOrWhiteSpace(lc))
        {
            return string.Empty;
        }

        var slash = lc.IndexOf('/');
        return slash > 0 ? lc[..slash] : lc;
    }

    private static IReadOnlyList<string> PreferredDonorLinePrefixes(string primaryPrefix)
    {
        if (primaryPrefix.Equals("683", StringComparison.OrdinalIgnoreCase))
        {
            return ["682"];
        }

        if (primaryPrefix.Equals("682", StringComparison.OrdinalIgnoreCase))
        {
            return ["681", "683"];
        }

        if (primaryPrefix.Equals("681", StringComparison.OrdinalIgnoreCase))
        {
            return ["682"];
        }

        return [];
    }

    private static List<RouteStopItem> OrientFullTemplateByTimingPoints(
        List<RouteStopItem> fullTemplate,
        IReadOnlyList<UmlaufPdfReader.TimingPoint> planTimingPoints)
    {
        var ordered = planTimingPoints
            .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _))
            .OrderBy(p => ToMinutes(p.Time))
            .ToList();
        if (ordered.Count < 2)
        {
            return fullTemplate.Select(s => s.Clone()).ToList();
        }

        var firstIdx = FindStopIndexByHint(fullTemplate, ordered[0].StopHint);
        var lastIdx = FindLastStopIndexByHint(fullTemplate, ordered[^1].StopHint);
        if (firstIdx >= 0 && lastIdx >= 0 && lastIdx < firstIdx)
        {
            return fullTemplate.AsEnumerable().Reverse().Select(s => s.Clone()).ToList();
        }

        return fullTemplate.Select(s => s.Clone()).ToList();
    }

    /// <summary>
    /// Planzeiten setzen, Zwischenräume proportional/gelernt füllen.
    /// Ergebnis ist streng monoton (keine Rücksprünge, kein 00:01 durch Mitternachts-Wrap).
    /// </summary>
    private static void ApplyPlanTimingAndFillGaps(
        List<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? planTimingPoints,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes,
        string tripStartTime,
        string? endTime)
    {
        if (stops.Count == 0)
        {
            return;
        }

        var anchored = MapPlanTimesToIndices(stops, planTimingPoints);
        if (!anchored.ContainsKey(0) &&
            RouteScheduleTimeCalculator.TryParseTime(tripStartTime, out _))
        {
            anchored[0] = RouteScheduleTimeCalculator.NormalizeTimeInput(tripStartTime);
        }

        if (!string.IsNullOrWhiteSpace(endTime) &&
            RouteScheduleTimeCalculator.TryParseTime(endTime, out var endOnly) &&
            !anchored.ContainsKey(stops.Count - 1))
        {
            var endNorm = RouteScheduleTimeCalculator.NormalizeTimeInput(endTime);
            var canAddEnd = true;
            if (anchored.Count > 0)
            {
                var lastAnchorTime = anchored.OrderBy(kv => kv.Key).Last().Value;
                canAddEnd = RouteScheduleTimeCalculator.TryParseTime(lastAnchorTime, out var lastT) &&
                            TripForwardMinutes(lastT, endOnly) is not null;
            }

            if (canAddEnd)
            {
                anchored[stops.Count - 1] = endNorm;
            }
        }

        // Startzeit nur auf Index 0 → Doppelhalt (Brockenberg) ebenfalls verankern,
        // sonst FillForward/Redistribute → +1 Min. und Verschiebung der ganzen Kette.
        anchored = ExpandAnchorsToEquivalentStops(stops, anchored);
        anchored = SanitizeMonotonicAnchors(anchored);

        foreach (var (idx, time) in anchored)
        {
            stops[idx].Time = time;
        }

        var orderedAnchors = anchored.Keys.OrderBy(i => i).ToList();
        if (orderedAnchors.Count >= 2)
        {
            for (var a = 0; a < orderedAnchors.Count - 1; a++)
            {
                RedistributeSegment(
                    stops,
                    templateStops,
                    orderedAnchors[a],
                    orderedAnchors[a + 1],
                    typicalMinutes);
            }
        }

        var anchoredSet = anchored.Keys.ToHashSet();
        if (orderedAnchors.Count > 0)
        {
            FillForwardFromAnchor(
                stops, templateStops, orderedAnchors[^1], anchoredSet, typicalMinutes);
            // Rückwärts nur bis zum ersten Anker, und nur mit gültigen Tages-Abständen
            FillBackwardToAnchor(
                stops, templateStops, orderedAnchors[0], anchoredSet, typicalMinutes);
        }

        foreach (var (idx, time) in anchored)
        {
            stops[idx].Time = time;
        }

        EnforceMonotonicStopTimes(stops);
        SyncEquivalentConsecutiveStopTimes(stops);
    }

    /// <summary>
    /// Ankerzeit auf benachbarte Halte mit gleichem normalisierten Namen übertragen
    /// (z. B. „Höhscheid Brockenberg“ / „Höhscheid-Brockenberg“ → dieselbe Minute).
    /// </summary>
    private static Dictionary<int, string> ExpandAnchorsToEquivalentStops(
        IReadOnlyList<RouteStopItem> stops,
        Dictionary<int, string> anchored)
    {
        if (anchored.Count == 0 || stops.Count == 0)
        {
            return anchored;
        }

        var result = new Dictionary<int, string>(anchored);
        foreach (var (idx, time) in anchored.OrderBy(kv => kv.Key))
        {
            for (var j = idx + 1; j < stops.Count; j++)
            {
                if (!AreEquivalentStopNames(stops[idx].Name, stops[j].Name))
                {
                    break;
                }

                result[j] = time;
            }

            for (var j = idx - 1; j >= 0; j--)
            {
                if (!AreEquivalentStopNames(stops[idx].Name, stops[j].Name))
                {
                    break;
                }

                result[j] = time;
            }
        }

        return result;
    }

    /// <summary>Aufeinanderfolgende Namens-Äquivalente auf die Zeit des ersten setzen.</summary>
    private static void SyncEquivalentConsecutiveStopTimes(List<RouteStopItem> stops)
    {
        for (var i = 1; i < stops.Count; i++)
        {
            if (!AreEquivalentStopNames(stops[i - 1].Name, stops[i].Name) ||
                string.IsNullOrWhiteSpace(stops[i - 1].Time))
            {
                continue;
            }

            stops[i].Time = stops[i - 1].Time;
        }
    }

    /// <summary>Planzeiten auf Indizes; nur vorwärts in der Kette, Zeiten monoton.</summary>
    private static Dictionary<int, string> MapPlanTimesToIndices(
        IReadOnlyList<RouteStopItem> stops,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? planTimingPoints)
    {
        var map = new Dictionary<int, string>();
        if (planTimingPoints is null || planTimingPoints.Count == 0 || stops.Count == 0)
        {
            return map;
        }

        var used = new HashSet<int>();
        var minIdx = 0;
        TimeOnly? lastTime = null;
        foreach (var point in planTimingPoints
                     .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _))
                     .OrderBy(p => ToMinutes(p.Time)))
        {
            // Nur vorwärts ab minIdx – kein Rückwärts-Fallback (sonst „Rathaus“ → frühere
            // Rathausstraße vor Bf. Mitte → Sanitize verwirft die echte 17:58).
            var idx = FindStopIndexByHintFrom(stops, point.StopHint, minIdx, used);
            if (idx < 0)
            {
                continue;
            }

            if (!RouteScheduleTimeCalculator.TryParseTime(point.Time, out var t))
            {
                continue;
            }

            if (lastTime is TimeOnly prev && TripForwardMinutes(prev, t) is null)
            {
                // Zeit liegt vor dem letzten Anker → überspringen
                continue;
            }

            var timeNorm = RouteScheduleTimeCalculator.NormalizeTimeInput(point.Time);
            map[idx] = timeNorm;
            used.Add(idx);
            // Gleicher Halt, anderer Schreibweise → gleiche Planzeit (nicht nächste Minute)
            for (var j = idx + 1; j < stops.Count; j++)
            {
                if (!AreEquivalentStopNames(stops[idx].Name, stops[j].Name) || used.Contains(j))
                {
                    break;
                }

                map[j] = timeNorm;
                used.Add(j);
            }

            minIdx = Math.Max(minIdx, used.Max() + 1);
            lastTime = t;
        }

        return map;
    }

    /// <summary>Anker entfernen, die zeitlich vor einem früheren Index-Anker liegen.</summary>
    private static Dictionary<int, string> SanitizeMonotonicAnchors(Dictionary<int, string> anchored)
    {
        var result = new Dictionary<int, string>();
        TimeOnly? lastTime = null;
        foreach (var (idx, timeStr) in anchored.OrderBy(kv => kv.Key))
        {
            if (!RouteScheduleTimeCalculator.TryParseTime(timeStr, out var t))
            {
                continue;
            }

            if (lastTime is TimeOnly prev && TripForwardMinutes(prev, t) is null)
            {
                continue;
            }

            result[idx] = RouteScheduleTimeCalculator.NormalizeTimeInput(timeStr);
            lastTime = t;
        }

        return result;
    }

    /// <summary>
    /// Jeder Halt mindestens 1 Min. nach dem vorherigen — außer bei aufeinanderfolgenden
    /// Namens-Äquivalenten (gleiche Minute erlaubt, z. B. Brockenberg-Doppelhalt).
    /// </summary>
    private static void EnforceMonotonicStopTimes(List<RouteStopItem> stops)
    {
        if (stops.Count == 0)
        {
            return;
        }

        if (!RouteScheduleTimeCalculator.TryParseTime(stops[0].Time, out var cursor))
        {
            return;
        }

        stops[0].Time = FormatTime(cursor);
        for (var i = 1; i < stops.Count; i++)
        {
            var sameStop = AreEquivalentStopNames(stops[i - 1].Name, stops[i].Name);
            if (!RouteScheduleTimeCalculator.TryParseTime(stops[i].Time, out var cur) ||
                TripForwardMinutes(cursor, cur) is not int fwd ||
                fwd < (sameStop ? 0 : 1))
            {
                if (sameStop)
                {
                    stops[i].Time = FormatTime(cursor);
                }
                else
                {
                    cursor = cursor.AddMinutes(1);
                    stops[i].Time = FormatTime(cursor);
                }
            }
            else
            {
                cursor = cur;
                stops[i].Time = FormatTime(cursor);
            }
        }
    }

    private static int FindStopIndexByHintFrom(
        IReadOnlyList<RouteStopItem> stops,
        string? hint,
        int fromIndex,
        ISet<int> used)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return -1;
        }

        var hintTokens = TokenizeStopHint(hint);
        if (hintTokens.Count == 0)
        {
            return -1;
        }

        var bestIdx = -1;
        var bestScore = 0;
        var start = Math.Clamp(fromIndex, 0, stops.Count);
        for (var i = start; i < stops.Count; i++)
        {
            if (used.Contains(i))
            {
                continue;
            }

            var score = ScoreTokenOverlap(hintTokens, TokenizeStopHint(stops[i].Name));
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        var minScore = hintTokens.Count >= 2 ? 15 : 8;
        return bestScore >= minScore ? bestIdx : -1;
    }

    private static void RedistributeSegment(
        List<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        int fromIdx,
        int toIdx,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes)
    {
        if (toIdx <= fromIdx + 1)
        {
            return;
        }

        if (!RouteScheduleTimeCalculator.TryParseTime(stops[fromIdx].Time, out var fromTime) ||
            !RouteScheduleTimeCalculator.TryParseTime(stops[toIdx].Time, out var toTime))
        {
            return;
        }

        // Kein Mitternachts-Wrap bei inkonsistenten Ankern (sonst 00:01 / 23h-Segmente)
        if (TripForwardMinutes(fromTime, toTime) is not int span || span <= 0)
        {
            return;
        }

        var nGaps = toIdx - fromIdx;
        // Zwischen PDF-Taktpunkten gleichmäßig verteilen (Bf. Mitte 06:31 → GWP 06:37
        // → Werwolf 06:33, Entenpfuhl 06:35). Vorlagen-3/2/1 und gelernte Mediane aus
        // Plan-Routen erzeugen hier systematisch die falschen Einzelminuten.
        var weights = new int[nGaps];
        Array.Fill(weights, 1);

        // Wenn in bestehenden Routen für jede Lücke dieselbe Minute gelernt wurde
        // (typisch 2/2/2 nach manueller Korrektur), diese Proportion bevorzugen.
        if (TryGetUniformLearnedGaps(
                stops, fromIdx, toIdx, typicalMinutes, out var learnedWeights))
        {
            weights = learnedWeights;
        }

        var gaps = AllocateGapMinutes(span, weights);
        var cursor = fromTime;
        for (var g = 0; g < nGaps - 1; g++)
        {
            cursor = cursor.AddMinutes(gaps[g]);
            stops[fromIdx + 1 + g].Time = FormatTime(cursor);
        }
    }

    /// <summary>
    /// Gelernt nur nutzen, wenn alle Lücken belegt sind und die Werte nah an gleichmäßig liegen
    /// (sonst schlagen alte Vorlagen-Mediane 3/2/1 wieder durch).
    /// </summary>
    private static bool TryGetUniformLearnedGaps(
        IReadOnlyList<RouteStopItem> stops,
        int fromIdx,
        int toIdx,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes,
        out int[] weights)
    {
        var nGaps = toIdx - fromIdx;
        weights = new int[nGaps];
        if (typicalMinutes.Count == 0 || nGaps <= 0)
        {
            return false;
        }

        for (var g = 0; g < nGaps; g++)
        {
            var fromCode = PlannerStopCode.Normalize(stops[fromIdx + g].PlannerStopCode);
            var toCode = PlannerStopCode.Normalize(stops[fromIdx + g + 1].PlannerStopCode);
            if (!PlannerStopCode.IsValid(fromCode) ||
                !PlannerStopCode.IsValid(toCode) ||
                !typicalMinutes.TryGetValue((fromCode, toCode), out var learned) ||
                learned <= 0)
            {
                return false;
            }

            weights[g] = learned;
        }

        var min = weights.Min();
        var max = weights.Max();
        // Erlaubt 2/2/2 oder 2/2/3 – nicht 3/2/1 aus unveränderten Vorlagen
        return max - min <= 1;
    }

    private static void FillForwardFromAnchor(
        List<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        int fromIdx,
        ISet<int> anchoredIndices,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes)
    {
        if (!RouteScheduleTimeCalculator.TryParseTime(stops[fromIdx].Time, out var cursor))
        {
            return;
        }

        for (var i = fromIdx; i < stops.Count - 1; i++)
        {
            if (anchoredIndices.Contains(i + 1))
            {
                RouteScheduleTimeCalculator.TryParseTime(stops[i + 1].Time, out cursor);
                continue;
            }

            if (AreEquivalentStopNames(stops[i].Name, stops[i + 1].Name))
            {
                stops[i + 1].Time = FormatTime(cursor);
                continue;
            }

            var gap = Math.Max(1, ResolveGapMinutes(stops, templateStops, i, i + 1, typicalMinutes));
            cursor = cursor.AddMinutes(gap);
            stops[i + 1].Time = FormatTime(cursor);
        }
    }

    private static void FillBackwardToAnchor(
        List<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        int toIdx,
        ISet<int> anchoredIndices,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes)
    {
        if (!RouteScheduleTimeCalculator.TryParseTime(stops[toIdx].Time, out var cursor))
        {
            return;
        }

        for (var i = toIdx; i > 0; i--)
        {
            if (anchoredIndices.Contains(i - 1))
            {
                RouteScheduleTimeCalculator.TryParseTime(stops[i - 1].Time, out cursor);
                continue;
            }

            if (AreEquivalentStopNames(stops[i - 1].Name, stops[i].Name))
            {
                stops[i - 1].Time = FormatTime(cursor);
                continue;
            }

            var gap = Math.Max(1, ResolveGapMinutes(stops, templateStops, i - 1, i, typicalMinutes));
            var next = cursor.AddMinutes(-gap);
            // Nicht über Mitternacht „nach hinten“ in den Vortag rutschen (→ 00:01-Artefakte)
            if (TripForwardMinutes(next, cursor) is null)
            {
                break;
            }

            cursor = next;
            stops[i - 1].Time = FormatTime(cursor);
        }
    }

    private static int ResolveGapMinutes(
        IReadOnlyList<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        int fromIdx,
        int toIdx,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes)
    {
        if (AreEquivalentStopNames(stops[fromIdx].Name, stops[toIdx].Name))
        {
            return 0;
        }

        var fromCode = PlannerStopCode.Normalize(stops[fromIdx].PlannerStopCode);
        var toCode = PlannerStopCode.Normalize(stops[toIdx].PlannerStopCode);
        if (PlannerStopCode.IsValid(fromCode) &&
            PlannerStopCode.IsValid(toCode) &&
            typicalMinutes.TryGetValue((fromCode, toCode), out var learned) &&
            learned > 0)
        {
            return Math.Max(1, learned);
        }

        return ResolveTemplateGapMinutes(stops, templateStops, fromIdx, toIdx);
    }

    /// <summary>
    /// Abstand laut gewählter Vorlagenkette (gleiche Indizes wie die gebauten Stops).
    /// </summary>
    private static int ResolveTemplateGapMinutes(
        IReadOnlyList<RouteStopItem> stops,
        IReadOnlyList<RouteStopItem> templateStops,
        int fromIdx,
        int toIdx)
    {
        if (fromIdx < 0 ||
            toIdx < 0 ||
            fromIdx >= stops.Count ||
            toIdx >= stops.Count ||
            AreEquivalentStopNames(stops[fromIdx].Name, stops[toIdx].Name))
        {
            return 0;
        }

        if (fromIdx < templateStops.Count &&
            toIdx < templateStops.Count &&
            string.Equals(
                CleanNameKey(stops[fromIdx].Name),
                CleanNameKey(templateStops[fromIdx].Name),
                StringComparison.Ordinal) &&
            string.Equals(
                CleanNameKey(stops[toIdx].Name),
                CleanNameKey(templateStops[toIdx].Name),
                StringComparison.Ordinal) &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[fromIdx].Time, out var tFrom) &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[toIdx].Time, out var tTo))
        {
            var templateGap = TemplateGapMinutes(tFrom, tTo);
            if (templateGap > 0)
            {
                return templateGap;
            }
        }

        // Index-Drift: per Name in der Vorlage suchen
        var iFrom = FindStopIndexByHint(templateStops, stops[fromIdx].Name);
        var iTo = FindStopIndexByHint(templateStops, stops[toIdx].Name);
        if (iFrom >= 0 &&
            iTo >= 0 &&
            iFrom != iTo &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[iFrom].Time, out tFrom) &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[iTo].Time, out tTo))
        {
            var templateGap = TemplateGapMinutes(tFrom, tTo);
            if (templateGap > 0)
            {
                return templateGap;
            }
        }

        return 1;
    }

    /// <summary>
    /// Vorlagen-Abstand zwischen zwei aufeinanderfolgenden Halten (ohne Mitternachts-Wrap).
    /// Bei umgedrehter Kette zählen rückläufige Vorlagenzeiten mit Abs.
    /// </summary>
    private static int TemplateGapMinutes(TimeOnly from, TimeOnly to)
    {
        var a = from.Hour * 60 + from.Minute;
        var b = to.Hour * 60 + to.Minute;
        var delta = Math.Abs(b - a);
        // Benachbarte Hst: typisch 1–15 Min.; Wrap (~23h) verwerfen
        if (delta is <= 0 or > 45)
        {
            return 0;
        }

        return delta;
    }

    private static string CleanNameKey(string? name) =>
        string.Join(' ', TokenizeStopHint(name));

    private static int[] AllocateGapMinutes(int span, IReadOnlyList<int> weights)
    {
        var n = weights.Count;
        var gaps = new int[n];
        if (n == 0)
        {
            return gaps;
        }

        if (span <= 0)
        {
            // Kein Zeitraum – alle 0 (Anker gleich); Enforce setzt ggf. Abstand
            return gaps;
        }

        // Gewicht 0 = Doppelhalt (gleiche Minute) – Minuten nur auf echte Lücken verteilen
        var active = Enumerable.Range(0, n).Where(i => weights[i] > 0).ToList();
        if (active.Count == 0)
        {
            gaps[n - 1] = span;
            return gaps;
        }

        if (span < active.Count)
        {
            for (var i = 0; i < span; i++)
            {
                gaps[active[i % active.Count]]++;
            }

            return gaps;
        }

        var safeWeights = active.Select(i => Math.Max(1, weights[i])).ToArray();
        var weightSum = safeWeights.Sum();
        var exact = safeWeights.Select(w => span * (double)w / weightSum).ToArray();
        for (var a = 0; a < active.Count; a++)
        {
            gaps[active[a]] = Math.Max(1, (int)Math.Floor(exact[a]));
        }

        var sum = gaps.Sum();
        if (sum > span)
        {
            while (sum > span)
            {
                var idx = -1;
                var best = 1;
                foreach (var i in active)
                {
                    if (gaps[i] > best)
                    {
                        best = gaps[i];
                        idx = i;
                    }
                }

                if (idx < 0)
                {
                    break;
                }

                gaps[idx]--;
                sum--;
            }
        }
        else if (sum < span)
        {
            var order = Enumerable.Range(0, active.Count)
                .OrderByDescending(a => exact[a] - gaps[active[a]])
                .Select(a => active[a])
                .ToList();
            var k = 0;
            while (sum < span)
            {
                gaps[order[k % order.Count]]++;
                sum++;
                k++;
            }
        }

        return gaps;
    }

    private static void EnforceMinimumOneMinuteGap(
        List<RouteStopItem> stops,
        ISet<int> anchoredIndices)
    {
        for (var i = 1; i < stops.Count; i++)
        {
            if (!RouteScheduleTimeCalculator.TryParseTime(stops[i - 1].Time, out var prev) ||
                !RouteScheduleTimeCalculator.TryParseTime(stops[i].Time, out var cur))
            {
                continue;
            }

            if (ForwardMinutes(prev, cur) >= 1)
            {
                continue;
            }

            if (anchoredIndices.Contains(i))
            {
                // Anker bleibt; Vorgänger (wenn kein Anker) eine Minute früher
                if (!anchoredIndices.Contains(i - 1))
                {
                    stops[i - 1].Time = FormatTime(cur.AddMinutes(-1));
                }

                continue;
            }

            // Nächsten Anker nicht überschreiten
            var nextAnchorIdx = -1;
            for (var j = i + 1; j < stops.Count; j++)
            {
                if (anchoredIndices.Contains(j))
                {
                    nextAnchorIdx = j;
                    break;
                }
            }

            var candidate = prev.AddMinutes(1);
            if (nextAnchorIdx >= 0 &&
                RouteScheduleTimeCalculator.TryParseTime(stops[nextAnchorIdx].Time, out var nextAnchor) &&
                ForwardMinutes(candidate, nextAnchor) <= 0)
            {
                // Kein Platz bis zum Anker – Segment später neu verteilen; hier Max. vor Anker
                var maxBefore = nextAnchor.AddMinutes(-(nextAnchorIdx - i));
                stops[i].Time = FormatTime(
                    ForwardMinutes(prev, maxBefore) >= 1 ? maxBefore : candidate);
            }
            else
            {
                stops[i].Time = FormatTime(candidate);
            }
        }
    }

    private static int ToMinutes(string? time)
    {
        if (!RouteScheduleTimeCalculator.TryParseTime(time, out var t))
        {
            return 0;
        }

        return t.Hour * 60 + t.Minute;
    }

    private static int ForwardMinutes(TimeOnly from, TimeOnly to)
    {
        var a = from.Hour * 60 + from.Minute;
        var b = to.Hour * 60 + to.Minute;
        if (b >= a)
        {
            return b - a;
        }

        return (24 * 60 - a) + b;
    }

    /// <summary>
    /// Vorwärts-Minuten für eine Tagesfahrt. Rückwärts ohne Mitternachtskontext → null
    /// (verhindert 23h-Segmente und 00:01 beim Auffüllen).
    /// </summary>
    private static int? TripForwardMinutes(TimeOnly from, TimeOnly to)
    {
        var a = from.Hour * 60 + from.Minute;
        var b = to.Hour * 60 + to.Minute;
        if (b >= a)
        {
            return b - a;
        }

        // Nur spät → früh über Mitternacht (Nachtdienst)
        if (a >= 18 * 60 && b <= 12 * 60)
        {
            return (24 * 60 - a) + b;
        }

        return null;
    }

    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Index der Haltestelle, die zum PDF-Hinweis passt (−1 = keine; erster Treffer).</summary>
    public static int FindStopIndexByHint(IReadOnlyList<RouteStopItem> stops, string? hint) =>
        FindStopIndexByHintCore(stops, hint, preferLast: false);

    /// <summary>Wie FindStopIndexByHint, aber letzter Treffer (Endhalt bei Rundkurs).</summary>
    public static int FindLastStopIndexByHint(IReadOnlyList<RouteStopItem> stops, string? hint) =>
        FindStopIndexByHintCore(stops, hint, preferLast: true);

    private static int FindStopIndexByHintCore(
        IReadOnlyList<RouteStopItem> stops,
        string? hint,
        bool preferLast)
    {
        if (string.IsNullOrWhiteSpace(hint) || stops.Count == 0)
        {
            return -1;
        }

        var hintTokens = TokenizeStopHint(hint);
        if (hintTokens.Count == 0)
        {
            return -1;
        }

        var bestIdx = -1;
        var bestScore = 0;
        var minScore = hintTokens.Count >= 2 ? 15 : 8;
        for (var i = 0; i < stops.Count; i++)
        {
            var nameTokens = TokenizeStopHint(stops[i].Name);
            if (nameTokens.Count == 0)
            {
                continue;
            }

            var score = ScoreTokenOverlap(hintTokens, nameTokens);
            if (score < minScore)
            {
                continue;
            }

            if (score > bestScore || (preferLast && score >= bestScore))
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    /// Gleiche Haltestelle unter unterschiedlicher Schreibweise
    /// (z. B. „Höhscheid Brockenberg“ / „Höhscheid-Brockenberg“).
    /// </summary>
    private static bool AreEquivalentStopNames(string? a, string? b)
    {
        var ta = TokenizeStopHint(a);
        var tb = TokenizeStopHint(b);
        if (ta.Count == 0 || tb.Count == 0 || ta.Count != tb.Count)
        {
            return false;
        }

        return ta.OrderBy(t => t, StringComparer.Ordinal)
            .SequenceEqual(tb.OrderBy(t => t, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static List<string> TokenizeStopHint(string? text)
    {
        var raw = (text ?? string.Empty).Trim().ToLowerInvariant();
        raw = raw.Replace("an/ab", " ", StringComparison.Ordinal);
        raw = raw.Replace("ab/an", " ", StringComparison.Ordinal);
        raw = Regex.Replace(raw, @"\b(an|ab)\b", " ");
        raw = raw
            .Replace("hauptbahnhof", "hbf", StringComparison.Ordinal)
            .Replace("gwp", "graf wilhelm platz", StringComparison.Ordinal);
        // „Graf - Wilh. - Pl.“ / „Graf-Wilhelm-Platz“ → gleiche Tokens
        raw = Regex.Replace(raw, @"graf\s*-?\s*wilh(?:elm)?", "graf wilhelm", RegexOptions.IgnoreCase);
        // Umlaute angleichen: Grünewald ↔ Gruenewald
        raw = FoldUmlauts(raw);
        raw = Regex.Replace(raw, @"[^a-z0-9]+", " ");
        var list = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Select(t => t switch
            {
                "pl" => "platz",
                "wilh" => "wilhelm",
                "str" => "strasse",
                "hbf" => "hauptbahnhof",
                "bf" => "bahnhof",
                _ => t
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // „Solingen Hbf“ und „Hauptbahnhof“ matchen über gemeinsames Token
        if (list.Contains("hauptbahnhof", StringComparer.Ordinal) ||
            raw.Contains("hbf", StringComparison.Ordinal))
        {
            if (!list.Contains("hauptbahnhof", StringComparer.Ordinal))
            {
                list.Add("hauptbahnhof");
            }
        }

        // Umlaufkarte: „Brockenberg“ = Haltestelle „Höhscheid-Brockenberg“ (nicht nur Höhscheid)
        if (list.Count == 1 &&
            list[0].Equals("brockenberg", StringComparison.Ordinal))
        {
            list.Insert(0, "hoehscheid");
        }

        return list;
    }

    private static string FoldUmlauts(string value) =>
        (value ?? string.Empty)
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);

    private static int ScoreTokenOverlap(IReadOnlyList<string> hintTokens, IReadOnlyList<string> nameTokens)
    {
        static bool IsStreetSuffixExtension(string shorter, string longer)
        {
            if (!longer.StartsWith(shorter, StringComparison.Ordinal) ||
                longer.Length <= shorter.Length)
            {
                return false;
            }

            var rest = longer[shorter.Length..];
            return rest is "strasse" or "str" or "platz" or "weg" or "allee" or "gasse" or "ring" ||
                   rest.StartsWith("strasse", StringComparison.Ordinal) ||
                   rest.StartsWith("platz", StringComparison.Ordinal);
        }

        static bool TokMatch(string a, string b)
        {
            if (a.Equals(b, StringComparison.Ordinal))
            {
                return true;
            }

            // „rathaus“ ≠ „rathausstrasse“ (False Friend)
            if (a.Length < b.Length && IsStreetSuffixExtension(a, b))
            {
                return false;
            }

            if (b.Length < a.Length && IsStreetSuffixExtension(b, a))
            {
                return false;
            }

            return a.StartsWith(b, StringComparison.Ordinal) ||
                   b.StartsWith(a, StringComparison.Ordinal);
        }

        var matched = 0;
        foreach (var h in hintTokens)
        {
            if (nameTokens.Any(n => TokMatch(h, n)))
            {
                matched++;
            }
        }

        if (matched == 0)
        {
            return 0;
        }

        // „höhscheid“ soll „Höhscheid“ treffen, nicht „Höhscheid-Brockenberg“
        var unmatchedName = nameTokens.Count(n => !hintTokens.Any(h => TokMatch(h, n)));
        return matched * 10 - unmatchedName;
    }

    private static void CopyDestinationFields(RouteStopItem from, RouteStopItem to)
    {
        var name = to.Name;
        var code = to.PlannerStopCode;
        var time = to.Time;
        var route = to.RouteName;
        to.CopyFrom(from);
        to.Name = name;
        to.PlannerStopCode = code;
        to.Time = time;
        to.RouteName = route;
    }

    private static void ApplyTimes(
        EditableRoutePackage editor,
        string displayKey,
        IReadOnlyList<RouteStopItem> templateStops,
        TripSpec trip)
    {
        var newStops = editor.GetStops(displayKey).Where(s => !s.IsWaypoint).ToList();
        var templateStartTime = ResolveTemplateStartTime(templateStops, trip.StartTime);
        var tripStartTime = RouteScheduleTimeCalculator.NormalizeTimeInput(trip.StartTime);

        for (var stopIndex = 0; stopIndex < newStops.Count && stopIndex < templateStops.Count; stopIndex++)
        {
            var stop = newStops[stopIndex];
            stop.RouteName = displayKey;
            stop.Time = stopIndex == 0
                ? tripStartTime
                : RouteScheduleTimeCalculator.CalculateStopTime(
                      tripStartTime,
                      templateStartTime,
                      templateStops[stopIndex].Time) ?? string.Empty;
        }

        if (trip.AnchorTimes is { Count: > 0 })
        {
            ApplyAnchors(newStops, trip.AnchorTimes);
        }

        if (!string.IsNullOrWhiteSpace(trip.EndTime) &&
            RouteScheduleTimeCalculator.TryParseTime(trip.EndTime, out _) &&
            newStops.Count > 0)
        {
            newStops[^1].Time = RouteScheduleTimeCalculator.NormalizeTimeInput(trip.EndTime);
            newStops[^1].IsEndStop = true;
        }
    }

    private static void ApplyAnchors(
        IReadOnlyList<RouteStopItem> stops,
        IReadOnlyDictionary<string, string> anchors)
    {
        foreach (var (anchorName, anchorTime) in anchors)
        {
            var match = FindBestStopMatch(stops, anchorName);
            if (match is null)
            {
                continue;
            }

            match.Time = RouteScheduleTimeCalculator.NormalizeTimeInput(anchorTime);
        }
    }

    private static RouteStopItem? FindBestStopMatch(IReadOnlyList<RouteStopItem> stops, string anchorName)
    {
        var needle = anchorName.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return null;
        }

        var exact = stops.FirstOrDefault(s =>
            string.Equals(s.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var contains = stops
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) &&
                        (s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                         needle.Contains(s.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => Math.Abs((s.Name?.Length ?? 0) - needle.Length))
            .FirstOrDefault();
        return contains;
    }

    private static string ResolveUniqueTripNumber(
        EditableRoutePackage editor,
        string routeName,
        string lineCourse,
        string requestedTripNumber,
        IReadOnlyCollection<DutyOperatingDay> operatingDays,
        ISet<string> reservedTripKeys,
        out bool suffixUsed)
    {
        suffixUsed = false;
        var baseTrip = RouteDisplayHelper.NormalizeTripNumber(requestedTripNumber);
        if (string.IsNullOrEmpty(baseTrip))
        {
            baseTrip = requestedTripNumber.Trim();
        }

        // Basis ohne bereits vorhandenes -N-Suffix für die Zählung
        var stem = StripNumericSuffix(baseTrip);
        for (var n = 0; n < 50; n++)
        {
            var candidate = n == 0 ? baseTrip : $"{stem}-{n + 1}";
            if (n > 0 && string.Equals(candidate, baseTrip, StringComparison.Ordinal))
            {
                continue;
            }

            var reserveKey = BuildTripReserveKey(lineCourse, candidate, operatingDays);
            if (reservedTripKeys.Contains(reserveKey))
            {
                continue;
            }

            var definition = new RouteDefinition(routeName, lineCourse, candidate);
            if (RouteDisplayHelper.HasRouteScheduleConflict(
                    editor.RouteNames,
                    editor.RouteOperatingDaysByRoute,
                    editor.RouteDateRangesByRoute,
                    definition,
                    operatingDays,
                    null,
                    editor.RouteOperatingDatesByRoute,
                    null))
            {
                continue;
            }

            suffixUsed = !string.Equals(candidate, baseTrip, StringComparison.Ordinal);
            return candidate;
        }

        throw new InvalidOperationException(
            $"Keine freie Fahrtnummer für {requestedTripNumber} (Linie/Kurs {lineCourse}).");
    }

    private static IReadOnlyList<DutyOperatingDay> ResolveOperatingDays(Request request, TripSpec trip)
    {
        if (trip.OperatingDays is { Count: > 0 })
        {
            return trip.OperatingDays.Distinct().ToList();
        }

        return request.OperatingDays?.Distinct().ToList() ?? [];
    }

    private static string StripNumericSuffix(string tripNumber)
    {
        var idx = tripNumber.LastIndexOf('-');
        if (idx <= 0 || idx >= tripNumber.Length - 1)
        {
            return tripNumber;
        }

        var suffix = tripNumber[(idx + 1)..];
        return suffix.All(char.IsDigit) ? tripNumber[..idx] : tripNumber;
    }

    private static string BuildTripReserveKey(
        string lineCourse,
        string tripNumber,
        IReadOnlyCollection<DutyOperatingDay> operatingDays)
    {
        var daysPart = string.Join(
            ",",
            operatingDays.Distinct().OrderBy(d => (int)d).Select(d => ((int)d).ToString()));
        return $"{RouteDisplayHelper.NormalizeLineCourse(lineCourse)}|{RouteDisplayHelper.NormalizeTripNumber(tripNumber)}|{daysPart}";
    }

    private static string ResolveTemplateKey(Request request, TripSpec trip)
    {
        if (!string.IsNullOrWhiteSpace(trip.TemplateRouteKey))
        {
            return trip.TemplateRouteKey.Trim();
        }

        if (trip.Direction == Direction.Hin)
        {
            if (string.IsNullOrWhiteSpace(request.HinTemplateRouteKey))
            {
                throw new InvalidOperationException("Keine Vorlage für Hin-Fahrt.");
            }

            return request.HinTemplateRouteKey;
        }

        if (string.IsNullOrWhiteSpace(request.RueckTemplateRouteKey))
        {
            throw new InvalidOperationException("Keine Vorlage für Rück-Fahrt.");
        }

        return request.RueckTemplateRouteKey;
    }

    private static (string RouteName, string LineCourse) ResolveRouteParts(
        string templateKey,
        Request request,
        TripSpec trip)
    {
        var parsed = RouteDisplayHelper.Parse(templateKey);
        var routeName = !string.IsNullOrWhiteSpace(trip.RouteNameOverride)
            ? trip.RouteNameOverride.Trim()
            : !string.IsNullOrWhiteSpace(request.RouteNameOverride)
                ? request.RouteNameOverride.Trim()
                : parsed.Name;
        var lineCourse = !string.IsNullOrWhiteSpace(trip.LineCourseOverride)
            ? RouteDisplayHelper.NormalizeLineCourse(trip.LineCourseOverride)
            : !string.IsNullOrWhiteSpace(request.LineCourseOverride)
                ? RouteDisplayHelper.NormalizeLineCourse(request.LineCourseOverride)
                : !string.IsNullOrWhiteSpace(parsed.LineCourse)
                    ? RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse)
                    : routeName;
        return (routeName, lineCourse);
    }

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

    private static List<string> SplitMainParts(string main)
    {
        if (main.Contains(';'))
        {
            return main.Split(';', StringSplitOptions.TrimEntries).ToList();
        }

        if (main.Contains('\t'))
        {
            return main.Split('\t', StringSplitOptions.TrimEntries).ToList();
        }

        // Komma nur wenn genau 3 Felder (keine Kommas in Namen erwartet)
        var commaParts = main.Split(',', StringSplitOptions.TrimEntries);
        return commaParts.Length == 3 ? commaParts.ToList() : main.Split(';', StringSplitOptions.TrimEntries).ToList();
    }

    private static bool TryParseDirection(string raw, out Direction direction)
    {
        direction = Direction.Hin;
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        value = value
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal);

        if (value is "hin" or "h" or "a" or "->" or "→" or "richtung a" or "richtunga")
        {
            direction = Direction.Hin;
            return true;
        }

        if (value is "rueck" or "ruck" or "r" or "b" or "<-" or "←" or "richtung b" or "richtungb")
        {
            direction = Direction.Rueck;
            return true;
        }

        return false;
    }
}
