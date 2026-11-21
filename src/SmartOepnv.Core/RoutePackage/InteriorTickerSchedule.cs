using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public sealed class InteriorTickerEntry
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Lokal, Form yyyy-MM-ddTHH:mm.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Lokal, Form yyyy-MM-ddTHH:mm. Das Ende ist nicht mehr enthalten.</summary>
    public string Until { get; set; } = string.Empty;
}

/// <summary>
/// Zeitfenster für den Liveticker. Eigene Dropbox-Datei, zusätzlich im Routenpaket.
/// </summary>
public static class InteriorTickerSchedule
{
    public const string FileName = "interior_ticker.json";
    public const string ItemsField = "interiorTickerItems";

    public static bool TryParseLocal(string? text, out DateTime local)
    {
        local = default;
        var raw = text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return false;
        }

        string[] formats = ["dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm"];
        return DateTime.TryParseExact(
            raw,
            formats,
            CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.None,
            out local);
    }

    public static string ToStorage(DateTime local) => local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    public static string ToDisplay(string stored)
    {
        return TryParseLocal(stored, out var local)
            ? local.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"))
            : stored;
    }

    public static JsonArray ToJsonArray(IEnumerable<InteriorTickerEntry> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            var text = item.Text?.Trim() ?? string.Empty;
            if (text.Length == 0 ||
                !TryParseLocal(item.From, out var from) ||
                !TryParseLocal(item.Until, out var until) ||
                until <= from ||
                until <= DateTime.Now)
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["text"] = text,
                ["from"] = ToStorage(from),
                ["until"] = ToStorage(until)
            });
        }

        return array;
    }

    public static string BuildFile(string? standardText, IEnumerable<InteriorTickerEntry> items)
    {
        var root = new JsonObject
        {
            ["type"] = "interior_ticker",
            ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["standardText"] = string.IsNullOrWhiteSpace(standardText)
                ? "info@smart-oepnv.de"
                : standardText.Trim(),
            ["items"] = ToJsonArray(items)
        };
        return root.ToJsonString();
    }
}
