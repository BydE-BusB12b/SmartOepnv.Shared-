using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Ansagestimme pro Route. Gespeichert wird nur Stimme 2 (<c>2</c>);
/// fehlender Eintrag ist Linguatec Petra.
/// </summary>
public static class RouteAnnouncementVoiceEditor
{
    public const string RootFieldName = "routeAnnouncementVoices";

    public static Dictionary<string, string> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            var voice = entry.Value?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(entry.Key) || !AnnouncementVoiceFiles.IsVoice2(voice))
            {
                continue;
            }

            result[entry.Key.Trim()] = AnnouncementVoiceFiles.Voice2;
        }

        return result;
    }

    public static string GetForRoute(IDictionary<string, string> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var voice) && AnnouncementVoiceFiles.IsVoice2(voice))
        {
            return AnnouncementVoiceFiles.Voice2;
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out voice) && AnnouncementVoiceFiles.IsVoice2(voice))
        {
            return AnnouncementVoiceFiles.Voice2;
        }

        return string.Empty;
    }

    public static void SetForRoute(IDictionary<string, string> map, string routeDisplayKey, string? voice)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(routeDisplayKey.Trim());
        if (!AnnouncementVoiceFiles.IsVoice2(voice))
        {
            map.Remove(key);
            return;
        }

        map[key] = AnnouncementVoiceFiles.Voice2;
    }

    public static void RemoveRoute(IDictionary<string, string> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, string> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key) || !AnnouncementVoiceFiles.IsVoice2(entry.Value))
            {
                continue;
            }

            obj[key] = AnnouncementVoiceFiles.Voice2;
        }

        if (obj.Count == 0)
        {
            root.Remove(RootFieldName);
        }
        else
        {
            root[RootFieldName] = obj;
        }
    }
}
