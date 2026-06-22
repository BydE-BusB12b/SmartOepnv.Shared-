using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public static class ManagedStopTemplateEditor
{
    public static IList<ManagedStopTemplateItem> LoadFromRoot(JsonObject root)
    {
        var list = new List<ManagedStopTemplateItem>();
        if (root["managedStopTemplates"] is not JsonArray arr)
        {
            return list;
        }

        foreach (var node in arr.OfType<JsonObject>())
        {
            list.Add(Parse(node));
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<ManagedStopTemplateItem> templates)
    {
        var arr = new JsonArray();
        foreach (var t in templates)
        {
            arr.Add(Write(t));
        }

        root["managedStopTemplates"] = arr;
    }

    private static ManagedStopTemplateItem Parse(JsonObject obj)
    {
        var id = obj["id"]?.GetValue<string>();
        return new ManagedStopTemplateItem
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            StopCode = PlannerStopCode.Normalize(
                obj["stopCode"]?.GetValue<string>() ?? obj["code"]?.GetValue<string>()),
            StopNameItcs = obj["stopNameItcs"]?.GetValue<string>() ?? string.Empty,
            StopDisplay = obj["stopDisplay"]?.GetValue<string>() ?? string.Empty,
            VrrStopId = obj["vrrStopId"]?.GetValue<string>() ?? string.Empty,
            DirectionDescription = obj["directionDescription"]?.GetValue<string>() ?? string.Empty,
            Lines = obj["lines"]?.GetValue<string>() ?? string.Empty,
            AnnouncementLat = obj["announcementLat"]?.GetValue<string>() ?? string.Empty,
            AnnouncementLng = obj["announcementLng"]?.GetValue<string>() ?? string.Empty,
            StopLat = obj["stopLat"]?.GetValue<string>() ?? string.Empty,
            StopLng = obj["stopLng"]?.GetValue<string>() ?? string.Empty,
            RadiusMeters = obj["radiusMeters"]?.GetValue<int>() ?? ManagedStopTemplateItem.DefaultRadiusMeters,
            ExternalSoundUri = obj["externalSoundUri"]?.GetValue<string>() ?? string.Empty,
            EmbeddedSoundFileName = obj["embeddedSoundFileName"]?.GetValue<string>() ?? string.Empty
        };
    }

    private static JsonObject Write(ManagedStopTemplateItem t) => new()
    {
        ["id"] = t.Id,
        ["stopCode"] = PlannerStopCode.Normalize(t.StopCode),
        ["stopNameItcs"] = t.StopNameItcs,
        ["stopDisplay"] = t.StopDisplay,
        ["vrrStopId"] = t.VrrStopId,
        ["directionDescription"] = t.DirectionDescription,
        ["lines"] = t.Lines,
        ["announcementLat"] = t.AnnouncementLat,
        ["announcementLng"] = t.AnnouncementLng,
        ["stopLat"] = t.StopLat,
        ["stopLng"] = t.StopLng,
        ["radiusMeters"] = t.RadiusMeters,
        ["externalSoundUri"] = t.ExternalSoundUri,
        ["embeddedSoundFileName"] = t.EmbeddedSoundFileName
    };
}
