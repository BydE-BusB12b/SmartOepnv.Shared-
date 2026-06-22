namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planerpasswort und Hauptnutzer-Flag über Overlay, Workspace und Routen-Paket hinweg erhalten.
/// </summary>
internal static class EmployeePlannerCredentialMerge
{
    public static string GetMergeKey(EmployeeRosterItem employee)
    {
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        if (personnel.Length > 0)
        {
            return $"p:{personnel}";
        }

        var phone = RegisteredVehiclesEditor.NormalizePhoneKey(employee.PhoneNumber);
        if (phone.Length > 0)
        {
            return $"t:{phone}";
        }

        return $"n:{employee.Name.Trim().ToLowerInvariant()}";
    }

    public static void MergeInto(EmployeeRosterItem target, EmployeeRosterItem source)
    {
        if (!string.IsNullOrWhiteSpace(source.PlannerPassword) &&
            string.IsNullOrWhiteSpace(target.PlannerPassword))
        {
            target.PlannerPassword = source.PlannerPassword;
        }

        if (source.LoginAsMainDevice)
        {
            target.LoginAsMainDevice = true;
        }

        if (source.PlannerLoginEnabled)
        {
            target.PlannerLoginEnabled = true;
        }

        if (!string.IsNullOrWhiteSpace(target.PlannerPassword))
        {
            target.PlannerLoginEnabled = true;
        }

        MergeCheckTimestamp(
            target,
            source.LicenseCheckConfirmedAtUtcMs,
            e => e.LicenseCheckConfirmedAtUtcMs,
            (e, v) => e.LicenseCheckConfirmedAtUtcMs = v);
        MergeCheckTimestamp(
            target,
            source.FqnCheckConfirmedAtUtcMs,
            e => e.FqnCheckConfirmedAtUtcMs,
            (e, v) => e.FqnCheckConfirmedAtUtcMs = v);
        MergeCheckTimestamp(
            target,
            source.DriverCardCheckConfirmedAtUtcMs,
            e => e.DriverCardCheckConfirmedAtUtcMs,
            (e, v) => e.DriverCardCheckConfirmedAtUtcMs = v);
        MergeCheckTimestamp(
            target,
            source.LastEditedAtUtcMs,
            e => e.LastEditedAtUtcMs,
            (e, v) => e.LastEditedAtUtcMs = v);

        MergeExpiryIfNewerCheck(target, source,
            e => e.LicenseCheckConfirmedAtUtcMs,
            e => e.LicenseExpiry,
            (e, v) => e.LicenseExpiry = v);
        MergeExpiryIfNewerCheck(target, source,
            e => e.FqnCheckConfirmedAtUtcMs,
            e => e.FqnExpiry,
            (e, v) => e.FqnExpiry = v);
        MergeExpiryIfNewerCheck(target, source,
            e => e.DriverCardCheckConfirmedAtUtcMs,
            e => e.DriverCardExpiry,
            (e, v) => e.DriverCardExpiry = v);
    }

    private static void MergeExpiryIfNewerCheck(
        EmployeeRosterItem target,
        EmployeeRosterItem source,
        Func<EmployeeRosterItem, long> getCheckMs,
        Func<EmployeeRosterItem, string> getExpiry,
        Action<EmployeeRosterItem, string> setExpiry)
    {
        if (getCheckMs(source) <= getCheckMs(target) || string.IsNullOrWhiteSpace(getExpiry(source)))
        {
            return;
        }

        setExpiry(target, getExpiry(source).Trim());
    }

    private static void MergeCheckTimestamp(
        EmployeeRosterItem target,
        long sourceValue,
        Func<EmployeeRosterItem, long> getValue,
        Action<EmployeeRosterItem, long> setValue)
    {
        if (sourceValue <= 0)
        {
            return;
        }

        if (getValue(target) < sourceValue)
        {
            setValue(target, sourceValue);
        }
    }

    public static List<EmployeeRosterItem> MergeLists(
        IEnumerable<EmployeeRosterItem> primary,
        IEnumerable<EmployeeRosterItem> secondary)
    {
        var result = primary.Select(Clone).ToList();
        var byKey = result.ToDictionary(GetMergeKey, e => e, StringComparer.Ordinal);

        foreach (var source in secondary)
        {
            var key = GetMergeKey(source);
            if (byKey.TryGetValue(key, out var target))
            {
                MergeInto(target, source);
            }
            else
            {
                byKey[key] = Clone(source);
            }
        }

        return result;
    }

    private static EmployeeRosterItem Clone(EmployeeRosterItem e) => new()
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
}
