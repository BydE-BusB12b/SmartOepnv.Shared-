using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public static class TicketTemplateEditor
{
    public const string RootKey = "ticketTemplate";

    public static TicketTemplateDocument LoadFromRoot(JsonObject root)
    {
        if (root[RootKey] is not JsonObject o)
        {
            return TicketTemplateDocument.CreateDefault();
        }

        var doc = new TicketTemplateDocument
        {
            Version = o["version"]?.GetValue<int>() ?? 1,
            PageWidthMm = ReadDouble(o, "pageWidthMm", 58),
            PageHeightMm = ReadDouble(o, "pageHeightMm", 50),
            BackgroundColor = o["backgroundColor"]?.GetValue<string>()?.Trim() is { Length: > 0 } bg
                ? bg
                : "#FFFFFF"
        };

        if (o["fields"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject f) continue;
                var type = f["type"]?.GetValue<string>()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(type)) continue;
                doc.Fields.Add(new TicketTemplateFieldItem
                {
                    Id = f["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Type = type,
                    X = ReadDouble(f, "x", 0),
                    Y = ReadDouble(f, "y", 0),
                    Width = ReadDouble(f, "width", 20),
                    Height = ReadDouble(f, "height", 8),
                    FontSize = ReadDouble(f, "fontSize", 12),
                    FontWeight = f["fontWeight"]?.GetValue<string>()?.Trim() is { Length: > 0 } fw
                        ? fw
                        : "normal",
                    Align = f["align"]?.GetValue<string>()?.Trim() is { Length: > 0 } al
                        ? al
                        : "left",
                    StaticText = f["staticText"]?.GetValue<string>() ?? string.Empty,
                    AssetKey = f["assetKey"]?.GetValue<string>() ?? string.Empty
                });
            }
        }

        if (o["logoAssets"] is JsonObject assets)
        {
            foreach (var kv in assets)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                var b64 = kv.Value?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(b64)) continue;
                try
                {
                    doc.LogoAssets[Path.GetFileName(kv.Key.Trim())] = Convert.FromBase64String(b64);
                }
                catch
                {
                    // ungültiges Base64 überspringen
                }
            }
        }

        if (doc.Fields.Count == 0)
        {
            return TicketTemplateDocument.CreateDefault();
        }

        // Ältere Vorlagen ohne Stempelbereich: Streifen rechts ergänzen (Layout sonst unverändert).
        if (!doc.Fields.Any(f =>
                string.Equals(f.Type, "stampStripes", StringComparison.OrdinalIgnoreCase)))
        {
            var stripeW = 11d;
            var stripeX = Math.Max(0, doc.PageWidthMm - stripeW - 1);
            doc.Fields.Insert(0, new TicketTemplateFieldItem
            {
                Id = "stampStripes",
                Type = "stampStripes",
                X = stripeX,
                Y = 1,
                Width = stripeW,
                Height = Math.Max(8, doc.PageHeightMm - 2)
            });
        }

        return doc;
    }

    public static void SaveToRoot(JsonObject root, TicketTemplateDocument doc)
    {
        var fields = new JsonArray();
        foreach (var f in doc.Fields)
        {
            fields.Add(new JsonObject
            {
                ["id"] = f.Id,
                ["type"] = f.Type,
                ["x"] = f.X,
                ["y"] = f.Y,
                ["width"] = f.Width,
                ["height"] = f.Height,
                ["fontSize"] = f.FontSize,
                ["fontWeight"] = f.FontWeight,
                ["align"] = f.Align,
                ["staticText"] = f.StaticText,
                ["assetKey"] = f.AssetKey
            });
        }

        var logoAssets = new JsonObject();
        foreach (var kv in doc.LogoAssets)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null || kv.Value.Length == 0) continue;
            logoAssets[Path.GetFileName(kv.Key.Trim())] = Convert.ToBase64String(kv.Value);
        }

        var obj = new JsonObject
        {
            ["version"] = doc.Version <= 0 ? 1 : doc.Version,
            ["pageWidthMm"] = doc.PageWidthMm,
            ["pageHeightMm"] = doc.PageHeightMm,
            ["unit"] = "mm",
            ["backgroundColor"] = doc.BackgroundColor,
            ["fields"] = fields
        };
        if (logoAssets.Count > 0)
            obj["logoAssets"] = logoAssets;

        root[RootKey] = obj;
    }

    public static TicketTemplateDocument Clone(TicketTemplateDocument source)
    {
        var doc = new TicketTemplateDocument
        {
            Version = source.Version,
            PageWidthMm = source.PageWidthMm,
            PageHeightMm = source.PageHeightMm,
            BackgroundColor = source.BackgroundColor
        };
        foreach (var f in source.Fields)
        {
            doc.Fields.Add(CloneField(f));
        }
        foreach (var kv in source.LogoAssets)
        {
            doc.LogoAssets[kv.Key] = kv.Value.ToArray();
        }
        return doc;
    }

    public static TicketTemplateFieldItem CloneField(TicketTemplateFieldItem f) => new()
    {
        Id = f.Id,
        Type = f.Type,
        X = f.X,
        Y = f.Y,
        Width = f.Width,
        Height = f.Height,
        FontSize = f.FontSize,
        FontWeight = f.FontWeight,
        Align = f.Align,
        StaticText = f.StaticText,
        AssetKey = f.AssetKey
    };

    private static double ReadDouble(JsonObject o, string key, double fallback)
    {
        if (o[key] is null) return fallback;
        try
        {
            return o[key]!.GetValue<double>();
        }
        catch
        {
            if (double.TryParse(
                    o[key]?.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var d))
            {
                return d;
            }
            return fallback;
        }
    }
}
