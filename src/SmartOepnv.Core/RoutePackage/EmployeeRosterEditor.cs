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

    private static EmployeeRosterItem Parse(JsonObject obj) => new()
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

    private static JsonObject Write(EmployeeRosterItem e) => new()
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
}
