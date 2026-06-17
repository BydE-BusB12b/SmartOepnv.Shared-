namespace SmartOepnv.Core.RoutePackage;



public static class SplitShiftCompliance

{

    /// <summary>

    /// Strukturprüfung geteilter Dienste (Pause, Teildauer, max. 13-h-Dienstschicht).

    /// Lenkzeit 9/10 h und Ruhezeiten werden separat über <see cref="DriverDispositionCompliance"/> geprüft.

    /// </summary>

    public static bool TryValidateStructure(

        long part1StartEpochMs,

        long part1EndEpochMs,

        long part2StartEpochMs,

        long part2EndEpochMs,

        out string errorMessage)

    {

        errorMessage = string.Empty;



        if (part1EndEpochMs <= part1StartEpochMs ||

            part2EndEpochMs <= part2StartEpochMs ||

            part2StartEpochMs <= part1EndEpochMs)

        {

            errorMessage = SplitShiftRules.PartOrderMessage;

            return false;

        }



        var part1Start = DateTimeOffset.FromUnixTimeMilliseconds(part1StartEpochMs).LocalDateTime;

        var part1End = DateTimeOffset.FromUnixTimeMilliseconds(part1EndEpochMs).LocalDateTime;

        var part2Start = DateTimeOffset.FromUnixTimeMilliseconds(part2StartEpochMs).LocalDateTime;

        var part2End = DateTimeOffset.FromUnixTimeMilliseconds(part2EndEpochMs).LocalDateTime;



        if (part1Start.Date != part2End.Date)

        {

            errorMessage = SplitShiftRules.SameDayMessage;

            return false;

        }



        var breakMs = part2StartEpochMs - part1EndEpochMs;

        if (breakMs < SplitShiftRules.MinBreakMinutes * 60_000L)

        {

            errorMessage = SplitShiftRules.MinBreakMessage;

            return false;

        }



        var minPartMs = SplitShiftRules.MinPartHours * 60L * 60L * 1000L;

        if (part1EndEpochMs - part1StartEpochMs < minPartMs ||

            part2EndEpochMs - part2StartEpochMs < minPartMs)

        {

            errorMessage = SplitShiftRules.MinPartDurationMessage;

            return false;

        }



        if (part2Start.TimeOfDay > TimeSpan.FromHours(SplitShiftRules.Part2LatestStartHour))

        {

            errorMessage = SplitShiftRules.Part2StartTooLateMessage;

            return false;

        }



        var envelopeMs = part2EndEpochMs - part1StartEpochMs;

        if (envelopeMs > SplitShiftRules.MaxServiceShiftHours * 60L * 60L * 1000L)

        {

            errorMessage = SplitShiftRules.MaxServiceShiftMessage;

            return false;

        }



        return true;

    }



    public static long SumWorkMsOnDay(DriverDispositionAssignment assignment, DateTime day)

    {

        var dayStartMs = new DateTimeOffset(day).ToUnixTimeMilliseconds();

        var dayEndMs = new DateTimeOffset(day.AddDays(1)).ToUnixTimeMilliseconds();

        long total = 0;



        foreach (var (segmentStart, segmentEnd) in assignment.EnumerateWorkSegments())

        {

            var overlapStart = Math.Max(segmentStart, dayStartMs);

            var overlapEnd = Math.Min(segmentEnd, dayEndMs);

            if (overlapEnd > overlapStart)

            {

                total += overlapEnd - overlapStart;

            }

        }



        return total;

    }

}

