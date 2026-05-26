using System.Text.Json.Nodes;

namespace SmartOepnv.Core;

public static class JsonNodeExtensions
{
    /// <summary>
    /// Adds a string without using JsonArray.Add&lt;T&gt;, which breaks ToJsonString(options) in .NET 8+.
    /// </summary>
    public static void AddString(this JsonArray array, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        array.Add((JsonNode)value);
    }

    public static string? DraftNodeToJsonText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject)
        {
            return node.ToJsonString();
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith('{'))
            {
                return text;
            }
        }

        return node.ToJsonString();
    }
}
