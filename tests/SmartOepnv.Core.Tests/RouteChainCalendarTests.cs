using System.Text.Json.Nodes;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class RouteChainCalendarTests
{
    [Fact]
    public void Named_chain_shows_on_its_validity_days_with_order_number()
    {
        var package = EditableRoutePackage.FromJson("""{"routes":["047 Hbf > See"]}""");
        const string route = "047 Hbf > See";
        package.SetRouteChainIdentity(route, "4711", 4, "Umleitung Hbf");
        package.SetRouteDateRange(route, new RouteDateRange
        {
            From = new DateOnly(2026, 11, 10),
            To = new DateOnly(2026, 11, 12)
        });

        Assert.DoesNotContain("Umleitung", RouteChainPlanner.FormatChainLabel(package, route), StringComparison.Ordinal);

        var days = RouteChainCalendar.Build(package, new DateOnly(2026, 11, 15));

        Assert.Equal(RouteChainCalendar.DayCount, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), days[0].Date);
        Assert.False(days[0].InMonth);
        Assert.Equal(30, days.Count(day => day.InMonth));
        Assert.Equal(
            ["Umleitung Hbf (4711)"],
            days.Single(day => day.Date == new DateOnly(2026, 11, 10)).Entries.Select(entry => entry.Label));
        Assert.Equal(
            ["Umleitung Hbf (4711)"],
            days.Single(day => day.Date == new DateOnly(2026, 11, 12)).Entries.Select(entry => entry.Label));
        Assert.Empty(days.Single(day => day.Date == new DateOnly(2026, 11, 9)).Entries);
        Assert.Empty(days.Single(day => day.Date == new DateOnly(2026, 11, 13)).Entries);

        var bar = Assert.Single(RouteChainCalendar.BuildWeeks(days)[2].Bars);
        Assert.Equal("Umleitung Hbf (4711)", bar.Label);
        Assert.Equal(1, bar.Column);
        Assert.Equal(3, bar.ColumnSpan);
        Assert.Equal(0, bar.Lane);
    }

    [Fact]
    public void Weekday_and_undated_chains_stay_off_the_wrong_days()
    {
        var package = EditableRoutePackage.FromJson("""{"routes":["Mo-Massnahme","Immer"]}""");
        package.SetRouteChainIdentity("Mo-Massnahme", "100", 1, "Nur Montag");
        package.SetRouteDateRange("Mo-Massnahme", new RouteDateRange
        {
            From = new DateOnly(2026, 11, 1),
            To = new DateOnly(2026, 11, 30)
        });
        package.SetRouteOperatingDays("Mo-Massnahme", [DutyOperatingDay.Monday]);
        package.SetRouteChainIdentity("Immer", "200", 2, "Ohne Datum");

        var days = RouteChainCalendar.Build(package, new DateOnly(2026, 11, 1));
        var monday = new DateOnly(2026, 11, 2);
        var tuesday = new DateOnly(2026, 11, 3);

        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.Equal(["Nur Montag (100)"], Labels(days, monday));
        Assert.Empty(Labels(days, tuesday));
        var mondayBars = RouteChainCalendar.BuildWeeks(days)
            .SelectMany(week => week.Bars)
            .Where(bar => bar.Label.StartsWith("Nur Montag", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(mondayBars);
        Assert.All(mondayBars, bar =>
        {
            Assert.Equal(0, bar.Column);
            Assert.Equal(1, bar.ColumnSpan);
        });
        Assert.DoesNotContain(days, day => day.Entries.Any(entry => entry.Label.Contains("Ohne Datum", StringComparison.Ordinal)));
    }

    [Fact]
    public void Chain_name_roundtrips_in_json()
    {
        var package = EditableRoutePackage.FromJson("""{"routes":["047 Hbf"]}""");
        package.SetRouteChainIdentity("047 Hbf", "4711", 8, "SEV Hbf");

        var root = new JsonObject();
        RouteChainIdentityEditor.SaveToRoot(root, package.RouteNames, package.RouteChainIdentitiesByRoute);
        var loaded = RouteChainIdentityEditor.LoadFromRoot(root);
        var identity = Assert.Single(loaded).Value;

        Assert.Equal("SEV Hbf", identity.Name);
        Assert.Equal("4711", identity.OrderNumber);
        Assert.Equal(8, identity.SequenceNumber);
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<RouteChainCalendarDay> days, DateOnly date) =>
        days.Single(day => day.Date == date).Entries.Select(entry => entry.Label).ToList();
}
