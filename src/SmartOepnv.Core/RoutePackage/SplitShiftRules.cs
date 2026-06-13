namespace SmartOepnv.Core.RoutePackage;

/// <summary>TV-N / FPersV – Regeln für geteilte Dienste im Nahverkehr.</summary>
public static class SplitShiftRules
{
    /// <summary>TV-N Berlin: Unterbrechung &gt; 60 min gilt als Dienstteilung.</summary>
    public const int MinBreakMinutes = 60;

    public const int MinPartHours = 2;

    /// <summary>TV-N Berlin: zweiter Dienstteil darf nicht nach 22:00 beginnen.</summary>
    public const int Part2LatestStartHour = 22;

    /// <summary>TV-N: Dienstschicht bei Teilung bis 14 h.</summary>
    public const int MaxServiceShiftHours = 14;

    /// <summary>TV-N: dienstplanmäßige tägliche Arbeitszeit (Lenkzeit) max. 8,5 h.</summary>
    public const double MaxDailyWorkingHours = 8.5;

    public static readonly string MinBreakMessage =
        $"Unterbrechung zwischen den Dienstteilen muss mindestens {MinBreakMinutes} Minuten betragen (TV-N Dienstteilung).";

    public static readonly string MinPartDurationMessage =
        $"Jeder Dienstteil muss mindestens {MinPartHours} Stunden betragen (TV-N).";

    public static readonly string Part2StartTooLateMessage =
        $"Der zweite Dienstteil darf nicht nach {Part2LatestStartHour}:00 Uhr beginnen (TV-N Berlin).";

    public static readonly string MaxServiceShiftMessage =
        $"Die Dienstschicht darf bei geteiltem Dienst höchstens {MaxServiceShiftHours} Stunden umfassen (TV-N).";

    public const string PartOrderMessage = "Teil 2 muss nach Teil 1 liegen.";

    public const string SameDayMessage = "Geteilte Dienste müssen am selben Kalendertag enden.";

    public static readonly string DailyWorkingTimeMessage =
        $"Die Lenkzeit (Dienstzeit) am Tag überschreitet {MaxDailyWorkingHours} Stunden (TV-N).";
}
