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

                if (TryFindMatch(templates, stop, out var existing))
                {
                    if (EnrichFromRouteStop(existing!, stop))
                    {
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
        var updated = 0;
        foreach (var template in templates)
        {
            if (template.IsEmptyDraft())
            {
                continue;
            }

            foreach (var routeName in editor.RouteNames)
            {
                foreach (var stop in editor.GetStops(routeName))
                {
                    if (stop.IsWaypoint || !MatchesRouteStop(template, stop))
                    {
                        continue;
                    }

                    if (ApplySharedFieldsFromTemplate(stop, template, routeName))
                    {
                        updated++;
                    }
                }
            }
        }

        return updated;
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

    private static bool TryFindMatch(
        IEnumerable<ManagedStopTemplateItem> templates,
        RouteStopItem stop,
        out ManagedStopTemplateItem? match)
    {
        foreach (var template in templates)
        {
            if (MatchesRouteStop(template, stop))
            {
                match = template;
                return true;
            }
        }

        match = null;
        return false;
    }

    private static bool MatchesRouteStop(ManagedStopTemplateItem template, RouteStopItem stop)
    {
        var routeCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        var templateCode = PlannerStopCode.Normalize(template.StopCode);

        // Route hat bereits eine Kartei-ID (z. B. nach „In Route einfügen“) –
        // nur dieselbe Vorlage darf synchronisieren (Hin-/Rückfahrt teilen oft VRR/Name).
        if (PlannerStopCode.IsValid(routeCode))
        {
            return PlannerStopCode.IsValid(templateCode) &&
                   string.Equals(routeCode, templateCode, StringComparison.Ordinal);
        }

        var vrrRoute = stop.VrrStopId.Trim();
        var vrrTemplate = template.VrrStopId.Trim();
        if (vrrRoute.Length > 0 &&
            vrrTemplate.Length > 0 &&
            string.Equals(vrrRoute, vrrTemplate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(
                template.StopNameItcs.Trim(),
                stop.Name.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AnnouncementCoordinatesMatch(template, stop);
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
