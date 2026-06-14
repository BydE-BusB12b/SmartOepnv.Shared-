namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Teilt Dienste intelligent auf (max. 9 h pro Teil, Fahrten nicht trennen).</summary>
public static class DutyTemplateSplitter
{
    public const int MaxDutyMinutes = 9 * 60;

    public sealed class SplitResult
    {
        public bool RequiresSplit { get; init; }

        public bool FoundValidSplit { get; init; }

        /// <summary>Teil 1 = Zeilen [0 .. SplitAfterIndex-1], Teil 2 ab Index SplitAfterIndex.</summary>
        public int SplitAfterIndex { get; init; }

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

        int? bestIndex = null;
        var bestBalance = int.MaxValue;

        for (var splitIndex = 1; splitIndex < ordered.Count; splitIndex++)
        {
            var part1 = ordered.Take(splitIndex).ToList();
            var part2 = ordered.Skip(splitIndex).ToList();
            var dur1 = DutyTemplateCalculator.ComputePartDuration(part1, prep, followUp);
            var dur2 = DutyTemplateCalculator.ComputePartDuration(part2, prep, followUp);

            if (dur1 <= MaxDutyMinutes && dur2 <= MaxDutyMinutes)
            {
                var balance = Math.Abs(dur1 - dur2);
                if (balance < bestBalance)
                {
                    bestBalance = balance;
                    bestIndex = splitIndex;
                }
            }
        }

        if (bestIndex is null)
        {
            return new SplitResult
            {
                RequiresSplit = true,
                WarningMessage =
                    $"Dienst dauert {DutyTemplateCalculator.FormatMinutes(fullDuration)} – " +
                    "kein gültiger Schnittpunkt unter 9 Stunden pro Teil gefunden."
            };
        }

        return new SplitResult
        {
            RequiresSplit = true,
            FoundValidSplit = true,
            SplitAfterIndex = bestIndex.Value
        };
    }
}
