using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core;

/// <summary>Robustes Lesen von JSON-Werten (App-Export nutzt teils Strings statt Zahlen).</summary>
public static class JsonNodeReading
{
    public static int GetInt32(JsonNode? node, int defaultValue = 0)
    {
        if (node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<long>(out var l))
            {
                return (int)l;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (int)Math.Round(d);
            }

            if (value.TryGetValue<string>(out var s) &&
                int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    public static bool GetBoolean(JsonNode? node, bool defaultValue = false)
    {
        if (node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b))
            {
                return b;
            }

            if (value.TryGetValue<string>(out var s))
            {
                var trimmed = s.Trim();
                if (bool.TryParse(trimmed, out var parsed))
                {
                    return parsed;
                }

                if (trimmed == "1")
                {
                    return true;
                }

                if (trimmed == "0")
                {
                    return false;
                }
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i != 0;
            }
        }

        return defaultValue;
    }

    public static double GetDouble(JsonNode? node, double defaultValue = 0)
    {
        if (node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d))
            {
                return d;
            }

            if (value.TryGetValue<long>(out var l))
            {
                return l;
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<string>(out var s) &&
                double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    public static string GetString(JsonNode? node, string defaultValue = "")
    {
        if (node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s))
            {
                return s;
            }

            return value.ToString() ?? defaultValue;
        }

        return node.ToString() ?? defaultValue;
    }
}
