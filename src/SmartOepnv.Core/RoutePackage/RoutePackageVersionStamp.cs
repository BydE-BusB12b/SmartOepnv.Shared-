using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Aufsteigende Paketversionen für <c>routes_export.json</c> und <c>routes_update.json</c>.
/// Geräte prüfen damit, ob eine Version schon installiert ist und installieren bei beiden Neu-Dateien
/// zuerst Export (Ansagen), danach Update.
/// </summary>
public static class RoutePackageVersionStamp
{
    public const string JsonKey = "packageVersion";
    public const string KindKey = "packageKind";
    public const string KindExport = "export";
    public const string KindUpdate = "update";

    public enum Kind
    {
        Export,
        Update
    }

    public static string Stamp(string json, Kind kind)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            !AppServices.IsPlannerApp ||
            AppServices.PlanerAppSettings is null)
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject root)
            {
                return json;
            }

            Stamp(root, kind);
            return root.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Stellt sicher, dass Fahrzeug-Pakete (<c>routes_export</c> / <c>routes_update</c>)
    /// eine aufsteigende <c>packageVersion</c> haben. Fehlt sie, wird gestempelt;
    /// vorhandene Versionen bleiben unverändert (kein Doppelzählen).
    /// </summary>
    public static string EnsureStamped(string json, Kind kind)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            !AppServices.IsPlannerApp ||
            AppServices.PlanerAppSettings is null)
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject root)
            {
                return json;
            }

            if (TryReadPackageVersion(root) > 0)
            {
                return json;
            }

            Stamp(root, kind);
            return root.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    public static long TryReadPackageVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            var node = JsonNode.Parse(json);
            return node is JsonObject root ? TryReadPackageVersion(root) : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static long TryReadPackageVersion(JsonObject root)
    {
        if (root[JsonKey] is null)
        {
            return 0;
        }

        try
        {
            return root[JsonKey]!.GetValue<long>();
        }
        catch
        {
            return 0;
        }
    }

    public static void Stamp(JsonObject root, Kind kind)
    {
        if (!AppServices.IsPlannerApp || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        var store = AppServices.PlanerAppSettings;
        var settings = store.Load();
        long next = kind switch
        {
            Kind.Export => settings.LastRoutesExportPackageVersion + 1,
            Kind.Update => settings.LastRoutesUpdatePackageVersion + 1,
            _ => 1
        };
        if (next < 1)
        {
            next = 1;
        }

        switch (kind)
        {
            case Kind.Export:
                settings.LastRoutesExportPackageVersion = next;
                break;
            case Kind.Update:
                settings.LastRoutesUpdatePackageVersion = next;
                break;
        }

        store.Save(settings);

        root[JsonKey] = next;
        root[KindKey] = kind == Kind.Export ? KindExport : KindUpdate;
        root["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (root["exportType"] is null)
        {
            root["exportType"] = "routes";
        }

        if (root["version"] is null)
        {
            root["version"] = "1.0";
        }

        if (root["autoImport"] is null)
        {
            root["autoImport"] = true;
        }
    }
}
