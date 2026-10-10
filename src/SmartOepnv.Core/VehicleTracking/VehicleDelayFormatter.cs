namespace SmartOepnv.Core.VehicleTracking;

/// <summary>
/// Formatiert Verspätung aus dem Fahrzeug-Payload für die Leitstelle.
/// Fahrzeug-Konvention (wie ITCS/Tablet): + = zu früh, − = zu spät (Soll − Ist).
/// </summary>
public static class VehicleDelayFormatter
{
    public static string FormatForOperations(int? delaySeconds)
    {
        if (delaySeconds is not int seconds)
        {
            return "–";
        }

        if (seconds == 0)
        {
            return "pünktlich";
        }

        var abs = Math.Abs(seconds);
        var minutes = abs / 60;
        var remainderSeconds = abs % 60;

        if (seconds > 0)
        {
            return minutes > 0
                ? $"{minutes} min früh"
                : $"{remainderSeconds} s früh";
        }

        return minutes > 0
            ? $"+{minutes} min"
            : $"+{remainderSeconds} s";
    }
}
