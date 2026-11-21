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

        foreach (var entry in map)
        {
            if (!AnnouncementVoiceFiles.IsVoice2(entry.Value))
            {
                continue;
            }

            if (SameTrip(routeDisplayKey, entry.Key))
            {
                return AnnouncementVoiceFiles.Voice2;
            }
        }

        return string.Empty;
    }

    public static void SetForRoute(IDictionary<string, string> map, string routeDisplayKey, string? voice)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        RemoveSameTrip(map, routeDisplayKey);
        if (!AnnouncementVoiceFiles.IsVoice2(voice))
        {
            return;
        }

        map[key] = AnnouncementVoiceFiles.Voice2;
    }

    public static void RemoveRoute(IDictionary<string, string> map, string routeDisplayKey)
    {
        RemoveSameTrip(map, routeDisplayKey);
    }

    /// <summary>
    /// Dieselbe Fahrt, auch wenn die Fahrtnummer führende Nullen hat
    /// oder der Name ein Datum vorangestellt hat.
    /// </summary>
    internal static bool SameTrip(string? left, string? right)
    {
        var a = RouteDisplayHelper.Parse(RouteDisplayHelper.ToDistributionDisplayString(left ?? string.Empty));
        var b = RouteDisplayHelper.Parse(RouteDisplayHelper.ToDistributionDisplayString(right ?? string.Empty));
        if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(b.Name))
        {
            return false;
        }

        if (!string.Equals(a.TripNumber, b.TripNumber, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
                RouteDisplayHelper.NormalizeLineCourse(a.LineCourse),
                RouteDisplayHelper.NormalizeLineCourse(b.LineCourse),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            RouteDisplayHelper.StripLeadingCalendarDatePrefix(a.Name),
            RouteDisplayHelper.StripLeadingCalendarDatePrefix(b.Name),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveSameTrip(IDictionary<string, string> map, string routeDisplayKey)
    {
        var doomed = map.Keys.Where(key => SameTrip(routeDisplayKey, key)).ToList();
        foreach (var key in doomed)
        {
            map.Remove(key);
        }

        map.Remove(routeDisplayKey.Trim());
        map.Remove(RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey));
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
        foreach (var key in allowedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (!AnnouncementVoiceFiles.IsVoice2(GetForRoute(map, key)))
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
