using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>KOM-Nachrichten- und Mail-Vorlagen (messageTemplates / mailTemplates).</summary>
public static class MessageTemplatesEditor
{
    public static IList<string> LoadMessageTemplates(JsonObject root) =>
        LoadStringArray(root, "messageTemplates");

    public static IList<string> LoadMailTemplates(JsonObject root) =>
        LoadStringArray(root, "mailTemplates");

    public static void SaveToRoot(JsonObject root, IList<string> messageTemplates, IList<string> mailTemplates)
    {
        SaveStringArray(root, "messageTemplates", messageTemplates);
        SaveStringArray(root, "mailTemplates", mailTemplates);
    }

    public static void Replace(
        EditableRoutePackage package,
        IList<string> messageTemplates,
        IList<string> mailTemplates)
    {
        package.MessageTemplates.Clear();
        package.MailTemplates.Clear();
        foreach (var t in messageTemplates)
        {
            package.MessageTemplates.Add(t);
        }

        foreach (var t in mailTemplates)
        {
            package.MailTemplates.Add(t);
        }
    }

    private static IList<string> LoadStringArray(JsonObject root, string key)
    {
        var list = new List<string>();
        if (root[key] is not JsonArray arr)
        {
            return list;
        }

        foreach (var node in arr)
        {
            var text = node?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    private static void SaveStringArray(JsonObject root, string key, IList<string> items)
    {
        var normalized = items
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            root.Remove(key);
            return;
        }

        var arr = new JsonArray();
        foreach (var text in normalized)
        {
            arr.AddString(text);
        }

        root[key] = arr;
    }
}
