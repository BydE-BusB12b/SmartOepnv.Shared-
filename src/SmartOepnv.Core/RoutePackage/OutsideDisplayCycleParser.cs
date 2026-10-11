using System.Text;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Wechseltext-Logstrings wie GPSAnsagen <c>frontLogString</c> / <c>sideLogString</c> (Base64 in outsideDisplays).
/// </summary>
public static class OutsideDisplayCycleParser
{
    public const int MaxCycles = 4;

    public static string BuildLogString(IReadOnlyList<(string Line1, string Line2)> goals)
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

        return string.Join('\n', blocks);
    }

    public static IReadOnlyList<(string Line1, string Line2)> ParseGoals(string? log, byte[]? telegramBytes)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return [];
        }

        var goalCount = TryGetGoalCountFromTelegram(telegramBytes);
        var lines = log
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return [];
        }

        if (goalCount is null or <= 1)
        {
            return [(lines[0], lines.Count > 1 ? lines[1] : string.Empty)];
        }

        return ParseLinesIntoGoals(lines, goalCount.Value);
    }

    public static void ApplyToCycles(
        IList<OutsideDisplayTextCycle> cycles,
        string? log,
        byte[]? telegramBytes)
    {
        while (cycles.Count < MaxCycles)
        {
            cycles.Add(new OutsideDisplayTextCycle());
        }

        for (var i = 0; i < MaxCycles; i++)
        {
            cycles[i].Clear();
        }

        var goals = ParseGoals(log, telegramBytes);
        for (var i = 0; i < goals.Count && i < MaxCycles; i++)
        {
            cycles[i].SetFromPair(goals[i].Line1, goals[i].Line2);
        }
    }

    public static IReadOnlyList<(string Line1, string Line2)> CollectFrontGoals(IEnumerable<OutsideDisplayTextCycle> cycles) =>
        cycles
            .Where(c => c.HasContent)
            .Select(c => c.ToGoalPair())
            .ToList();

    /// <summary>
    /// Seiten-Ziele indexgleich zur Front-Zielliste. Leerer Seiten-Slot übernimmt das Front-Ziel
    /// derselben Position (nicht: alle Seiten-Ziele nach vorne schieben).
    /// Ohne jeglichen Seiteninhalt: komplette Front-Liste.
    /// </summary>
    public static IReadOnlyList<(string Line1, string Line2)> CollectSideGoals(
        IEnumerable<OutsideDisplayTextCycle> sideCycles,
        IReadOnlyList<(string Line1, string Line2)> frontGoals)
    {
        var sideList = sideCycles as IList<OutsideDisplayTextCycle> ?? sideCycles.ToList();
        var anySide = false;
        for (var i = 0; i < sideList.Count; i++)
        {
            if (sideList[i].HasContent)
            {
                anySide = true;
                break;
            }
        }

        if (!anySide)
        {
            return frontGoals;
        }

        var result = new List<(string Line1, string Line2)>(Math.Max(frontGoals.Count, 1));
        for (var i = 0; i < frontGoals.Count; i++)
        {
            var side = i < sideList.Count ? sideList[i] : null;
            result.Add(side is not null && side.HasContent ? side.ToGoalPair() : frontGoals[i]);
        }

        for (var i = frontGoals.Count; i < sideList.Count && i < MaxCycles; i++)
        {
            if (sideList[i].HasContent)
            {
                result.Add(sideList[i].ToGoalPair());
            }
        }

        return result;
    }

    private static int? TryGetGoalCountFromTelegram(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 6)
        {
            return null;
        }

        var length = bytes[^1] is >= 32 and <= 126 ? bytes.Length - 1 : bytes.Length;
        if (length < 5)
        {
            return null;
        }

        var ascii = Encoding.ASCII.GetString(bytes, 0, length);
        if (Ds021NeuProgramBuilder.IsDs021NeuPayloadAscii(ascii))
        {
            return null;
        }

        if (FmaS1ProgramBuilder.IsFmaS1PayloadAscii(ascii))
        {
            return null;
        }

        var header = ascii.Length >= 5 ? ascii[..5] : ascii;
        // Front: aA1…, Seite DS021T A2: aA2…, Seite A4 teils aA3…
        if (header.Length < 5 ||
            header[0] != 'a' ||
            header[1] != 'A' ||
            header[2] is not ('1' or '2' or '3'))
        {
            return null;
        }

        return header[3] switch
        {
            '2' or '3' => 1,
            '5' => 2,
            '4' => CountGoalsInMultiBody(bytes, length),
            _ => null
        };
    }

    private static int CountGoalsInMultiBody(byte[] bytes, int length)
    {
        var text = Encoding.ASCII.GetString(bytes, 5, Math.Max(0, length - 5));
        var blocks = 0;
        var idx = 0;
        while (idx < text.Length)
        {
            while (idx < text.Length && text[idx] == '\n')
            {
                idx++;
            }

            if (idx >= text.Length)
            {
                break;
            }

            blocks++;
            while (idx < text.Length && text[idx] != '\r')
            {
                idx++;
            }
        }

        return blocks is >= 1 and <= MaxCycles ? blocks : MaxCycles;
    }

    private static List<(string Line1, string Line2)> ParseLinesIntoGoals(IReadOnlyList<string> lines, int goalCount)
    {
        var goals = new List<(string, string)>(goalCount);
        var index = 0;
        while (goals.Count < goalCount && index < lines.Count)
        {
            var remainingGoals = goalCount - goals.Count;
            var remainingLines = lines.Count - index;

            if (remainingLines == remainingGoals)
            {
                goals.Add((lines[index], string.Empty));
                index++;
                continue;
            }

            if (remainingLines >= remainingGoals * 2)
            {
                goals.Add((lines[index], lines[index + 1]));
                index += 2;
                continue;
            }

            goals.Add((lines[index], remainingLines > remainingGoals ? lines[index + 1] : string.Empty));
            index += remainingLines > remainingGoals ? 2 : 1;
        }

        return goals;
    }
}
