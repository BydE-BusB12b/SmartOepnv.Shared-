using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Planer: Führerschein-, FQN- und Fahrerkartenkontrolle – Bestätigung gilt 3 Monate.</summary>
public static class EmployeeDocumentCheck
{
    public const int ValidityMonths = 3;

    public static bool IsValid(long confirmedAtUtcMs)
    {
        if (confirmedAtUtcMs <= 0)
        {
            return false;
        }

        return GetValidUntilUtc(confirmedAtUtcMs) > DateTime.UtcNow;
    }

    public static bool IsDue(long confirmedAtUtcMs) => !IsValid(confirmedAtUtcMs);

    public static long ConfirmNowUtcMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static DateTime GetValidUntilUtc(long confirmedAtUtcMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(confirmedAtUtcMs).UtcDateTime.AddMonths(ValidityMonths);

    public static string FormatStatus(long confirmedAtUtcMs)
    {
        if (!IsValid(confirmedAtUtcMs))
        {
            return "Kontrolle fällig – bitte bestätigen.";
        }

        var until = GetValidUntilUtc(confirmedAtUtcMs).ToLocalTime();
        return $"Bestätigt – gültig bis {until:dd.MM.yyyy}.";
    }
}
