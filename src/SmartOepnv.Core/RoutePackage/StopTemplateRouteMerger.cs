namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Übernimmt Haltestellen aus routeStops in die Haltestellenbibliothek (managedStopTemplates).
/// </summary>
public static class StopTemplateRouteMerger
{
    public sealed record MergeResult(int Added, int Enriched, int RouteStopCount);

  private const double CoordinateEpsilon = 0.000001;

    public static MergeResult MergeAllRouteStops(
        IList<ManagedStopTemplateItem> templates,
        EditableRoutePackage editor,
        string? onlyRouteName = null)
    {
        var added = 0;
        var enriched = 0;
        var routeStopCount = 0;
        var usedCodes = new HashSet<string>(
            templates
                .Select(t => PlannerStopCode.Normalize(t.StopCode))
                .Where(c => c.Length == PlannerStopCode.DigitCount),
            StringComparer.Ordinal);

        var index = TemplateIndex.Build(templates);

        var routeNames = string.IsNullOrWhiteSpace(onlyRouteName)
            ? editor.RouteNames
            : editor.RouteNames.Where(r =>
                string.Equals(r, onlyRouteName, StringComparison.OrdinalIgnoreCase));

        foreach (var routeName in routeNames)
        {
            foreach (var stop in editor.GetStops(routeName))
            {
                if (stop.IsWaypoint || string.IsNullOrWhiteSpace(stop.Name))
                {
                    continue;
                }

                routeStopCount++;

                if (index.TryFind(stop, out var existing) && existing is not null)
                {
                    var hadCode = PlannerStopCode.IsValid(PlannerStopCode.Normalize(existing.StopCode));
                    var hadVrr = !string.IsNullOrWhiteSpace(existing.VrrStopId);
                    if (EnrichFromRouteStop(existing, stop))
                    {
                        if (!hadCode || !hadVrr)
                        {
                            index.Index(existing);
                        }

                        enriched++;
                    }

                    continue;
                }

                var tpl = ManagedStopTemplateItem.FromRouteStop(stop);
                CoordinateFormatting.NormalizeTemplate(tpl);

                var code = PlannerStopCode.Normalize(tpl.StopCode);
                if (code.Length != PlannerStopCode.DigitCount)
                {
                    code = PlannerStopCode.SuggestNext(usedCodes);
                    tpl.StopCode = code;
                }

                usedCodes.Add(code);

                templates.Add(tpl);
                index.Index(tpl);
                added++;
            }
        }

        return new MergeResult(added, enriched, routeStopCount);
    }

    /// <summary>
    /// Überträgt Stammdaten aus der Haltestellenbibliothek auf alle passenden Haltestellen in Routen
    /// (gleiche ID, VRR-ID oder Name+Koordinaten). Routenspezifische Felder (Start/Ende, Ziele, …) bleiben unverändert.
    /// </summary>
    public static int ApplyTemplatesToRouteStops(
        EditableRoutePackage editor,
        IEnumerable<ManagedStopTemplateItem> templates)
    {
        var persistable = templates.Where(static t => !t.IsEmptyDraft()).ToList();
        if (persistable.Count == 0)
        {
            return 0;
        }

        var index = TemplateIndex.Build(persistable);
        var updated = 0;
        foreach (var routeName in editor.RouteNames)
        {
            foreach (var stop in editor.GetStops(routeName))
            {
                if (stop.IsWaypoint || !index.TryFind(stop, out var template) || template is null)
                {
                    continue;
                }

                if (ApplySharedFieldsFromTemplate(stop, template, routeName))
                {
                    updated++;
                }
            }
        }

        return updated;
    }

