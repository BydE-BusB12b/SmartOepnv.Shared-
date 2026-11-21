namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ein voller Kalendermonat. Maßnahmen stehen nur an Tagen mit Gültigkeit.</summary>
public static class RouteChainCalendar
{
    public const int DayCount = 42;

    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    public static IReadOnlyList<RouteChainCalendarDay> Build(EditableRoutePackage editor, DateOnly month)
    {
        var first = MonthOf(month);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-offset);
        var measures = Collect(editor);
        var days = new List<RouteChainCalendarDay>(DayCount);
        for (var index = 0; index < DayCount; index++)
        {
            var date = gridStart.AddDays(index);
            var inMonth = date.Year == first.Year && date.Month == first.Month;
            var entries = measures
                .Where(measure => measure.Chain.Any(key => RunsOnDatedDay(editor, key, date)))
                .Select(measure => new RouteChainCalendarEntry(measure.Label, measure.HeadRouteKey))
                .ToList();
            days.Add(new RouteChainCalendarDay(date, inMonth, entries));
        }

        return days;
    }

    /// <summary>
    /// Sechs Wochenzeilen. Eine Maßnahme ist pro zusammenhängender Tagesfolge ein Balken,
    /// der Text steht einmal darauf. Überlappende Maßnahmen liegen untereinander.
    /// </summary>
    public static IReadOnlyList<RouteChainCalendarWeek> BuildWeeks(IReadOnlyList<RouteChainCalendarDay> days)
    {
        var weeks = new List<RouteChainCalendarWeek>();
        for (var start = 0; start + 7 <= days.Count; start += 7)
        {
            var slice = days.Skip(start).Take(7).ToList();
            weeks.Add(new RouteChainCalendarWeek(slice, BarsForWeek(slice)));
        }

        return weeks;
    }

    private static List<RouteChainCalendarBar> BarsForWeek(IReadOnlyList<RouteChainCalendarDay> week)
    {
        var order = new List<string>();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in week.SelectMany(day => day.Entries))
        {
            if (labels.ContainsKey(entry.HeadRouteKey))
            {
                continue;
            }

            labels[entry.HeadRouteKey] = entry.Label;
            order.Add(entry.HeadRouteKey);
        }

        var bars = new List<RouteChainCalendarBar>();
        var lane = 0;
        foreach (var key in order)
        {
            var runStart = -1;
            var placed = false;
            for (var index = 0; index <= week.Count; index++)
            {
                var active = index < week.Count &&
                             week[index].Entries.Any(entry =>
                                 string.Equals(entry.HeadRouteKey, key, StringComparison.Ordinal));
                if (active && runStart < 0)
                {
                    runStart = index;
                }

                if (!active && runStart >= 0)
                {
                    bars.Add(new RouteChainCalendarBar(
                        labels[key],
                        key,
                        runStart,
                        index - runStart,
                        lane));
                    placed = true;
                    runStart = -1;
                }
            }

            if (placed)
            {
                lane++;
            }
        }

        return bars;
    }

    private static List<ChainMeasure> Collect(EditableRoutePackage editor)
    {
        var measures = new List<ChainMeasure>();
        foreach (var head in RouteChainPlanner.ListChainHeads(editor, editor.RouteNames.ToList()))
        {
            var chain = RouteChainPlanner.BuildConnectedRouteChain(editor, head);
            if (chain.Count == 0)
            {
                chain = [head];
            }

            var label = CalendarLabel(editor, head, chain);
            if (label.Length == 0)
            {
                continue;
            }

            measures.Add(new ChainMeasure(head, label, chain));
        }

        measures.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
        return measures;
    }

    /// <summary>Nur der Kalender zeigt den Schnurnamen. Die Routenliste bleibt bei Linie und Fahrt.</summary>
    private static string CalendarLabel(
        EditableRoutePackage editor,
        string head,
        IReadOnlyList<string> chain)
    {
        var name = chain
            .Select(key => editor.GetRouteChainIdentity(key).Name)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return RouteChainPlanner.FormatChainLabel(editor, head).Trim();
        }

        var order = chain
            .Select(key => editor.GetRouteChainIdentity(key).OrderNumber)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim() ?? string.Empty;
        return order.Length == 0 ? name : $"{name} ({order})";
    }

    /// <summary>
    /// Tag gehört zur Maßnahme, wenn die Fahrt an dem Tag fährt und ein Zeitraum oder einzelne Betriebstage gesetzt sind.
    /// Ohne Datum gäbe es kein „wann“.
    /// </summary>
    private static bool RunsOnDatedDay(EditableRoutePackage editor, string routeKey, DateOnly date)
    {
        if (!RouteChainPlanner.IsRouteActiveOn(editor, routeKey, date))
        {
            return false;
        }

        var range = editor.GetRouteDateRange(routeKey);
        if (range.IsRestricted && range.Contains(date))
        {
            return true;
        }

        var dates = editor.GetRouteOperatingDates(routeKey);
        return RouteOperatingDatesEditor.IsRestricted(dates) && dates.Contains(date);
    }

    private sealed class ChainMeasure(string headRouteKey, string label, IReadOnlyList<string> chain)
    {
        public string HeadRouteKey { get; } = headRouteKey;

        public string Label { get; } = label;

        public IReadOnlyList<string> Chain { get; } = chain;
    }
}

public sealed class RouteChainCalendarEntry(string label, string headRouteKey)
{
    public string Label { get; } = label;

    public string HeadRouteKey { get; } = headRouteKey;
}

public sealed class RouteChainCalendarBar(
    string label,
    string headRouteKey,
    int column,
    int columnSpan,
    int lane)
{
    public string Label { get; } = label;

    public string HeadRouteKey { get; } = headRouteKey;

    public int Column { get; } = column;

    public int ColumnSpan { get; } = columnSpan;

    public int Lane { get; } = lane;
}

public sealed class RouteChainCalendarWeek(
    IReadOnlyList<RouteChainCalendarDay> days,
    IReadOnlyList<RouteChainCalendarBar> bars)
{
    public IReadOnlyList<RouteChainCalendarDay> Days { get; } = days;

    public IReadOnlyList<RouteChainCalendarBar> Bars { get; } = bars;
}

public sealed class RouteChainCalendarDay(
    DateOnly date,
    bool inMonth,
    IReadOnlyList<RouteChainCalendarEntry> entries)
{
    public DateOnly Date { get; } = date;

    public bool InMonth { get; } = inMonth;

    public int DayNumber => Date.Day;

    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<RouteChainCalendarEntry> Entries { get; } = entries;
}
