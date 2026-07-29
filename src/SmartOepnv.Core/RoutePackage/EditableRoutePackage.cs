using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

public sealed class EditableRoutePackage
{
    private JsonObject _root = new();

    /// <summary>
    /// Roh-JSON von <c>embeddedSounds</c> / <c>specialAnnouncements</c> aus dem letzten Laden/Rebuild.
    /// Ermöglicht schnelle lokale Saves ohne erneute Base64-Serialisierung.
    /// </summary>
    private string? _cachedEmbeddedSoundsJson;
    private string? _cachedSpecialAnnouncementsJson;

    public IList<string> RouteNames { get; } = new List<string>();
    public JsonObject PackageRoot => _root;
    public IDictionary<string, IList<RouteStopItem>> StopsByRoute { get; } =
        new Dictionary<string, IList<RouteStopItem>>(StringComparer.Ordinal);

    public IList<EmployeeRosterItem> Employees { get; } = [];
    public IList<RegisteredVehicleItem> RegisteredVehicles { get; } = [];
    public IList<RegisteredVehiclePhoneRedirect> RegisteredVehiclePhoneRedirects { get; } = [];
    public IList<string> MessageTemplates { get; } = [];
    public IList<string> MailTemplates { get; } = [];
    public IList<ManagedStopTemplateItem> StopTemplates { get; } = [];
    public IList<ManagedAnnouncementTemplateItem> AnnouncementTemplates { get; } = [];
    public IList<string> AdditionalAllowedRoutes { get; } = [];
    public IList<DateBasedHintItem> DateBasedHints { get; } = [];
    public IList<string> OutsideDisplays { get; } = [];
    public IDictionary<string, HashSet<DutyOperatingDay>> RouteOperatingDaysByRoute { get; } =
        new Dictionary<string, HashSet<DutyOperatingDay>>(StringComparer.Ordinal);

    public IDictionary<string, RouteDateRange> RouteDateRangesByRoute { get; } =
        new Dictionary<string, RouteDateRange>(StringComparer.Ordinal);

