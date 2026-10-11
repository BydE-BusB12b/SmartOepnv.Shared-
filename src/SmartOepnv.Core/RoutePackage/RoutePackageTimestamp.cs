using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Setzt <c>timestamp</c> im JSON auf die aktuelle Zeit – wie Android <c>jsonWithUploadTimestamp</c>.
/// Für Planer-Uploads bitte [RoutePackageVersionStamp] nutzen (enthält Timestamp + packageVersion).
/// </summary>
public static class RoutePackageTimestamp
{
    public static string WithUploadTimestamp(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
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

            return root.ToJsonString();
        }
        catch
        {
            return json;
        }
    }
}
