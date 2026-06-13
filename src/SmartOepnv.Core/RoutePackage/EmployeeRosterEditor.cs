using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

public static class EmployeeRosterEditor
{
    public static IList<EmployeeRosterItem> LoadFromRoot(JsonObject root)
    {
        var list = new List<EmployeeRosterItem>();
        if (root["employeeRoster"] is not JsonArray arr)
        {
            return list;
        }

        foreach (var node in arr.OfType<JsonObject>())
        {
            var item = Parse(node);
            if (item.IsDeprecatedDefaultCredential()) continue;
            if (string.IsNullOrWhiteSpace(item.Name) && string.IsNullOrWhiteSpace(item.PersonnelNumber))
            {
                continue;
            }

            list.Add(item);
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<EmployeeRosterItem> employees)
    {
        var arr = new JsonArray();
        foreach (var e in employees.Where(x => !x.IsDeprecatedDefaultCredential()))
        {
            arr.Add(Write(e));
        }

        root["employeeRoster"] = arr;
        root["employeeRosterMeta"] = new JsonObject
        {
            ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["count"] = arr.Count
        };
    }

    private static EmployeeRosterItem Parse(JsonObject obj)
    {
        var item = new EmployeeRosterItem
        {
            Name = obj["name"]?.GetValue<string>() ?? string.Empty,
            PhoneNumber = obj["phoneNumber"]?.GetValue<string>() ?? string.Empty,
            PersonnelNumber = obj["personnelNumber"]?.GetValue<string>() ?? string.Empty,
            Password = obj["password"]?.GetValue<string>() ?? string.Empty,
            LicenseExpiry = obj["licenseExpiry"]?.GetValue<string>() ?? string.Empty,
            FqnExpiry = obj["fqnExpiry"]?.GetValue<string>() ?? string.Empty,
            DriverCardExpiry = obj["driverCardExpiry"]?.GetValue<string>() ?? string.Empty,
            LoginAsMainDevice = obj["loginAsMainDevice"]?.GetValue<bool>() ?? false
        };

        if (obj["plannerPassword"] is JsonValue plannerPassword &&
            !string.IsNullOrWhiteSpace(plannerPassword.GetValue<string>()))
        {
            item.PlannerPassword = plannerPassword.GetValue<string>()!;
        }

        if (obj.TryGetPropertyValue("plannerLoginEnabled", out var plannerLoginNode) &&
            plannerLoginNode is JsonValue plannerLoginValue)
        {
            item.PlannerLoginEnabled = plannerLoginValue.GetValue<bool>();
        }
        else if (!string.IsNullOrWhiteSpace(item.PlannerPassword))
        {
            item.PlannerLoginEnabled = true;
        }

        if (obj.TryGetPropertyValue("licenseCheckConfirmedAtUtcMs", out var licenseCheckNode) &&
            licenseCheckNode is JsonValue licenseCheckValue)
        {
            item.LicenseCheckConfirmedAtUtcMs = licenseCheckValue.GetValue<long>();
        }

        if (obj.TryGetPropertyValue("fqnCheckConfirmedAtUtcMs", out var fqnCheckNode) &&
            fqnCheckNode is JsonValue fqnCheckValue)
        {
            item.FqnCheckConfirmedAtUtcMs = fqnCheckValue.GetValue<long>();
        }

        if (obj.TryGetPropertyValue("driverCardCheckConfirmedAtUtcMs", out var cardCheckNode) &&
            cardCheckNode is JsonValue cardCheckValue)
        {
            item.DriverCardCheckConfirmedAtUtcMs = cardCheckValue.GetValue<long>();
        }

        return item;
    }

    private static JsonObject Write(EmployeeRosterItem e)
    {
        var obj = new JsonObject
        {
            ["name"] = e.Name,
            ["phoneNumber"] = e.PhoneNumber,
            ["personnelNumber"] = e.PersonnelNumber,
            ["password"] = e.Password,
            ["licenseExpiry"] = e.LicenseExpiry,
            ["fqnExpiry"] = e.FqnExpiry,
            ["driverCardExpiry"] = e.DriverCardExpiry,
            ["loginAsMainDevice"] = e.LoginAsMainDevice
        };

        if (e.PlannerLoginEnabled)
        {
            obj["plannerLoginEnabled"] = true;
        }

        if (!string.IsNullOrWhiteSpace(e.PlannerPassword))
        {
            obj["plannerPassword"] = e.PlannerPassword;
        }

        if (e.LicenseCheckConfirmedAtUtcMs > 0)
        {
            obj["licenseCheckConfirmedAtUtcMs"] = e.LicenseCheckConfirmedAtUtcMs;
        }

        if (e.FqnCheckConfirmedAtUtcMs > 0)
        {
            obj["fqnCheckConfirmedAtUtcMs"] = e.FqnCheckConfirmedAtUtcMs;
        }

        if (e.DriverCardCheckConfirmedAtUtcMs > 0)
        {
            obj["driverCardCheckConfirmedAtUtcMs"] = e.DriverCardCheckConfirmedAtUtcMs;
        }

        return obj;
    }

    /// <summary>Entfernt Planer-Anmeldedaten vor App-Export (routes_export.json).</summary>
    public static void StripPlannerSecretsFromRoot(JsonObject root)
    {
        if (root["employeeRoster"] is not JsonArray arr)
        {
            return;
        }

        foreach (var node in arr.OfType<JsonObject>())
        {
            node.Remove("plannerPassword");
            node.Remove("plannerLoginEnabled");
            node.Remove("licenseCheckConfirmedAtUtcMs");
            node.Remove("fqnCheckConfirmedAtUtcMs");
            node.Remove("driverCardCheckConfirmedAtUtcMs");
        }
    }
}