    public IDictionary<string, HashSet<DateOnly>> RouteOperatingDatesByRoute { get; } =
        new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);

    public IDictionary<string, string> RouteInteriorDisplayDestinationsByRoute { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public HashSet<string> RoutesExcludedFromItcsRouteList { get; } =
        new(StringComparer.Ordinal);

    public HashSet<string> RoutesMainDeviceOnly { get; } =
        new(StringComparer.Ordinal);

    public IDictionary<string, string> AutoScheduleSourceByRoute { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static EditableRoutePackage FromJson(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Ungültiges JSON.");
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException("JSON-Wurzel muss ein Objekt sein.");
        }

        var package = new EditableRoutePackage { _root = root };
        package.CaptureHeavyMediaCacheFromSourceJson(json);
        package.ReloadFromRoot();
        return package;
    }

    /// <param name="includeHeavyMedia">
    /// false = nur Routen/Haltestellen/Metadaten (schnell speichern); Audio bleibt im Cache/Sidecar.
    /// </param>
    public string ToJson(bool indented = true, bool rebuildEmbeddedMedia = true, bool includeHeavyMedia = true)
    {
        NormalizeStopsStorageBeforeSave();
        ConsolidateDuplicateRouteKeys();
        SyncToRoot(rebuildEmbeddedMedia);
        _root["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_root["version"] is null)
        {
            _root["version"] = "1.0";
        }

        if (_root["exportType"] is null)
        {
            _root["exportType"] = "routes";
        }

        _root["autoImport"] = true;

        if (!includeHeavyMedia)
        {
            if (rebuildEmbeddedMedia)
            {
                CaptureHeavyMediaCacheFromRootNodes();
            }

            return SerializeRootWithoutHeavyMedia();
        }

        if (!rebuildEmbeddedMedia)
        {
            return SerializeRootPreservingHeavyMediaCache();
        }

        var json = _root.ToJsonString();
        CaptureHeavyMediaCacheFromSourceJson(json);
        return json;
    }

    /// <summary>Sidecar-JSON mit nur <c>embeddedSounds</c>/<c>specialAnnouncements</c> (oder null).</summary>
    public string? TryGetHeavyMediaSidecarJson()
    {
        if (_cachedEmbeddedSoundsJson is null && _cachedSpecialAnnouncementsJson is null)
        {
            CaptureHeavyMediaCacheFromRootNodes();
        }

        if (_cachedEmbeddedSoundsJson is null && _cachedSpecialAnnouncementsJson is null)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder(64
            + (_cachedEmbeddedSoundsJson?.Length ?? 0)
            + (_cachedSpecialAnnouncementsJson?.Length ?? 0));
        sb.Append('{');
        var needsComma = false;
        if (_cachedEmbeddedSoundsJson is not null)
        {
            sb.Append("\"embeddedSounds\":").Append(_cachedEmbeddedSoundsJson);
            needsComma = true;
        }

        if (_cachedSpecialAnnouncementsJson is not null)
        {
            if (needsComma)
            {
                sb.Append(',');
            }

            sb.Append("\"specialAnnouncements\":").Append(_cachedSpecialAnnouncementsJson);
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Cache verwerfen, sobald <c>embeddedSounds</c> inhaltlich geändert wurde.</summary>
    public void InvalidateEmbeddedSoundsJsonCache() => _cachedEmbeddedSoundsJson = null;

    public void InvalidateSpecialAnnouncementsJsonCache() => _cachedSpecialAnnouncementsJson = null;

    private void CaptureHeavyMediaCacheFromSourceJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            _cachedEmbeddedSoundsJson = doc.RootElement.TryGetProperty("embeddedSounds", out var sounds)
                ? sounds.GetRawText()
                : null;
            _cachedSpecialAnnouncementsJson =
                doc.RootElement.TryGetProperty("specialAnnouncements", out var special)
                    ? special.GetRawText()
                    : null;
        }
        catch
        {
            _cachedEmbeddedSoundsJson = null;
            _cachedSpecialAnnouncementsJson = null;
        }
    }

    private void CaptureHeavyMediaCacheFromRootNodes()
    {
        try
        {
            if (_root["embeddedSounds"] is JsonNode sounds)
            {
                _cachedEmbeddedSoundsJson = sounds.ToJsonString();
            }

            if (_root["specialAnnouncements"] is JsonNode special)
            {
                _cachedSpecialAnnouncementsJson = special.ToJsonString();
            }
        }
        catch
        {
            // Cache bleibt unverändert
        }
    }

    private string SerializeRootWithoutHeavyMedia()
    {
        JsonNode? soundsNode = null;
        JsonNode? specialNode = null;
        if (_root.ContainsKey("embeddedSounds"))
        {
            soundsNode = _root["embeddedSounds"];
            _root.Remove("embeddedSounds");
        }

        if (_root.ContainsKey("specialAnnouncements"))
        {
            specialNode = _root["specialAnnouncements"];
            _root.Remove("specialAnnouncements");
        }

        try
        {
            return _root.ToJsonString();
        }
        finally
        {
            if (soundsNode is not null)
            {
                _root["embeddedSounds"] = soundsNode;
            }

            if (specialNode is not null)
            {
                _root["specialAnnouncements"] = specialNode;
            }
        }
    }

    private string SerializeRootPreservingHeavyMediaCache()
    {
        var hasSounds = _root.ContainsKey("embeddedSounds");
        var hasSpecial = _root.ContainsKey("specialAnnouncements");

        if ((hasSounds && _cachedEmbeddedSoundsJson is null) ||
            (hasSpecial && _cachedSpecialAnnouncementsJson is null))
        {
            var full = _root.ToJsonString();
            CaptureHeavyMediaCacheFromSourceJson(full);
            return full;
        }

        var body = SerializeRootWithoutHeavyMedia();
        return InjectHeavyMediaProperties(
            body,
            hasSounds ? _cachedEmbeddedSoundsJson : null,
            hasSpecial ? _cachedSpecialAnnouncementsJson : null);
    }

    public static string InjectHeavyMediaProperties(
        string body,
        string? embeddedSoundsJson,
        string? specialAnnouncementsJson)
    {
        if (embeddedSoundsJson is null && specialAnnouncementsJson is null)
        {
            return body;
        }

        if (body.Length < 2 || body[^1] != '}')
        {
            return body;
        }

        var needsComma = body.Length > 2;
        var extraLength = (embeddedSoundsJson?.Length ?? 0) + (specialAnnouncementsJson?.Length ?? 0) + 64;
        var sb = new System.Text.StringBuilder(body.Length + extraLength);
        sb.Append(body, 0, body.Length - 1);

        void AppendProperty(string name, string valueJson)
        {
            if (needsComma)
            {
                sb.Append(',');
            }

            sb.Append('"').Append(name).Append("\":").Append(valueJson);
            needsComma = true;
        }

        if (embeddedSoundsJson is not null)
        {
            AppendProperty("embeddedSounds", embeddedSoundsJson);
        }

        if (specialAnnouncementsJson is not null)
        {
            AppendProperty("specialAnnouncements", specialAnnouncementsJson);
        }

        sb.Append('}');
        return sb.ToString();
    }

    public void ReloadFromRoot()
    {
        RouteNames.Clear();
        StopsByRoute.Clear();
        Employees.Clear();
        RegisteredVehicles.Clear();
        RegisteredVehiclePhoneRedirects.Clear();
        MessageTemplates.Clear();
        MailTemplates.Clear();
        StopTemplates.Clear();
        AnnouncementTemplates.Clear();
        AdditionalAllowedRoutes.Clear();
        DateBasedHints.Clear();
        OutsideDisplays.Clear();
        RouteOperatingDaysByRoute.Clear();
        RouteDateRangesByRoute.Clear();
        RouteOperatingDatesByRoute.Clear();
        RouteInteriorDisplayDestinationsByRoute.Clear();
        RoutesExcludedFromItcsRouteList.Clear();
        RoutesMainDeviceOnly.Clear();
        AutoScheduleSourceByRoute.Clear();

        if (_root["routes"] is JsonArray routes)
        {
            foreach (var r in routes)
            {
                var name = r?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddRouteNameIfMissing(name);
                }
            }
        }

        LoadRouteNamesFromLineCourseRoutes(_root);

        foreach (var (key, text) in RouteInteriorDisplayDestinationEditor.LoadFromRoot(_root))
        {
            RouteInteriorDisplayDestinationsByRoute[key] = text;
        }

        if (_root["routeStops"] is JsonObject routeStops)
        {
            foreach (var route in routeStops)
            {
                var list = new List<RouteStopItem>();
                if (route.Value is JsonArray stops)
                {
                    foreach (var stopNode in stops)
                    {
                        if (stopNode is JsonObject stopObj)
                        {
                            list.Add(GpsAnsagenStopJson.Parse(stopObj, route.Key));
                        }
                    }
                }

                if (list.Count == 0)
                {
                    continue;
                }

                var storageKey = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(route.Key, StopsByRoute)
                    ?? route.Key;
                if (StopsByRoute.TryGetValue(storageKey, out var existing) && existing.Count > 0)
                {
                    foreach (var stop in list)
                    {
                        existing.Add(stop);
                    }
                }
                else
                {
                    StopsByRoute[storageKey] = list;
                }
            }
        }

        foreach (var name in RouteNames
                     .Where(n => !StopsByRoute.Keys.Any(k => RouteDisplayHelper.RouteKeysMatch(k, n)))
                     .ToList())
        {
            StopsByRoute[name] = new List<RouteStopItem>();
        }

        foreach (var employee in EmployeeRosterEditor.LoadFromRoot(_root))
        {
            Employees.Add(employee);
        }

        foreach (var redirect in RegisteredVehiclesEditor.LoadPhoneRedirectsFromRoot(_root))
        {
            RegisteredVehiclePhoneRedirects.Add(redirect);
        }

        foreach (var vehicle in RegisteredVehiclesEditor.LoadFromRoot(_root))
        {
            RegisteredVehicles.Add(vehicle);
        }

        foreach (var msg in MessageTemplatesEditor.LoadMessageTemplates(_root))
        {
            MessageTemplates.Add(msg);
        }

        foreach (var mail in MessageTemplatesEditor.LoadMailTemplates(_root))
        {
            MailTemplates.Add(mail);
        }

        foreach (var template in ManagedStopTemplateEditor.LoadFromRoot(_root))
        {
            StopTemplates.Add(template);
        }

        foreach (var announcement in ManagedAnnouncementTemplateEditor.LoadFromRoot(_root))
        {
            AnnouncementTemplates.Add(announcement);
        }

        ManagedAnnouncementTemplateEditor.ApplySpecialFlagsFromRoot(_root, AnnouncementTemplates);

        foreach (var hint in DateBasedHintsEditor.LoadFromRoot(_root))
        {
            DateBasedHints.Add(hint);
        }

        foreach (var entry in RoutePackagePhoneMetadata.LoadOutsideDisplays(_root))
        {
            OutsideDisplays.Add(entry);
        }

        var allStops = StopsByRoute.Values.SelectMany(s => s).ToList();
        var exportedRoutes = RouteDistributionRouteCollector.CollectAllRoutesForDistribution(RouteNames, allStops);
        foreach (var extra in RoutePackagePhoneMetadata.LoadAdditionalAllowedRoutes(_root, exportedRoutes))
        {
            AdditionalAllowedRoutes.Add(extra);
        }

        foreach (var (key, days) in RouteOperatingDaysEditor.LoadFromRoot(_root))
        {
            RouteOperatingDaysByRoute[key] = days;
        }

        foreach (var (key, range) in RouteDateRangeEditor.LoadFromRoot(_root))
        {
            RouteDateRangesByRoute[key] = range;
        }

        foreach (var (key, dates) in RouteOperatingDatesEditor.LoadFromRoot(_root))
        {
            RouteOperatingDatesByRoute[key] = dates;
        }

        foreach (var key in RouteItcsRouteListEditor.LoadFromRoot(_root))
        {
            RoutesExcludedFromItcsRouteList.Add(key);
        }

        foreach (var key in RouteMainDeviceOnlyEditor.LoadFromRoot(_root))
        {
            RoutesMainDeviceOnly.Add(key);
        }

        foreach (var (key, source) in AutoScheduleSourceRouteEditor.LoadFromRoot(_root))
        {
            AutoScheduleSourceByRoute[key] = source;
        }

        ConsolidateDuplicateRouteKeys();
        NormalizeRouteDisplayNamesForOperatingDays();
        OutsideDisplayDestinationResolver.EnsureOutsideDisplayIds(this);
        OutsideDisplayDestinationResolver.SyncStopLinks(this);
        EnsureRouteNamesForStopBuckets();
        RecoverOrphanedStopsAfterTripNumberChange();
        PruneOrphanStopBuckets();
    }

    /// <summary>Routenliste ergänzen, wenn Haltestellen unter einem abweichenden Schlüssel liegen.</summary>
    private void EnsureRouteNamesForStopBuckets()
    {
        foreach (var pair in StopsByRoute.Where(entry => entry.Value.Count > 0).ToList())
        {
            if (RouteNames.Any(name => RouteDisplayHelper.RouteKeysMatch(name, pair.Key)))
            {
                continue;
            }

            AddRouteNameIfMissing(pair.Key);
        }
    }

    /// <summary>
    /// Haltestellen unter alter Fahrtnummer wieder an die Route binden (z. B. nach fehlgeschlagener Umbenennung).
    /// </summary>
    private void RecoverOrphanedStopsAfterTripNumberChange()
    {
        var orphans = StopsByRoute
            .Where(pair => pair.Value.Count > 0 &&
                           !RouteNames.Any(name => RouteDisplayHelper.RouteKeysMatch(name, pair.Key)))
            .ToList();

        if (orphans.Count == 0)
        {
            return;
        }

        foreach (var routeName in RouteNames)
        {
            if (GetStops(routeName).Count > 0)
            {
                continue;
            }

            var routeDef = RouteDisplayHelper.Parse(routeName);
            var candidates = orphans
                .Where(pair =>
                {
                    var orphanDef = RouteDisplayHelper.Parse(pair.Key);
                    if (!string.Equals(orphanDef.Name, routeDef.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(routeDef.LineCourse))
                    {
                        return true;
                    }

                    return string.Equals(
                        RouteDisplayHelper.NormalizeLineCourse(orphanDef.LineCourse),
                        RouteDisplayHelper.NormalizeLineCourse(routeDef.LineCourse),
                        StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (candidates.Count != 1)
            {
                continue;
            }

            var orphanKey = candidates[0].Key;
            var stops = candidates[0].Value;
            var storageKey = RouteDisplayHelper.ToCanonicalRouteKey(routeName);

            StopsByRoute.Remove(orphanKey);
            if (StopsByRoute.TryGetValue(storageKey, out var existing) && existing.Count > 0)
            {
                AppendDistinctStops(existing, stops);
            }
            else
            {
                StopsByRoute[storageKey] = stops;
            }

            foreach (var stop in stops)
            {
                stop.RouteName = routeName;
            }

            orphans.RemoveAll(pair => string.Equals(pair.Key, orphanKey, StringComparison.Ordinal));
        }
    }

    private void PruneOrphanStopBuckets()
    {
        foreach (var key in StopsByRoute.Keys.ToList())
        {
            if (!RouteNames.Any(name => RouteDisplayHelper.RouteKeysMatch(name, key)))
            {
                StopsByRoute.Remove(key);
            }
        }
    }

    public IList<RouteStopItem> GetStops(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
        {
            return [];
        }

        var storageKey = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(routeName, StopsByRoute);
        if (storageKey is not null && StopsByRoute.TryGetValue(storageKey, out var stops))
        {
            return stops;
        }

        return [];
    }

    /// <summary>
    /// Übernimmt die aktuelle Haltestellenliste einer Route (z. B. aus der Routen-UI) in den Editor.
    /// </summary>
    public void ReplaceStopsForRoute(string routeKey, IEnumerable<RouteStopItem> stops)
    {
        var trimmed = routeKey.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var list = stops.ToList();
        var storageKey = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(trimmed, StopsByRoute) ?? trimmed;
        foreach (var stop in list)
        {
            stop.RouteName = storageKey;
        }

        StopsByRoute[storageKey] = list;
    }

    /// <summary>Alias-Routenschlüssel zusammenführen (z. B. mit/ohne Verkehrstags-Kennung).</summary>
    public void ConsolidateRouteKeys() => ConsolidateDuplicateRouteKeys();

    public void AddRoute(string routeName)
    {
        var name = routeName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        AddRouteNameIfMissing(name);
        if (!StopsByRoute.ContainsKey(name))
        {
            StopsByRoute[name] = new List<RouteStopItem>();
        }
    }

    public bool TryAddRoute(
        RouteDefinition definition,
        IReadOnlyCollection<DutyOperatingDay>? operatingDays,
        string? copyStopsFromRouteKey,
        out string displayKey,
        out string? error,
        bool inItcsRouteList = false,
        bool mainDeviceOnly = false,
        RouteDateRange? dateRange = null,
        IReadOnlyCollection<DateOnly>? operatingDates = null)
    {
        var days = operatingDays is null
            ? RouteOperatingDaysEditor.AllDays.ToList()
            : operatingDays.Distinct().ToList();
        displayKey = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, days);
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            error = "Bitte geben Sie einen Routennamen ein.";
            return false;
        }

        if (string.IsNullOrEmpty(displayKey))
        {
            error = "Ungültiger Routenname.";
            return false;
        }

        if (days.Count == 0)
        {
            error = "Bitte mindestens einen Verkehrstag auswählen.";
            return false;
        }

        if (RouteNames.Contains(displayKey))
        {
            error = "Route schon vorhanden.";
            return false;
        }

        if (RouteDisplayHelper.HasRouteScheduleConflict(
                RouteNames,
                RouteOperatingDaysByRoute,
                RouteDateRangesByRoute,
                definition,
                days,
                dateRange,
                RouteOperatingDatesByRoute,
                operatingDates))
        {
            error = "Route schon vorhanden (Linie/Kurs, Fahrt, Verkehrstag und/oder Datumsbereich überschneiden sich).";
            return false;
        }

        AddRoute(displayKey);
        SetRouteOperatingDays(displayKey, days);
        SetRouteDateRange(displayKey, dateRange);
        SetRouteOperatingDates(displayKey, operatingDates);
        SetRouteInItcsRouteList(displayKey, inItcsRouteList);
        SetRouteMainDeviceOnly(displayKey, mainDeviceOnly);
        if (!string.IsNullOrWhiteSpace(copyStopsFromRouteKey))
        {
            var sourceKey = copyStopsFromRouteKey.Trim();
            var sourceStops = GetStops(sourceKey);
            if (sourceStops.Count > 0)
            {
                var routeKeyForStops = displayKey;
                var storageKey = RouteDisplayHelper.ToCanonicalRouteKey(routeKeyForStops);
                StopsByRoute[storageKey] = sourceStops
                    .Select(s => CloneStopForRoute(s, routeKeyForStops))
                    .ToList();
                if (!string.Equals(storageKey, routeKeyForStops, StringComparison.Ordinal))
                {
                    StopsByRoute.Remove(routeKeyForStops);
                }

                // Kanonischer Schlüssel (Fahrt: 4, nicht 0004) – sonst findet die Route den Snap nicht.
                RouteNavigationMetadataCopy.CopyForRoute(_root, sourceKey, storageKey);
            }
        }

        error = null;
        return true;
    }

    public bool TryUpdateRoute(
        string existingRouteKey,
        RouteDefinition definition,
        IReadOnlyCollection<DutyOperatingDay> operatingDays,
        bool inItcsRouteList,
        bool mainDeviceOnly,
        out string displayKey,
        out string? error,
        RouteDateRange? dateRange = null,
        IReadOnlyCollection<DateOnly>? operatingDates = null)
    {
        var oldKey = ResolveExistingRouteKey(existingRouteKey);
        if (!RouteNames.Contains(oldKey) && !StopsByRoute.ContainsKey(RouteDisplayHelper.ToCanonicalRouteKey(oldKey)))
        {
            displayKey = string.Empty;
            error = "Route nicht gefunden.";
            return false;
        }

        var days = operatingDays.Distinct().ToList();
        displayKey = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, days);
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            error = "Bitte geben Sie einen Routennamen ein.";
            return false;
        }

        if (string.IsNullOrEmpty(displayKey))
        {
            error = "Ungültiger Routenname.";
            return false;
        }

        if (days.Count == 0)
        {
            error = "Bitte mindestens einen Verkehrstag auswählen.";
            return false;
        }

        var otherRoutes = RouteNames
            .Where(route => !RouteDisplayHelper.RouteKeysMatch(route, oldKey))
            .ToList();
        if (otherRoutes.Contains(displayKey, StringComparer.Ordinal))
        {
            error = "Route schon vorhanden.";
            return false;
        }

        if (RouteDisplayHelper.HasRouteScheduleConflict(
                otherRoutes,
                RouteOperatingDaysByRoute,
                RouteDateRangesByRoute,
                definition,
                days,
                dateRange,
                RouteOperatingDatesByRoute,
                operatingDates))
        {
            error = "Route schon vorhanden (Linie/Kurs, Fahrt, Verkehrstag und/oder Datumsbereich überschneiden sich).";
            return false;
        }

        if (!string.Equals(oldKey, displayKey, StringComparison.Ordinal))
        {
            RenameRouteKey(oldKey, displayKey);
            RouteOperatingDaysEditor.RemoveRoute(RouteOperatingDaysByRoute, oldKey);
            RouteDateRangeEditor.RemoveRoute(RouteDateRangesByRoute, oldKey);
            RouteOperatingDatesEditor.RemoveRoute(RouteOperatingDatesByRoute, oldKey);
        }

        SetRouteOperatingDays(displayKey, days);
        SetRouteDateRange(displayKey, dateRange);
        SetRouteOperatingDates(displayKey, operatingDates);
        SetRouteInItcsRouteList(displayKey, inItcsRouteList);
        SetRouteMainDeviceOnly(displayKey, mainDeviceOnly);
        error = null;
        return true;
    }

    private static RouteStopItem CloneStopForRoute(RouteStopItem source, string routeName)
    {
        var clone = source.Clone();
        clone.RouteName = routeName;
        return clone;
    }

    public void RemoveRoute(string routeName)
    {
        var keysToRemove = RouteNames
            .Where(name => RouteDisplayHelper.RouteKeysMatch(name, routeName))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var key in keysToRemove)
        {
            RouteNames.Remove(key);
            RouteOperatingDaysEditor.RemoveRoute(RouteOperatingDaysByRoute, key);
            RouteDateRangeEditor.RemoveRoute(RouteDateRangesByRoute, key);
            RouteOperatingDatesEditor.RemoveRoute(RouteOperatingDatesByRoute, key);
            RouteInteriorDisplayDestinationEditor.RemoveRoute(RouteInteriorDisplayDestinationsByRoute, key);
            RouteItcsRouteListEditor.RemoveRoute(RoutesExcludedFromItcsRouteList, key);
            RouteMainDeviceOnlyEditor.RemoveRoute(RoutesMainDeviceOnly, key);
            AutoScheduleSourceRouteEditor.RemoveRoute(AutoScheduleSourceByRoute, key);
        }

        foreach (var stopKey in StopsByRoute.Keys
                     .Where(key => RouteDisplayHelper.RouteKeysMatch(key, routeName))
                     .ToList())
        {
            StopsByRoute.Remove(stopKey);
        }

        RoutePackagePhoneMetadata.RemoveRouteKeysFromBlocks(_root, routeName);
        RemoveSimpleRouteNameFromRoot(routeName);
    }

    private void RemoveSimpleRouteNameFromRoot(string routeName)
    {
        if (_root["routes"] is not JsonArray simpleRoutes)
        {
            return;
        }

        for (var i = simpleRoutes.Count - 1; i >= 0; i--)
        {
            var name = simpleRoutes[i]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name) && RouteDisplayHelper.RouteKeysMatch(name, routeName))
            {
                simpleRoutes.RemoveAt(i);
            }
        }

        if (simpleRoutes.Count == 0)
        {
            _root.Remove("routes");
        }
    }

    /// <summary>
    /// Verkehrstage ändern und bei Bedarf den Anzeigenamen (mit/ohne Verkehrstags-Kennung) anpassen.
    /// </summary>
    public string ApplyOperatingDaysChange(string routeDisplayKey, IEnumerable<DutyOperatingDay> days)
    {
        var resolvedKey = ResolveExistingRouteKey(routeDisplayKey);
        var definition = RouteDisplayHelper.Parse(resolvedKey);
        var selectedDays = days.Distinct().ToList();
        var newDisplayKey = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, selectedDays);
        if (!string.Equals(resolvedKey, newDisplayKey, StringComparison.Ordinal))
        {
            RenameRouteKey(resolvedKey, newDisplayKey);
        }

        SetRouteOperatingDays(newDisplayKey, selectedDays);
        return newDisplayKey;
    }

    private string ResolveExistingRouteKey(string routeDisplayKey)
    {
        if (RouteNames.Contains(routeDisplayKey))
        {
            return routeDisplayKey;
        }

        foreach (var name in RouteNames)
        {
            if (RouteDisplayHelper.RouteKeysMatch(name, routeDisplayKey))
            {
                return name;
            }
        }

        return routeDisplayKey.Trim();
    }

    private void RenameRouteKey(string oldKey, string newKey)
    {
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            return;
        }

        var index = RouteNames.IndexOf(oldKey);
        if (index >= 0)
        {
            RouteNames[index] = newKey;
        }
        else if (!RouteNames.Contains(newKey))
        {
            RouteNames.Add(newKey);
        }

        MigrateStopsForRenamedRoute(oldKey, newKey);
        UpdateRouteChangeReferencesForRenamedRoute(oldKey, newKey);

        var interior = RouteInteriorDisplayDestinationEditor.GetForRoute(
            RouteInteriorDisplayDestinationsByRoute,
            oldKey);
        if (!string.IsNullOrEmpty(interior))
        {
            RouteInteriorDisplayDestinationEditor.SetForRoute(
                RouteInteriorDisplayDestinationsByRoute,
                newKey,
                interior);
            RouteInteriorDisplayDestinationEditor.RemoveRoute(RouteInteriorDisplayDestinationsByRoute, oldKey);
        }

        if (!RouteItcsRouteListEditor.IsInItcsRouteList(RoutesExcludedFromItcsRouteList, oldKey))
        {
            RouteItcsRouteListEditor.SetInItcsRouteList(RoutesExcludedFromItcsRouteList, newKey, false);
            RouteItcsRouteListEditor.RemoveRoute(RoutesExcludedFromItcsRouteList, oldKey);
        }

        if (RouteMainDeviceOnlyEditor.IsMainDeviceOnly(RoutesMainDeviceOnly, oldKey))
        {
            RouteMainDeviceOnlyEditor.SetMainDeviceOnly(RoutesMainDeviceOnly, newKey, true);
            RouteMainDeviceOnlyEditor.RemoveRoute(RoutesMainDeviceOnly, oldKey);
        }

        AutoScheduleSourceRouteEditor.RenameRouteKey(AutoScheduleSourceByRoute, oldKey, newKey);
        RouteDateRangeEditor.RenameRouteKey(RouteDateRangesByRoute, oldKey, newKey);
        RouteOperatingDatesEditor.RenameRouteKey(RouteOperatingDatesByRoute, oldKey, newKey);

        RouteNavigationMetadataCopy.CopyForRoute(_root, oldKey, newKey);
        RoutePackagePhoneMetadata.RemoveRouteKeysFromBlocks(_root, oldKey);
    }

    private void MigrateStopsForRenamedRoute(string oldKey, string newKey)
    {
        var keysToMigrate = StopsByRoute.Keys
            .Where(key => RouteDisplayHelper.RouteKeysMatch(key, oldKey))
            .ToList();

        if (keysToMigrate.Count == 0)
        {
            return;
        }

        var mergedStops = new List<RouteStopItem>();
        foreach (var key in keysToMigrate)
        {
            if (StopsByRoute.TryGetValue(key, out var stops) && stops.Count > 0)
            {
                mergedStops.AddRange(stops);
            }

            StopsByRoute.Remove(key);
        }

        var newStorageKey = RouteDisplayHelper.ToCanonicalRouteKey(newKey);
        if (mergedStops.Count == 0)
        {
            return;
        }

        if (StopsByRoute.TryGetValue(newStorageKey, out var existingAtNewKey) && existingAtNewKey.Count > 0)
        {
            AppendDistinctStops(existingAtNewKey, mergedStops);
            foreach (var stop in existingAtNewKey)
            {
                stop.RouteName = newKey;
            }

            return;
        }

        StopsByRoute[newStorageKey] = mergedStops;
        foreach (var stop in mergedStops)
        {
            stop.RouteName = newKey;
        }
    }

    private void UpdateRouteChangeReferencesForRenamedRoute(string oldKey, string newKey)
    {
        var newReference = RouteDisplayHelper.ToDisplayString(RouteDisplayHelper.Parse(newKey));
        foreach (var stop in StopsByRoute.Values.SelectMany(stops => stops))
        {
            if (!stop.RouteChangeEnabled || string.IsNullOrWhiteSpace(stop.SelectedLineCourseTrip))
            {
                continue;
            }

            if (RouteDisplayHelper.RouteKeysMatch(stop.SelectedLineCourseTrip, oldKey))
            {
                stop.SelectedLineCourseTrip = newReference;
            }
        }
    }

    private void NormalizeRouteDisplayNamesForOperatingDays()
    {
        foreach (var routeKey in RouteNames.ToList())
        {
            var days = RouteOperatingDaysEditor.GetDaysForRoute(RouteOperatingDaysByRoute, routeKey);
            if (RouteOperatingDaysEditor.IsConfiguredForAllDays(days))
            {
                continue;
            }

            var definition = RouteDisplayHelper.Parse(routeKey);
            var expectedKey = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, days);
            if (!string.Equals(routeKey, expectedKey, StringComparison.Ordinal))
            {
                RenameRouteKey(routeKey, expectedKey);
            }
        }
    }

    private void ConsolidateDuplicateRouteKeys()
    {
        foreach (var group in RouteNames
                     .GroupBy(RouteDisplayHelper.ToCanonicalRouteKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .ToList())
        {
            var aliases = group.Distinct(StringComparer.Ordinal).ToList();
            var primary = RoutePackageRouteKeyHelper.SelectPrimaryDisplayKey(aliases, StopsByRoute);
            foreach (var alias in aliases)
            {
                if (string.Equals(alias, primary, StringComparison.Ordinal))
                {
                    continue;
                }

                MergeRouteAliasInto(alias, primary);
            }
        }

        foreach (var group in StopsByRoute.Keys
                     .GroupBy(RouteDisplayHelper.ToCanonicalRouteKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .ToList())
        {
            var aliases = group.Distinct(StringComparer.Ordinal).ToList();
            var primary = RoutePackageRouteKeyHelper.SelectPrimaryDisplayKey(aliases, StopsByRoute);
            foreach (var alias in aliases)
            {
                if (string.Equals(alias, primary, StringComparison.Ordinal))
                {
                    continue;
                }

                if (StopsByRoute.TryGetValue(alias, out var aliasStops) && aliasStops.Count > 0)
                {
                    MergeRouteStopBuckets(alias, primary);
                }
                else
                {
                    StopsByRoute.Remove(alias);
                }
            }
        }
    }

    private void MergeRouteAliasInto(string alias, string primary)
    {
        MergeRouteStopBuckets(alias, primary);

        RouteNames.Remove(alias);

        var aliasInterior = RouteInteriorDisplayDestinationEditor.GetForRoute(
            RouteInteriorDisplayDestinationsByRoute,
            alias);
        if (!string.IsNullOrEmpty(aliasInterior) &&
            string.IsNullOrEmpty(RouteInteriorDisplayDestinationEditor.GetForRoute(
                RouteInteriorDisplayDestinationsByRoute,
                primary)))
        {
            RouteInteriorDisplayDestinationEditor.SetForRoute(
                RouteInteriorDisplayDestinationsByRoute,
                primary,
                aliasInterior);
        }

        RouteInteriorDisplayDestinationEditor.RemoveRoute(RouteInteriorDisplayDestinationsByRoute, alias);
        AutoScheduleSourceRouteEditor.RenameRouteKey(AutoScheduleSourceByRoute, alias, primary);
        RouteOperatingDatesEditor.RenameRouteKey(RouteOperatingDatesByRoute, alias, primary);
        RouteNavigationMetadataCopy.CopyForRoute(_root, alias, primary);
        RoutePackagePhoneMetadata.RemoveRouteKeysFromBlocks(_root, alias);
    }

    public HashSet<DutyOperatingDay> GetRouteOperatingDays(string routeDisplayKey) =>
        RouteOperatingDaysEditor.GetDaysForRoute(RouteOperatingDaysByRoute, routeDisplayKey);

    public void SetRouteOperatingDays(string routeDisplayKey, IEnumerable<DutyOperatingDay> days) =>
        RouteOperatingDaysEditor.SetDaysForRoute(RouteOperatingDaysByRoute, routeDisplayKey, days);

    public RouteDateRange GetRouteDateRange(string routeDisplayKey) =>
        RouteDateRangeEditor.GetRangeForRoute(RouteDateRangesByRoute, routeDisplayKey);

    public void SetRouteDateRange(string routeDisplayKey, RouteDateRange? range) =>
        RouteDateRangeEditor.SetRangeForRoute(RouteDateRangesByRoute, routeDisplayKey, range);

    public HashSet<DateOnly> GetRouteOperatingDates(string routeDisplayKey) =>
        RouteOperatingDatesEditor.GetDatesForRoute(RouteOperatingDatesByRoute, routeDisplayKey);

    public void SetRouteOperatingDates(string routeDisplayKey, IEnumerable<DateOnly>? dates) =>
        RouteOperatingDatesEditor.SetDatesForRoute(RouteOperatingDatesByRoute, routeDisplayKey, dates);

    public string GetRouteInteriorDisplayDestination(string routeDisplayKey) =>
        RouteInteriorDisplayDestinationEditor.GetForRoute(RouteInteriorDisplayDestinationsByRoute, routeDisplayKey);

    public void SetRouteInteriorDisplayDestination(string routeDisplayKey, string? text) =>
        RouteInteriorDisplayDestinationEditor.SetForRoute(RouteInteriorDisplayDestinationsByRoute, routeDisplayKey, text);

    public bool IsRouteInItcsRouteList(string routeDisplayKey) =>
        RouteItcsRouteListEditor.IsInItcsRouteList(RoutesExcludedFromItcsRouteList, routeDisplayKey);

    public void SetRouteInItcsRouteList(string routeDisplayKey, bool inList) =>
        RouteItcsRouteListEditor.SetInItcsRouteList(RoutesExcludedFromItcsRouteList, routeDisplayKey, inList);

    public bool IsRouteMainDeviceOnly(string routeDisplayKey) =>
        RouteMainDeviceOnlyEditor.IsMainDeviceOnly(RoutesMainDeviceOnly, routeDisplayKey);

    public void SetRouteMainDeviceOnly(string routeDisplayKey, bool mainDeviceOnly) =>
        RouteMainDeviceOnlyEditor.SetMainDeviceOnly(RoutesMainDeviceOnly, routeDisplayKey, mainDeviceOnly);

    public string GetAutoScheduleSourceRoute(string routeDisplayKey) =>
        AutoScheduleSourceRouteEditor.GetSourceRoute(AutoScheduleSourceByRoute, routeDisplayKey);

    public void SetAutoScheduleSourceRoute(string routeDisplayKey, string sourceRouteKey) =>
        AutoScheduleSourceRouteEditor.SetSourceRoute(AutoScheduleSourceByRoute, routeDisplayKey, sourceRouteKey);

    public bool TryCopyNavigationDataFromAutoScheduleSource(string routeDisplayKey, out string? error)
    {
        var targetKey = ResolveExistingRouteKey(routeDisplayKey);
        var sourceKey = GetAutoScheduleSourceRoute(targetKey);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            error = "Nur für per Fahrplan vervielfältigte Fahrten verfügbar.";
            return false;
        }

        var resolvedSource = ResolveExistingRouteKey(sourceKey);
        if (!RouteNames.Any(name => RouteDisplayHelper.RouteKeysMatch(name, resolvedSource)))
        {
            error = "Fahrplan-Vorlagen-Route wurde nicht gefunden.";
            return false;
        }

        if (!RouteNavigationMetadataCopy.HasNavigationData(_root, resolvedSource))
        {
            error = "Die Vorlagen-Route hat keine Navidaten.";
            return false;
        }

        RouteNavigationMetadataCopy.CopyForRoute(_root, resolvedSource, targetKey);
        error = null;
        return true;
    }

    public void AddStop(string routeName, RouteStopItem? template = null)
    {
        var storageKey = ResolveStopStorageKey(routeName);
        var stop = template ?? new RouteStopItem { RouteName = storageKey, Name = "Neue Haltestelle" };
        stop.RouteName = storageKey;
        stop.PlannerStopCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (string.IsNullOrWhiteSpace(stop.PlannerStopCode))
        {
            stop.PlannerStopCode = PlannerStopCode.SuggestNext(
                StopsByRoute.Values.SelectMany(s => s).Select(s => s.PlannerStopCode)
                    .Concat(StopTemplates.Select(t => t.StopCode)));
        }

        GetOrCreateStopList(storageKey).Add(stop);
    }

    public void AddStopFromTemplate(string routeName, ManagedStopTemplateItem template)
    {
        AddStop(routeName, template.ToRouteStop(routeName));
    }

    public void RemoveStop(string routeName, RouteStopItem stop)
    {
        GetStops(routeName).Remove(stop);
    }

    public bool TryMoveStop(string routeName, RouteStopItem stop, int direction)
    {
        if (direction is not (-1) and not 1)
        {
            return false;
        }

        var list = GetStops(routeName);
        var index = list.IndexOf(stop);
        if (index < 0)
        {
            return false;
        }

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= list.Count)
        {
            return false;
        }

        list.RemoveAt(index);
        list.Insert(newIndex, stop);
        return true;
    }

    private void AddRouteNameIfMissing(string routeName)
    {
        if (!RouteNames.Contains(routeName))
        {
            RouteNames.Add(routeName);
        }
    }

    private void LoadRouteNamesFromLineCourseRoutes(JsonObject root)
    {
        if (root["lineCourseRoutes"] is not JsonObject lineCourseRoutes)
        {
            return;
        }

        var operatingDays = root[RouteOperatingDaysEditor.RootFieldName] is JsonObject map
            ? RouteOperatingDaysEditor.LoadFromRoot(root)
            : new Dictionary<string, HashSet<DutyOperatingDay>>(StringComparer.Ordinal);

        foreach (var group in lineCourseRoutes)
        {
            if (group.Value is not JsonArray routes)
            {
                continue;
            }

            foreach (var routeNode in routes.OfType<JsonObject>())
            {
                var definition = new RouteDefinition(
                    routeNode["name"]?.GetValue<string>() ?? string.Empty,
                    routeNode["lineCourse"]?.GetValue<string>() ?? string.Empty,
                    routeNode["tripNumber"]?.GetValue<string>() ?? string.Empty,
                    routeNode["passengerDisplayLine"]?.GetValue<string>() ?? string.Empty);
                var interiorDestination = routeNode["interiorDestinationText"]?.GetValue<string>()?.Trim();
                var display = RouteDisplayHelper.ToDisplayString(definition);
                if (string.IsNullOrWhiteSpace(display))
                {
                    continue;
                }

                var days = RouteOperatingDaysEditor.GetDaysForRoute(operatingDays, display);
                if (!RouteOperatingDaysEditor.IsConfiguredForAllDays(days))
                {
                    display = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, days);
                }

                if (RouteNames.Any(existing => RouteDisplayHelper.RouteKeysMatch(existing, display)))
                {
                    if (!string.IsNullOrEmpty(interiorDestination))
                    {
                        var existingKey = RouteNames.First(existing =>
                            RouteDisplayHelper.RouteKeysMatch(existing, display));
                        RouteInteriorDisplayDestinationEditor.SetForRoute(
                            RouteInteriorDisplayDestinationsByRoute,
                            existingKey,
                            interiorDestination);
                    }

                    continue;
                }

                AddRouteNameIfMissing(display);
                if (!string.IsNullOrEmpty(interiorDestination))
                {
                    RouteInteriorDisplayDestinationEditor.SetForRoute(
                        RouteInteriorDisplayDestinationsByRoute,
                        display,
                        interiorDestination);
                }
            }
        }
    }

    private void SyncToRoot(bool rebuildEmbeddedMedia = true)
    {
        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        GpsAnsagenRouteExportSync.ApplyToPackage(this, _root, workspace, rebuildEmbeddedMedia);
    }

    public void ReplaceStopTemplates(IList<ManagedStopTemplateItem> templates)
    {
        StopTemplates.Clear();
        foreach (var t in templates)
        {
            StopTemplates.Add(t);
        }
    }

    public void ReplaceAnnouncementTemplates(IList<ManagedAnnouncementTemplateItem> templates)
    {
        AnnouncementTemplates.Clear();
        foreach (var t in templates)
        {
            AnnouncementTemplates.Add(t);
        }
    }

    public void SyncEmbeddedSoundsFromTemplates(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        LocalWorkspaceStore? workspace = null)
    {
        SyncEmbeddedSoundsFromFileNames(
            templates.Select(t => ((string?)t.EmbeddedSoundFileName, t.LocalAudioPath)),
            workspace);
    }

    public void SyncEmbeddedSoundsFromStopTemplates(
        IEnumerable<ManagedStopTemplateItem> templates,
        LocalWorkspaceStore? workspace = null)
    {
        SyncEmbeddedSoundsFromFileNames(
            templates.Select(t => ((string?)t.EmbeddedSoundFileName, t.LocalAudioPath)),
            workspace);
    }

    /// <summary>
    /// true, wenn mindestens ein Ton neu aus Datei/Workspace in <c>embeddedSounds</c> muss –
    /// false bei reiner Dateinamen-Verknüpfung auf bereits eingebettete Töne.
    /// </summary>
    public bool NeedsEmbeddedSoundMaterialization(
        IEnumerable<(string? FileName, string? LocalPath)> items,
        LocalWorkspaceStore? workspace = null)
    {
        var existingNames = new HashSet<string>(
            EmbeddedSoundsEditor.ListFileNames(_root),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (fileName, localPath) in items)
        {
            var name = fileName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                return true;
            }

            if (existingNames.Contains(name))
            {
                continue;
            }

            if (workspace is not null &&
                PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, name) is not null)
            {
                return true;
            }
        }

        return false;
    }

    public bool NeedsEmbeddedSoundMaterialization(
        IEnumerable<ManagedStopTemplateItem> templates,
        LocalWorkspaceStore? workspace = null) =>
        NeedsEmbeddedSoundMaterialization(
            templates.Select(t => ((string?)t.EmbeddedSoundFileName, t.LocalAudioPath)),
            workspace);

    private void SyncEmbeddedSoundsFromFileNames(
        IEnumerable<(string? FileName, string? LocalPath)> items,
        LocalWorkspaceStore? workspace)
    {
        var existingNames = new HashSet<string>(
            EmbeddedSoundsEditor.ListFileNames(_root),
            StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var (fileName, localPath) in items)
        {
            var name = fileName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                EmbeddedSoundsEditor.UpsertFromFile(_root, name, localPath);
                existingNames.Add(name);
                changed = true;
                if (workspace is not null)
                {
                    CopyToWorkspace(workspace, name, localPath);
                }

                continue;
            }

            if (existingNames.Contains(name))
            {
                continue;
            }

            if (workspace is not null)
            {
                var wsPath = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, name);
                if (wsPath is not null)
                {
                    EmbeddedSoundsEditor.UpsertFromFile(_root, name, wsPath);
                    existingNames.Add(name);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            InvalidateEmbeddedSoundsJsonCache();
        }
    }

    private static void CopyToWorkspace(LocalWorkspaceStore workspace, string fileName, string sourcePath)
    {
        try
        {
            var target = Path.Combine(PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(workspace), fileName);
            File.Copy(sourcePath, target, overwrite: true);
        }
        catch
        {
            // Workspace-Kopie optional
        }
    }

    public void ReplaceEmployees(IList<EmployeeRosterItem> employees)
    {
        Employees.Clear();
        foreach (var e in employees.Where(x => !x.IsDeprecatedDefaultCredential()))
        {
            Employees.Add(e);
        }
    }

    public void ReplaceRegisteredVehicles(IList<RegisteredVehicleItem> vehicles)
    {
        RegisteredVehicles.Clear();
        foreach (var v in vehicles)
        {
            RegisteredVehicles.Add(v);
        }
    }

    public void ReplaceRegisteredVehiclePhoneRedirects(IList<RegisteredVehiclePhoneRedirect> redirects)
    {
        RegisteredVehiclePhoneRedirects.Clear();
        foreach (var r in redirects)
        {
            RegisteredVehiclePhoneRedirects.Add(r);
        }
    }

    public void ReplaceDateBasedHints(IList<DateBasedHintItem> hints)
    {
        DateBasedHints.Clear();
        foreach (var h in hints)
        {
            DateBasedHints.Add(h);
        }
    }

    public void ReplaceOutsideDisplays(IList<string> entries)
    {
        OutsideDisplays.Clear();
        foreach (var e in entries)
        {
            OutsideDisplays.Add(e);
        }
    }

    public void ReplaceMessageTemplates(IList<string> messageTemplates, IList<string> mailTemplates)
    {
        MessageTemplates.Clear();
        MailTemplates.Clear();
        foreach (var t in messageTemplates)
        {
            MessageTemplates.Add(t);
        }

        foreach (var t in mailTemplates)
        {
            MailTemplates.Add(t);
        }
    }

    private void NormalizeStopsStorageBeforeSave()
    {
        foreach (var group in RouteNames
                     .GroupBy(RouteDisplayHelper.ToCanonicalRouteKey, StringComparer.OrdinalIgnoreCase))
        {
            var routeAliases = group.Distinct(StringComparer.Ordinal).ToList();
            var stopKeys = StopsByRoute.Keys
                .Where(key => RouteDisplayHelper.RouteKeysMatch(key, routeAliases[0]))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (stopKeys.Count <= 1)
            {
                continue;
            }

            var primary = RoutePackageRouteKeyHelper.SelectPrimaryDisplayKey(routeAliases, StopsByRoute);
            foreach (var aliasKey in stopKeys)
            {
                if (string.Equals(aliasKey, primary, StringComparison.Ordinal))
                {
                    continue;
                }

                MergeRouteStopBuckets(aliasKey, primary);
            }
        }
    }

    private void MergeRouteStopBuckets(string alias, string primary)
    {
        if (!StopsByRoute.TryGetValue(alias, out var aliasStops))
        {
            return;
        }

        if (aliasStops.Count == 0)
        {
            StopsByRoute.Remove(alias);
            return;
        }

        if (!StopsByRoute.TryGetValue(primary, out var primaryStops) || primaryStops.Count == 0)
        {
            StopsByRoute[primary] = aliasStops;
        }
        else if (!ReferenceEquals(primaryStops, aliasStops))
        {
            AppendDistinctStops(primaryStops, aliasStops);
            StopsByRoute.Remove(alias);
        }
        else
        {
            StopsByRoute.Remove(alias);
        }

        if (StopsByRoute.TryGetValue(primary, out var merged))
        {
            foreach (var stop in merged)
            {
                stop.RouteName = primary;
            }
        }
    }

    private static void AppendDistinctStops(IList<RouteStopItem> target, IEnumerable<RouteStopItem> source)
    {
        var seenCodes = new HashSet<string>(
            target
                .Select(stop => PlannerStopCode.Normalize(stop.PlannerStopCode))
                .Where(code => code.Length > 0),
            StringComparer.Ordinal);

        foreach (var stop in source)
        {
            var code = PlannerStopCode.Normalize(stop.PlannerStopCode);
            if (code.Length > 0)
            {
                if (!seenCodes.Add(code))
                {
                    continue;
                }
            }

            target.Add(stop);
        }
    }

    private string ResolveStopStorageKey(string routeName)
    {
        var trimmed = routeName.Trim();
        return RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(trimmed, StopsByRoute) ?? trimmed;
    }

    private IList<RouteStopItem> GetOrCreateStopList(string storageKey)
    {
        if (!StopsByRoute.TryGetValue(storageKey, out var list))
        {
            list = new List<RouteStopItem>();
            StopsByRoute[storageKey] = list;
        }

        return list;
    }

}
