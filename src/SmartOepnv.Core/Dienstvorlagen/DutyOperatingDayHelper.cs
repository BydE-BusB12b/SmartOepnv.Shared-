namespace SmartOepnv.Core.Dienstvorlagen;

public enum DutyOperatingDay
{
    Monday = 0,
    Tuesday = 1,
    Wednesday = 2,
    Thursday = 3,
    Friday = 4,
    Saturday = 5,
    SundayHoliday = 6
}

/// <summary>Einzelne Betriebstage auswählen und als Text gruppieren (z. B. Montag-Mittwoch).</summary>
public static class DutyOperatingDayHelper
{
    private static readonly (DutyOperatingDay Day, string Name)[] Definitions =
    [
        (DutyOperatingDay.Monday, "Montag"),
        (DutyOperatingDay.Tuesday, "Dienstag"),
        (DutyOperatingDay.Wednesday, "Mittwoch"),
        (DutyOperatingDay.Thursday, "Donnerstag"),
        (DutyOperatingDay.Friday, "Freitag"),
        (DutyOperatingDay.Saturday, "Samstag"),
        (DutyOperatingDay.SundayHoliday, "Sonn- und Feiertag")
    ];

    public static IReadOnlyList<(DutyOperatingDay Day, string Name)> AllDays => Definitions;

    public static string GetName(DutyOperatingDay day) =>
        Definitions.First(d => d.Day == day).Name;

    public static string FormatDisplay(IEnumerable<DutyOperatingDay> selectedDays)
    {
        var sorted = selectedDays.Distinct().OrderBy(day => (int)day).ToList();
        if (sorted.Count == 0)
        {
            return string.Empty;
        }

        var groups = new List<List<DutyOperatingDay>>();
        var current = new List<DutyOperatingDay> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            if ((int)sorted[i] == (int)sorted[i - 1] + 1)
            {
                current.Add(sorted[i]);
            }
            else
            {
                groups.Add(current);
                current = [sorted[i]];
            }
        }

        groups.Add(current);

        return string.Join(", ", groups.Select(FormatGroup));
    }

    public static HashSet<DutyOperatingDay> Parse(string? text)
    {
        var result = new HashSet<DutyOperatingDay>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var segment in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseSegment(segment, out var days))
            {
                foreach (var day in days)
                {
                    result.Add(day);
                }
            }
        }

        return result;
    }

    private static string FormatGroup(IReadOnlyList<DutyOperatingDay> group) =>
        group.Count == 1
            ? GetName(group[0])
            : $"{GetName(group[0])}-{GetName(group[^1])}";

    private static bool TryParseSegment(string segment, out IEnumerable<DutyOperatingDay> days)
    {
        foreach (var (day, name) in Definitions)
        {
            if (string.Equals(segment, name, StringComparison.OrdinalIgnoreCase))
            {
                days = [day];
                return true;
            }
        }

        for (var start = 0; start < Definitions.Length; start++)
        {
            for (var end = start; end < Definitions.Length; end++)
            {
                var rangeText = $"{Definitions[start].Name}-{Definitions[end].Name}";
                if (!string.Equals(segment, rangeText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                days = Enumerable.Range(start, end - start + 1)
                    .Select(index => Definitions[index].Day);
                return true;
            }
        }

        days = [];
        return false;
    }
}
