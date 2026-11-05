using SmartOepnv.Core.Betrieb;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Session;

public static class PlanerCredentialValidator
{
    // Notfall-Zugang „Admin“: Passwort je Betrieb, sonst der feste Standard.
    private const string BackdoorUsername = BuiltinAdminEmployee.Name;

    public static string ActiveAdminPassword()
    {
        var stored = BetriebProfileStore.GetAdminPlannerPassword(
            BetriebProfileStore.GetActiveProfile()?.Id);
        return string.IsNullOrEmpty(stored) ? BuiltinAdminEmployee.PlannerPassword : stored;
    }

    public static bool TryValidate(string username, string password, out string authenticatedName)
    {
        authenticatedName = string.Empty;
        var trimmedUser = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedUser) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (string.Equals(trimmedUser, BackdoorUsername, StringComparison.Ordinal))
        {
            if (!string.Equals(password, ActiveAdminPassword(), StringComparison.Ordinal))
            {
                return false;
            }

            authenticatedName = BackdoorUsername;
            return true;
        }

        foreach (var employee in EnumeratePlannerUsers())
        {
            if (!employee.CanLoginToPlanner || BuiltinAdminEmployee.IsBuiltinAdmin(employee))
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
