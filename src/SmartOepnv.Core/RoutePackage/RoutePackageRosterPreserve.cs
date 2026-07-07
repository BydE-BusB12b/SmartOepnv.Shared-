using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Schützt Personal- und Fahrzeugregister vor leeren Importen (z. B. fehlerhafte routes_export.json).
/// </summary>
public static class RoutePackageRosterPreserve
{
    public sealed record Snapshot(
        IReadOnlyList<EmployeeRosterItem> Employees,
        IReadOnlyList<RegisteredVehicleItem> Vehicles,
        IReadOnlyList<RegisteredVehiclePhoneRedirect> PhoneRedirects);

    public static bool JsonContainsRosterData(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root is null)
        {
            return false;
        }

        return CountArray(root, "employeeRoster") > 0 ||
               CountArray(root, "registeredVehicles") > 0;
    }

    public static bool JsonObjectContainsRosterData(JsonObject root) =>
        CountArray(root, "employeeRoster") > 0 ||
        CountArray(root, "registeredVehicles") > 0;

    public static Snapshot? CaptureFromEditor(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return null;
        }

        if (editor.Employees.Count == 0 && editor.RegisteredVehicles.Count == 0)
        {
            return null;
        }

        return new Snapshot(
            editor.Employees.Select(CloneEmployee).ToList(),
            editor.RegisteredVehicles.Select(CloneVehicle).ToList(),
            editor.RegisteredVehiclePhoneRedirects.Select(CloneRedirect).ToList());
    }

    public static void RestoreIfIncomingEmpty(EditableRoutePackage editor, Snapshot? previous)
    {
        if (previous is null)
        {
            return;
        }

        if (editor.Employees.Count == 0 && previous.Employees.Count > 0)
        {
            editor.ReplaceEmployees(previous.Employees.Select(CloneEmployee).ToList());
        }

        if (editor.RegisteredVehicles.Count == 0 && previous.Vehicles.Count > 0)
        {
            editor.ReplaceRegisteredVehicles(previous.Vehicles.Select(CloneVehicle).ToList());
            if (previous.PhoneRedirects.Count > 0)
            {
                editor.ReplaceRegisteredVehiclePhoneRedirects(
                    previous.PhoneRedirects.Select(CloneRedirect).ToList());
            }
        }
    }

    private static int CountArray(JsonObject root, string key) =>
        root[key] is JsonArray arr ? arr.Count : 0;

    private static EmployeeRosterItem CloneEmployee(EmployeeRosterItem e) => new()
    {
        Name = e.Name,
        PhoneNumber = e.PhoneNumber,
        PersonnelNumber = e.PersonnelNumber,
        Password = e.Password,
        LicenseExpiry = e.LicenseExpiry,
        FqnExpiry = e.FqnExpiry,
        DriverCardExpiry = e.DriverCardExpiry,
        LoginAsMainDevice = e.LoginAsMainDevice,
        PlannerLoginEnabled = e.PlannerLoginEnabled,
        PlannerPassword = e.PlannerPassword,
        LicenseCheckConfirmedAtUtcMs = e.LicenseCheckConfirmedAtUtcMs,
        FqnCheckConfirmedAtUtcMs = e.FqnCheckConfirmedAtUtcMs,
        DriverCardCheckConfirmedAtUtcMs = e.DriverCardCheckConfirmedAtUtcMs,
        LastEditedAtUtcMs = e.LastEditedAtUtcMs
    };

    private static RegisteredVehicleItem CloneVehicle(RegisteredVehicleItem v) => new()
    {
        Name = v.Name,
        PhoneNumber = v.PhoneNumber,
        PersonnelNumber = v.PersonnelNumber,
        Password = v.Password,
        LicenseExpiry = v.LicenseExpiry,
        FqnExpiry = v.FqnExpiry,
        DriverCardExpiry = v.DriverCardExpiry,
        LoginAsMainDevice = v.LoginAsMainDevice,
        LoadedPhoneNumber = string.IsNullOrWhiteSpace(v.LoadedPhoneNumber) ? v.PhoneNumber : v.LoadedPhoneNumber,
        PlannerDetails = v.PlannerDetails.Clone()
    };

    private static RegisteredVehiclePhoneRedirect CloneRedirect(RegisteredVehiclePhoneRedirect r) => new()
    {
        FromPhoneNumber = r.FromPhoneNumber,
        ToPhoneNumber = r.ToPhoneNumber,
        RecordedAt = r.RecordedAt,
        Note = r.Note
    };
}
