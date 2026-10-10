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

        // Endhalt der Fahrgastfahrt bleibt Endhalt für Routenwechsel; Einfahrt danach
        if (stops.Count > 0)
        {
            stops[^1].IsEndStop = true;
        }

        var stop = CreateSwsDepotStop(editor, routeKey, einfahrtTime, ausfahrt: false, warnings);
        stop.IsEndStop = true;
        stops.Add(stop);
        editor.ReplaceStopsForRoute(routeKey, stops);

        if (string.IsNullOrWhiteSpace(einfahrtTime))
        {
            warnings.Add("SWS Einfahrt ohne Zeit (Einfahrtzeit leer, Vorlage hatte Einfahrt).");
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
            RouteStopEditorCatalog.EnsureStartStopMarker(stop);
        }
        else
        {
            stop.IsEndStop = true;
        }

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
        var templateStops = StripDepotStops(GetTemplateStops(editor, templateRouteKey));
        if (templateStops.Count == 0)
        {
            return [];
        }

        var startIdx = FindStopIndexByHint(templateStops, startStopHint);
        var endIdx = FindStopIndexByHint(templateStops, endStopHint);
        if (startIdx < 0)
        {
            startIdx = 0;
        }

        if (endIdx < 0)
        {
            endIdx = templateStops.Count - 1;
        }

        if (endIdx < startIdx)
        {
            startIdx = 0;
            endIdx = templateStops.Count - 1;
        }

        templateStops = templateStops.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();

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
            stop.Time = i == 0
                ? tripStartTime
                : RouteScheduleTimeCalculator.CalculateStopTime(
                      tripStartTime,
                      templateStartTime,
                      templateStops[i].Time) ?? string.Empty;

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
            // Nur ab minIdx vorwärts – kein Zurückspringen auf frühere Halte
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

            map[idx] = RouteScheduleTimeCalculator.NormalizeTimeInput(point.Time);
            used.Add(idx);
            minIdx = idx + 1;
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

    /// <summary>Jeder Halt mindestens 1 Min. nach dem vorherigen (Anker ggf. korrigieren).</summary>
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
            if (!RouteScheduleTimeCalculator.TryParseTime(stops[i].Time, out var cur) ||
                TripForwardMinutes(cursor, cur) is not int fwd ||
                fwd < 1)
            {
                cursor = cursor.AddMinutes(1);
                stops[i].Time = FormatTime(cursor);
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

        var minScore = hintTokens.Count >= 2 ? 2 : 1;
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
        var weights = new int[nGaps];
        for (var g = 0; g < nGaps; g++)
        {
            weights[g] = ResolveGapMinutes(
                stops,
                templateStops,
                fromIdx + g,
                fromIdx + g + 1,
                typicalMinutes);
        }

        var gaps = AllocateGapMinutes(span, weights);
        var cursor = fromTime;
        for (var g = 0; g < nGaps - 1; g++)
        {
            cursor = cursor.AddMinutes(gaps[g]);
            stops[fromIdx + 1 + g].Time = FormatTime(cursor);
        }
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
        var fromCode = PlannerStopCode.Normalize(stops[fromIdx].PlannerStopCode);
        var toCode = PlannerStopCode.Normalize(stops[toIdx].PlannerStopCode);
        if (PlannerStopCode.IsValid(fromCode) &&
            PlannerStopCode.IsValid(toCode) &&
            typicalMinutes.TryGetValue((fromCode, toCode), out var learned) &&
            learned > 0)
        {
            return Math.Max(1, learned);
        }

        if (fromIdx < templateStops.Count &&
            toIdx < templateStops.Count &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[fromIdx].Time, out var tFrom) &&
            RouteScheduleTimeCalculator.TryParseTime(templateStops[toIdx].Time, out var tTo))
        {
            var templateGap = ForwardMinutes(tFrom, tTo);
            if (templateGap > 0)
            {
                return Math.Max(1, templateGap);
            }
        }

        return 1;
    }

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

        if (span < n)
        {
            // Weniger Minuten als Lücken: so gleichmäßig wie möglich (manche 0)
            for (var i = 0; i < span; i++)
            {
                gaps[i % n]++;
            }

            return gaps;
        }

        var safeWeights = weights.Select(w => Math.Max(1, w)).ToArray();
        var weightSum = safeWeights.Sum();
        var exact = safeWeights.Select(w => span * (double)w / weightSum).ToArray();
        for (var i = 0; i < n; i++)
        {
            gaps[i] = Math.Max(1, (int)Math.Floor(exact[i]));
        }

        var sum = gaps.Sum();
        if (sum > span)
        {
            // Von den größten Lücken reduzieren, aber nicht unter 1
            while (sum > span)
            {
                var idx = -1;
                var best = 1;
                for (var i = 0; i < n; i++)
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
            var order = Enumerable.Range(0, n)
                .OrderByDescending(i => exact[i] - gaps[i])
                .ToList();
            var k = 0;
            while (sum < span)
            {
                gaps[order[k % n]]++;
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

    /// <summary>Index der Haltestelle, die zum PDF-Hinweis passt (−1 = keine).</summary>
    public static int FindStopIndexByHint(IReadOnlyList<RouteStopItem> stops, string? hint)
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
        for (var i = 0; i < stops.Count; i++)
        {
            var nameTokens = TokenizeStopHint(stops[i].Name);
            if (nameTokens.Count == 0)
            {
                continue;
            }

            var score = ScoreTokenOverlap(hintTokens, nameTokens);
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        // Mindestens 2 Treffer-Tokens oder ein eindeutiger längerer Treffer
        var minScore = hintTokens.Count >= 2 ? 2 : 1;
        return bestScore >= minScore ? bestIdx : -1;
    }

    private static List<string> TokenizeStopHint(string? text)
    {
        var raw = (text ?? string.Empty).Trim().ToLowerInvariant();
        raw = raw.Replace("an/ab", " ", StringComparison.Ordinal);
        raw = raw.Replace("ab/an", " ", StringComparison.Ordinal);
        raw = Regex.Replace(raw, @"\b(an|ab)\b", " ");
        raw = raw
            .Replace("hauptbahnhof", "hbf", StringComparison.Ordinal)
            .Replace("graf-wilhelm", "graf wilhelm", StringComparison.Ordinal)
            .Replace("graf wilh", "graf wilhelm", StringComparison.Ordinal)
            .Replace("gwp", "graf wilhelm platz", StringComparison.Ordinal);
        raw = Regex.Replace(raw, @"[^a-z0-9äöüß]+", " ");
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

        return list;
    }

    private static int ScoreTokenOverlap(IReadOnlyList<string> hintTokens, IReadOnlyList<string> nameTokens)
    {
        var score = 0;
        foreach (var h in hintTokens)
        {
            if (nameTokens.Any(n =>
                    n.Equals(h, StringComparison.Ordinal) ||
                    n.StartsWith(h, StringComparison.Ordinal) ||
                    h.StartsWith(n, StringComparison.Ordinal)))
            {
                score++;
            }
        }

        return score;
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
