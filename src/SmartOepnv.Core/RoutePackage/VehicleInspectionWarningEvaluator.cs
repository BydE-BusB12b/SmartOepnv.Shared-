using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>HU-/SP-Fristen für Planer-Dashboard (30–16 / 15–5 / 4–1 / überfällig).</summary>
public static class VehicleInspectionWarningEvaluator
{
    private const string DateFormat = "yyyy-MM-dd";

    public static IList<VehicleInspectionWarning> Evaluate(
        IEnumerable<RegisteredVehicleItem> vehicles,
        DateOnly? today = null)
    {
        var reference = today ?? DateOnly.FromDateTime(DateTime.Today);
        var warnings = new List<VehicleInspectionWarning>();

        foreach (var vehicle in vehicles)
        {
            TryAddWarning(warnings, vehicle, "HU", vehicle.PlannerDetails.NextMainInspection, reference);
            TryAddWarning(warnings, vehicle, "SP", vehicle.PlannerDetails.NextSpInspection, reference);
        }

        return warnings
            .OrderByDescending(w => w.SortKey)
            .ThenBy(w => w.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryAddWarning(
        ICollection<VehicleInspectionWarning> warnings,
        RegisteredVehicleItem vehicle,
        string inspectionKind,
        string? dateRaw,
        DateOnly today)
    {
        if (!TryParseInspectionDate(dateRaw, out var dueDate))
        {
            return;
        }

        var label = BuildVehicleLabel(vehicle);
        var phoneNorm = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.PhoneNumber);

        var daysUntil = dueDate.DayNumber - today.DayNumber;
        if (daysUntil > 30)
        {
            return;
        }

        VehicleInspectionWarning? warning = daysUntil switch
        {
            >= 16 and <= 30 => Create(
                VehicleInspectionWarningLevel.Notice30To16Days,
                $"!Hinweis: {inspectionKind} für Fahrzeug {label} in {daysUntil} Tagen.",
                100 + daysUntil,
                phoneNorm),
            >= 5 and <= 15 => Create(
                VehicleInspectionWarningLevel.Notice15To5Days,
                $"!Hinweis: {inspectionKind} für Fahrzeug {label} in {daysUntil} Tagen.",
                200 + daysUntil,
                phoneNorm),
            >= 1 and <= 4 => Create(
                VehicleInspectionWarningLevel.Urgent4To1Days,
                $"!Achtung: {inspectionKind} für Fahrzeug {label} fällig in {daysUntil} Tagen.",
                400 - daysUntil,
                phoneNorm),
            <= 0 => Create(
                VehicleInspectionWarningLevel.Overdue,
                $"!ACHTUNG: {inspectionKind} für Fahrzeug {label} jetzt fällig (seit {Math.Abs(daysUntil)} Tagen abgelaufen)",
                1000 + Math.Abs(daysUntil),
                phoneNorm),
            _ => null
        };

        if (warning is not null)
        {
            warnings.Add(warning);
        }
    }

    private static VehicleInspectionWarning Create(
        VehicleInspectionWarningLevel level,
        string message,
        int sortKey,
        string phoneNormalized) =>
        new()
        {
            Level = level,
            Message = message,
            SortKey = sortKey,
            PhoneNormalized = phoneNormalized
        };

    private static string BuildVehicleLabel(RegisteredVehicleItem vehicle)
    {
        if (!string.IsNullOrWhiteSpace(vehicle.Name))
        {
            return vehicle.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(vehicle.PhoneNumber))
        {
            return vehicle.PhoneNumber.Trim();
        }

        return "unbenannt";
    }

    internal static bool TryParseInspectionDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (DateOnly.TryParseExact(trimmed, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt.Date);
            return true;
        }

        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
