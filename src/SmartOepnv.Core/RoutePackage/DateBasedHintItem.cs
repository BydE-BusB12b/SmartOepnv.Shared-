using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public sealed class DateBasedHintItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string HintText { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public bool IsShown { get; set; }

    public static DateBasedHintItem FromJson(JsonObject obj) =>
        new()
        {
            Id = JsonNodeReading.GetString(obj["id"], Guid.NewGuid().ToString()),
            HintText = JsonNodeReading.GetString(obj["hintText"]),
            StartDate = JsonNodeReading.GetString(obj["startDate"]),
            EndDate = JsonNodeReading.GetString(obj["endDate"]),
            IsShown = JsonNodeReading.GetBoolean(obj["isShown"])
        };

    public JsonObject ToJson() =>
        new()
        {
            ["id"] = Id,
            ["hintText"] = HintText,
            ["startDate"] = StartDate,
            ["endDate"] = EndDate,
            ["isShown"] = IsShown
        };
}

public static class DateBasedHintsEditor
{
    public static IList<DateBasedHintItem> LoadFromRoot(JsonObject root)
    {
        if (root["dateBasedHints"] is not JsonArray arr)
        {
            return [];
        }

        var list = new List<DateBasedHintItem>();
        foreach (var node in arr)
        {
            if (node is JsonObject obj)
            {
                list.Add(DateBasedHintItem.FromJson(obj));
            }
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<DateBasedHintItem> hints)
    {
        if (hints.Count == 0)
        {
            root.Remove("dateBasedHints");
            return;
        }

        var arr = new JsonArray();
        foreach (var hint in hints)
        {
            arr.Add(hint.ToJson());
        }

        root["dateBasedHints"] = arr;
    }
}