    /// <summary>Fingerprint der Felder, die auf Routen-Haltestellen übertragen werden.</summary>
    public static string ComputeApplyFingerprint(IEnumerable<ManagedStopTemplateItem> templates)
    {
        var parts = templates
            .Where(static t => !t.IsEmptyDraft())
            .OrderBy(static t => t.Id, StringComparer.Ordinal)
            .Select(static t =>
                string.Join(
                    '\u001f',
                    t.Id,
                    PlannerStopCode.Normalize(t.StopCode),
                    t.StopNameItcs.Trim(),
                    t.StopDisplay.Trim(),
                    t.VrrStopId.Trim(),
                    t.AnnouncementLat.Trim(),
                    t.AnnouncementLng.Trim(),
                    t.StopLat.Trim(),
                    t.StopLng.Trim(),
                    t.RadiusMeters.ToString(),
                    t.EmbeddedSoundFileName.Trim()));
        return string.Join('\n', parts);
    }

    private static bool ApplySharedFieldsFromTemplate(
        RouteStopItem stop,
        ManagedStopTemplateItem template,
        string routeName)
    {
        var source = template.ToRouteStop(routeName);
        var changed = false;

        var routeCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        var templateCode = PlannerStopCode.Normalize(template.StopCode);
        if (!PlannerStopCode.IsValid(routeCode))
        {
            if (PlannerStopCode.IsValid(templateCode) &&
                !string.Equals(stop.PlannerStopCode, templateCode, StringComparison.Ordinal))
            {
                stop.PlannerStopCode = templateCode;
                changed = true;
            }
        }
        else if (PlannerStopCode.IsValid(templateCode) &&
                 string.Equals(routeCode, templateCode, StringComparison.Ordinal) &&
                 !string.Equals(stop.PlannerStopCode, templateCode, StringComparison.Ordinal))
        {
            stop.PlannerStopCode = templateCode;
            changed = true;
        }

        if (!string.Equals(stop.Name, source.Name, StringComparison.Ordinal))
        {
            stop.Name = source.Name;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(source.StopDisplay))
        {
            var display = source.StopDisplay.Trim();
            if (!string.Equals(stop.StopDisplay, display, StringComparison.Ordinal))
            {
                stop.StopDisplay = display;
                changed = true;
            }
        }

        if (!string.Equals(stop.VrrStopId, source.VrrStopId, StringComparison.OrdinalIgnoreCase))
        {
            stop.VrrStopId = source.VrrStopId;
            changed = true;
        }

        if (!string.Equals(stop.GpsCoordinates, source.GpsCoordinates, StringComparison.Ordinal))
        {
            stop.GpsCoordinates = source.GpsCoordinates;
            changed = true;
        }

        if (!string.Equals(stop.StopCoordinates, source.StopCoordinates, StringComparison.Ordinal))
        {
            stop.StopCoordinates = source.StopCoordinates;
            changed = true;
        }

        if (stop.Radius != source.Radius)
        {
            stop.Radius = source.Radius;
            changed = true;
        }

        if (!string.Equals(stop.EmbeddedSoundFileName, source.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase))
        {
            stop.EmbeddedSoundFileName = source.EmbeddedSoundFileName;
            changed = true;
        }

        return changed;
    }

    private sealed class TemplateIndex
    {
        private readonly Dictionary<string, ManagedStopTemplateItem> _byCode =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ManagedStopTemplateItem> _byVrr =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ManagedStopTemplateItem>> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        public static TemplateIndex Build(IEnumerable<ManagedStopTemplateItem> templates)
        {
            var index = new TemplateIndex();
            foreach (var template in templates)
            {
                if (!template.IsEmptyDraft())
                {
                    index.Index(template);
                }
            }

            return index;
        }

        public void Index(ManagedStopTemplateItem template)
        {
            var code = PlannerStopCode.Normalize(template.StopCode);
            if (PlannerStopCode.IsValid(code))
            {
                _byCode.TryAdd(code, template);
            }

            var vrr = template.VrrStopId.Trim();
            if (vrr.Length > 0)
            {
                _byVrr.TryAdd(vrr, template);
            }

            var name = template.StopNameItcs.Trim();
            if (name.Length == 0)
            {
                return;
            }

            if (!_byName.TryGetValue(name, out var list))
            {
                list = [];
                _byName[name] = list;
            }

            if (!list.Contains(template))
            {
                list.Add(template);
            }
        }

