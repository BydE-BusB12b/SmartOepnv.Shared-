namespace SmartOepnv.Core.RoutePackage;



/// <summary>

/// FPersV / TV-N – Regeln für geteilte Dienste in der Fahrerdisposition

/// (ein Dienst mit Arbeitsteil 1 + dienstfreier Pause + Arbeitsteil 2, z. B. Dienstnummer 301).

/// Nicht zu verwechseln mit der Planer-Vorlagen-Aufteilung in Teil 1/2/3.

/// </summary>

public static class SplitShiftRules

{

    /// <summary>FPersV: Unterbrechung zwischen den Arbeitsteilen mind. 2 Stunden.</summary>

    public const int MinBreakMinutes = 120;



    public const int MinPartHours = 2;



    /// <summary>TV-N Berlin: zweiter Dienstteil darf nicht nach 22:00 beginnen.</summary>

    public const int Part2LatestStartHour = 22;



    /// <summary>

    /// TV-N: maximale Dienstschicht bei Teilung (Spanne erster Beginn bis letztes Ende).

    /// </summary>

    public const int MaxServiceShiftHours = 13;



    public static readonly string MinBreakMessage =

        $"Die dienstfreie Pause zwischen den Arbeitsteilen muss mindestens {MinBreakMinutes / 60} Stunden betragen (FPersV).";



    public static readonly string MinPartDurationMessage =

        $"Jeder Arbeitsteil muss mindestens {MinPartHours} Stunden betragen (TV-N).";



    public static readonly string Part2StartTooLateMessage =

        $"Der zweite Arbeitsteil darf nicht nach {Part2LatestStartHour}:00 Uhr beginnen (TV-N Berlin).";



    public static readonly string MaxServiceShiftMessage =

        $"Die Dienstschicht des geteilten Dienstes darf höchstens {MaxServiceShiftHours} Stunden umfassen " +

        $"(von Beginn Arbeitsteil 1 bis Ende Arbeitsteil 2, TV-N). Lenkzeit und Ruhezeiten prüft die FPersV separat.";



    public const string PartOrderMessage = "Arbeitsteil 2 muss nach Arbeitsteil 1 liegen.";



    public const string SameDayMessage = "Geteilte Dienste müssen am selben Kalendertag enden.";

}

