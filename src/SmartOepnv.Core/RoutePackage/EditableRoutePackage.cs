using System.Text.Json.Nodes;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

public sealed class EditableRoutePackage
{
    private JsonObject _root = new();

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

    public static EditableRoutePackage FromJson(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Ungültiges JSON.");
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException("JSON-Wurzel muss ein Objekt sein.");
        }

        var package = new EditableRoutePackage { _root = root };
        package.ReloadFromRoot();
        return package;
    }

    public string ToJson(bool indented = true)
    {
        SyncToRoot();
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

        return _root.ToJsonString();
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

        if (_root["routeStops"] is JsonObject routeStops)
        {
            foreach (var route in routeStops)
            {
                AddRouteNameIfMissing(route.Key);
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

                StopsByRoute[route.Key] = list;
            }
        }

        foreach (var name in RouteNames.Where(n => !StopsByRoute.ContainsKey(n)).ToList())
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
    }

    public IList<RouteStopItem> GetStops(string routeName) =>
        StopsByRoute.TryGetValue(routeName, out var stops) ? stops : new List<RouteStopItem>();

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
        out string? error)
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

        if (RouteDisplayHelper.HasOperatingDayConflict(RouteNames, RouteOperatingDaysByRoute, definition, days))
        {
            error = "Route schon vorhanden (Linie/Kurs, Fahrt und Verkehrstag überschneiden sich).";
            return false;
        }

        AddRoute(displayKey);
        SetRouteOperatingDays(displayKey, days);
        if (!string.IsNullOrWhiteSpace(copyStopsFromRouteKey) &&
            StopsByRoute.TryGetValue(copyStopsFromRouteKey.Trim(), out var sourceStops))
        {
            var sourceKey = copyStopsFromRouteKey.Trim();
            var targetKey = displayKey;
            StopsByRoute[targetKey] = sourceStops.Select(s => CloneStopForRoute(s, targetKey)).ToList();
            RouteNavigationMetadataCopy.CopyForRoute(_root, sourceKey, targetKey);
        }

        error = null;
        return true;
    }

    private static RouteStopItem CloneStopForRoute(RouteStopItem source, string routeName) =>
        new()
        {
            PlannerStopCode = source.PlannerStopCode,
            Name = source.Name,
            RouteName = routeName,
            GpsCoordinates = source.GpsCoordinates,
            StopCoordinates = source.StopCoordinates,
            Radius = source.Radius,
            VrrStopId = source.VrrStopId,
            StopDisplay = source.StopDisplay,
            Time = source.Time,
            IsWaypoint = source.IsWaypoint,
            WaypointName = source.WaypointName,
            IsAnnouncementEnabled = source.IsAnnouncementEnabled,
            EmbeddedSoundFileName = source.EmbeddedSoundFileName,
            Destination = source.Destination,
            Ds003aDestination = source.Ds003aDestination,
            LineNumber = source.LineNumber,
            EndDestination = source.EndDestination,
            Ds003aEndDestination = source.Ds003aEndDestination,
            IsEndStop = source.IsEndStop,
            PlayEndStopAnnouncement = source.PlayEndStopAnnouncement,
            RouteChangeEnabled = source.RouteChangeEnabled,
            SelectedLineCourseTrip = source.SelectedLineCourseTrip,
            EndDestinationCoordinates = source.EndDestinationCoordinates,
            IsDisplayEnabled = source.IsDisplayEnabled,
            DisplayText = source.DisplayText,
            DisplayText2 = source.DisplayText2,
            DisplayText3 = source.DisplayText3,
            UseDisplayText2 = source.UseDisplayText2,
            UseDisplayText3 = source.UseDisplayText3,
            DisplayInterval = source.DisplayInterval,
            NextStop = source.NextStop,
            Abstand = source.Abstand
        };

    public void RemoveRoute(string routeName)
    {
        RouteNames.Remove(routeName);
        StopsByRoute.Remove(routeName);
        RouteOperatingDaysEditor.RemoveRoute(RouteOperatingDaysByRoute, routeName);
    }

    public HashSet<DutyOperatingDay> GetRouteOperatingDays(string routeDisplayKey) =>
        RouteOperatingDaysEditor.GetDaysForRoute(RouteOperatingDaysByRoute, routeDisplayKey);

    public void SetRouteOperatingDays(string routeDisplayKey, IEnumerable<DutyOperatingDay> days) =>
        RouteOperatingDaysEditor.SetDaysForRoute(RouteOperatingDaysByRoute, routeDisplayKey, days);

    public void AddStop(string routeName, RouteStopItem? template = null)
    {
        if (!StopsByRoute.ContainsKey(routeName))
        {
            StopsByRoute[routeName] = new List<RouteStopItem>();
        }

        var stop = template ?? new RouteStopItem { RouteName = routeName, Name = "Neue Haltestelle" };
        stop.RouteName = routeName;
        stop.PlannerStopCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (string.IsNullOrWhiteSpace(stop.PlannerStopCode))
        {
            stop.PlannerStopCode = PlannerStopCode.SuggestNext(
                StopsByRoute.Values.SelectMany(s => s).Select(s => s.PlannerStopCode)
                    .Concat(StopTemplates.Select(t => t.StopCode)));
        }

        StopsByRoute[routeName].Add(stop);
    }

    public void AddStopFromTemplate(string routeName, ManagedStopTemplateItem template)
    {
        AddStop(routeName, template.ToRouteStop(routeName));
    }

    public void RemoveStop(string routeName, RouteStopItem stop)
    {
        if (StopsByRoute.TryGetValue(routeName, out var list))
        {
            list.Remove(stop);
        }
    }

    public bool TryMoveStop(string routeName, RouteStopItem stop, int direction)
    {
        if (direction is not (-1) and not 1 ||
            !StopsByRoute.TryGetValue(routeName, out var list))
        {
            return false;
        }

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

    private void SyncToRoot()
    {
        var workspace = AppServices.IsInitialized ? AppServices.Workspace : null;
        GpsAnsagenRouteExportSync.ApplyToPackage(this, _root, workspace);
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

    private void SyncEmbeddedSoundsFromFileNames(
        IEnumerable<(string? FileName, string? LocalPath)> items,
        LocalWorkspaceStore? workspace)
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
                EmbeddedSoundsEditor.UpsertFromFile(_root, name, localPath);
                existingNames.Add(name);
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
                }
            }
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

}
