namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Teilt Dienste intelligent auf (max. 9 h pro Teil, Fahrten nicht trennen).</summary>
public static class DutyTemplateSplitter
{
    public const int MaxDutyMinutes = 9 * 60;

    public sealed class SplitResult
    {
        public bool RequiresSplit { get; init; }

        public bool FoundValidSplit { get; init; }

        public int PartCount { get; init; } = 2;

        /// <summary>Teil 1 = Zeilen [0 .. SplitAfterIndex-1], Teil 2 ab Index SplitAfterIndex.</summary>
        public int SplitAfterIndex { get; init; }

        /// <summary>Bei 3 Teilen: Teil 3 ab diesem Index. Teil 2 = [SplitAfterIndex .. SecondSplitAfterIndex-1].</summary>
        public int SecondSplitAfterIndex { get; init; }

        public string? WarningMessage { get; init; }
    }

    public static SplitResult Analyze(
        IReadOnlyList<DutyTemplateRow> rows,
        int preparationMinutes,
        int followUpMinutes)
    {
        var ordered = DutyTemplateCalculator.OrderRows(rows);
        if (ordered.Count == 0)
        {
            return new SplitResult();
        }

        var prep = DutyTemplateCalculator.ResolvePreparationMinutes(preparationMinutes);
        var followUp = DutyTemplateCalculator.ResolveFollowUpMinutes(followUpMinutes);
        var fullDuration = DutyTemplateCalculator.ComputePartDuration(ordered, prep, followUp);

        if (fullDuration <= MaxDutyMinutes)
        {
            if (ordered.Count == 1 && fullDuration > MaxDutyMinutes)
            {
                return new SplitResult
                {
                    RequiresSplit = true,
                    WarningMessage =
                        $"Einzelne Fahrt dauert {DutyTemplateCalculator.FormatMinutes(fullDuration)} – " +
                        "kann nicht unter 9 Stunden geteilt werden."
                };
            }

            return new SplitResult();
        }

        if (ordered.Count < 2)
        {
            return new SplitResult
            {
                RequiresSplit = true,
                WarningMessage =
                    $"Dienst dauert {DutyTemplateCalculator.FormatMinutes(fullDuration)}, " +
                    "enthält aber nur eine Fahrt – Aufteilung nicht möglich."
            };
        }

        if (TryFindTwoPartSplit(ordered, prep, followUp, out var twoPartIndex))
        {
            return new SplitResult
            {
                RequiresSplit = true,
                FoundValidSplit = true,
                PartCount = 2,
                SplitAfterIndex = twoPartIndex
            };
        }

        if (ordered.Count >= 3
            && TryFindThreePartSplit(ordered, prep, followUp, out var firstSplit, out var secondSplit))
        {
            return new SplitResult
            {
                RequiresSplit = true,
                FoundValidSplit = true,
                PartCount = 3,
                SplitAfterIndex = firstSplit,
                SecondSplitAfterIndex = secondSplit
            };
        }

        return new SplitResult
        {
            RequiresSplit = true,
            WarningMessage =
                $"Dienst dauert {DutyTemplateCalculator.FormatMinutes(fullDuration)} – " +
                "kein gültiger Schnitt unter 9 Stunden pro Teil gefunden (weder in 2 noch in 3 Teile)."
        };
    }

    private static bool TryFindTwoPartSplit(
        IReadOnlyList<DutyTemplateRow> ordered,
        int preparationMinutes,
        int followUpMinutes,
        out int splitIndex)
    {
        splitIndex = 0;
        int? bestIndex = null;
        var bestBalance = int.MaxValue;

        for (var candidate = 1; candidate < ordered.Count; candidate++)
        {
            var part1 = ordered.Take(candidate).ToList();
            var part2 = ordered.Skip(candidate).ToList();
            var duration1 = DutyTemplateCalculator.ComputePartDuration(part1, preparationMinutes, followUpMinutes);
            var duration2 = DutyTemplateCalculator.ComputePartDuration(part2, preparationMinutes, followUpMinutes);

            if (duration1 <= MaxDutyMinutes && duration2 <= MaxDutyMinutes)
            {
                var balance = Math.Abs(duration1 - duration2);
                if (balance < bestBalance)
                {
                    bestBalance = balance;
                    bestIndex = candidate;
                }
            }
        }

        if (bestIndex is null)
        {
            return false;
        }

        splitIndex = bestIndex.Value;
        return true;
    }

    private static bool TryFindThreePartSplit(
        IReadOnlyList<DutyTemplateRow> ordered,
        int preparationMinutes,
        int followUpMinutes,
        out int firstSplitIndex,
        out int secondSplitIndex)
    {
        firstSplitIndex = 0;
        secondSplitIndex = 0;
        int? bestFirst = null;
        int? bestSecond = null;
        var bestBalance = int.MaxValue;

        for (var first = 1; first < ordered.Count - 1; first++)
        {
            for (var second = first + 1; second < ordered.Count; second++)
            {
                var part1 = ordered.Take(first).ToList();
                var part2 = ordered.Skip(first).Take(second - first).ToList();
                var part3 = ordered.Skip(second).ToList();
                if (part1.Count == 0 || part2.Count == 0 || part3.Count == 0)
                {
                    continue;
                }

                var duration1 = DutyTemplateCalculator.ComputePartDuration(part1, preparationMinutes, followUpMinutes);
                var duration2 = DutyTemplateCalculator.ComputePartDuration(part2, preparationMinutes, followUpMinutes);
                var duration3 = DutyTemplateCalculator.ComputePartDuration(part3, preparationMinutes, followUpMinutes);
                if (duration1 > MaxDutyMinutes || duration2 > MaxDutyMinutes || duration3 > MaxDutyMinutes)
                {
                    continue;
                }

                var min = Math.Min(duration1, Math.Min(duration2, duration3));
                var max = Math.Max(duration1, Math.Max(duration2, duration3));
                var balance = max - min;
                if (balance < bestBalance)
                {
                    bestBalance = balance;
                    bestFirst = first;
                    bestSecond = second;
                }
            }
        }

        if (bestFirst is null || bestSecond is null)
        {
            return false;
        }

        firstSplitIndex = bestFirst.Value;
        secondSplitIndex = bestSecond.Value;
        return true;
    }
}
