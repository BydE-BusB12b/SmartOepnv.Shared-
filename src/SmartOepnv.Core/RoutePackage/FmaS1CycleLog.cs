namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Wechseltext-Logstrings für FMA-S1 (Zyklus-Trenner <c>;;;</c>, wie GPSAnsagen <c>FmaS1Message</c>).
/// </summary>
public static class FmaS1CycleLog
{
    public const string CycleDelimiter = ";;;";
    private const string GoalLogDelimiter = "|||";

    public static string Encode(IReadOnlyList<(string Line1, string Line2)> goals)
    {
        var blocks = new List<string>();
        foreach (var (line1, line2) in goals)
        {
            var l1 = line1.Trim();
            var l2 = line2.Trim();
            if (string.IsNullOrEmpty(l1) && string.IsNullOrEmpty(l2))
            {
                continue;
            }

            blocks.Add(string.IsNullOrEmpty(l2) ? l1 : $"{l1}\n{l2}");
        }

        return string.Join(CycleDelimiter, blocks);
    }

    public static IReadOnlyList<(string Line1, string Line2)> Parse(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return [(string.Empty, string.Empty)];
        }

        var segment = log.Split(GoalLogDelimiter, 2)[0];
        var blocks = segment.Split(CycleDelimiter, StringSplitOptions.None);
        var cycles = new List<(string, string)>();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            var lines = block.Split('\n');
            var l1 = lines.Length > 0 ? lines[0] : string.Empty;
            var l2 = lines.Length > 1 ? lines[1] : string.Empty;
            cycles.Add((l1, l2));
        }

        return cycles.Count > 0 ? cycles : [(string.Empty, string.Empty)];
    }

    public static void ApplyToCycles(IList<OutsideDisplayTextCycle> cycles, string? log)
    {
        while (cycles.Count < OutsideDisplayCycleParser.MaxCycles)
        {
            cycles.Add(new OutsideDisplayTextCycle());
        }

        for (var i = 0; i < OutsideDisplayCycleParser.MaxCycles; i++)
        {
            cycles[i].Clear();
        }

        var goals = Parse(log);
        for (var i = 0; i < goals.Count && i < OutsideDisplayCycleParser.MaxCycles; i++)
        {
            cycles[i].SetFromPair(goals[i].Line1, goals[i].Line2);
        }
    }
}