        public bool TryFind(RouteStopItem stop, out ManagedStopTemplateItem? match)
        {
            var routeCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
            if (PlannerStopCode.IsValid(routeCode))
            {
                if (_byCode.TryGetValue(routeCode, out match))
                {
                    return true;
                }

                match = null;
                return false;
            }

            var vrr = stop.VrrStopId.Trim();
            if (vrr.Length > 0 && _byVrr.TryGetValue(vrr, out match))
            {
                return true;
            }

            var name = stop.Name.Trim();
            if (name.Length > 0 && _byName.TryGetValue(name, out var named))
            {
                foreach (var template in named)
                {
                    if (AnnouncementCoordinatesMatch(template, stop))
                    {
                        match = template;
                        return true;
                    }
                }
            }

            match = null;
            return false;
        }
    }

    private static bool AnnouncementCoordinatesMatch(ManagedStopTemplateItem template, RouteStopItem stop)
    {
        if (CoordinateFormatting.TryParseParts(
                template.AnnouncementLat,
                template.AnnouncementLng,
                out var tLat,
                out var tLon) &&
            CoordinateFormatting.TryParsePair(stop.GpsCoordinates, out var sLatRaw, out var sLonRaw) &&
            CoordinateFormatting.TryParseParts(sLatRaw, sLonRaw, out var sLat, out var sLon))
        {
            return Math.Abs(tLat - sLat) < CoordinateEpsilon &&
                   Math.Abs(tLon - sLon) < CoordinateEpsilon;
        }

        if (!string.IsNullOrWhiteSpace(stop.GpsCoordinates) ||
            CoordinateFormatting.TryParseParts(template.AnnouncementLat, template.AnnouncementLng, out _, out _))
        {
            return false;
        }

        if (CoordinateFormatting.TryParseParts(template.StopLat, template.StopLng, out var tStopLat, out var tStopLon) &&
            CoordinateFormatting.TryParsePair(stop.StopCoordinates, out var rsLatRaw, out var rsLonRaw) &&
            CoordinateFormatting.TryParseParts(rsLatRaw, rsLonRaw, out var rsLat, out var rsLon))
        {
            return Math.Abs(tStopLat - rsLat) < CoordinateEpsilon &&
                   Math.Abs(tStopLon - rsLon) < CoordinateEpsilon;
        }

        return string.IsNullOrWhiteSpace(stop.StopCoordinates) &&
               !CoordinateFormatting.TryParseParts(template.StopLat, template.StopLng, out _, out _);
    }

    private static bool EnrichFromRouteStop(
        ManagedStopTemplateItem template,
        RouteStopItem stop)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(template.StopDisplay) &&
            !string.IsNullOrWhiteSpace(stop.StopDisplay))
        {
            template.StopDisplay = stop.StopDisplay.Trim();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(template.VrrStopId) &&
            !string.IsNullOrWhiteSpace(stop.VrrStopId))
        {
            template.VrrStopId = stop.VrrStopId.Trim();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName) &&
            !string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName))
        {
            template.EmbeddedSoundFileName = stop.EmbeddedSoundFileName.Trim();
            changed = true;
        }

        var routeCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (PlannerStopCode.IsValid(routeCode) &&
            !PlannerStopCode.IsValid(template.StopCode))
        {
            template.StopCode = routeCode;
            changed = true;
        }

        if (!CoordinateFormatting.TryParseParts(template.AnnouncementLat, template.AnnouncementLng, out _, out _) &&
            CoordinateFormatting.TryParsePair(stop.GpsCoordinates, out var lat, out var lon))
        {
            template.AnnouncementLat = lat;
            template.AnnouncementLng = lon;
            changed = true;
        }

        if (!CoordinateFormatting.TryParseParts(template.StopLat, template.StopLng, out _, out _) &&
            CoordinateFormatting.TryParsePair(stop.StopCoordinates, out var stopLat, out var stopLon))
        {
            template.StopLat = stopLat;
            template.StopLng = stopLon;
            changed = true;
        }

        if (template.RadiusMeters <= 0 && stop.Radius > 0)
        {
            template.RadiusMeters = stop.Radius;
            changed = true;
        }

        return changed;
    }
}
