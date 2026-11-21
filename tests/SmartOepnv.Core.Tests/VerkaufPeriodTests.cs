using SmartOepnv.Core.Mascot;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Verkauf;
using Xunit;

namespace SmartOepnv.Core.Tests;

public sealed class VerkaufPeriodTests
{
    [Fact]
    public void Second_day_starts_one_minute_after_24_hours()
    {
        var first = Local(2026, 10, 31, 21, 20, 37);
        var origin = VerkaufDriverDocument.OperatingDayOriginMs(first.ToUnixTimeMilliseconds());
        Assert.Equal(Local(2026, 10, 31, 21, 20, 0).ToUnixTimeMilliseconds(), origin);

        var stillFirstDay = Local(2026, 11, 1, 21, 20, 59).ToUnixTimeMilliseconds();
        var secondDay = Local(2026, 11, 1, 21, 21, 0).ToUnixTimeMilliseconds();
        var firstMs = first.ToUnixTimeMilliseconds();

        Assert.Equal(1, VerkaufDriverDocument.CountSalesDays(firstMs, stillFirstDay));
        Assert.False(VerkaufDriverDocument.IsPeriodExpiredAt(firstMs, stillFirstDay));
        Assert.Equal(2, VerkaufDriverDocument.CountSalesDays(firstMs, secondDay));
    }

    [Fact]
    public void Lock_starts_after_five_full_days()
    {
        var first = Local(2026, 10, 31, 21, 20, 0);
        var firstMs = first.ToUnixTimeMilliseconds();
        var lastMinuteOfDay5 = first.AddDays(5).AddSeconds(59).ToUnixTimeMilliseconds();
        var lockAt = first.AddDays(5).AddMinutes(1).ToUnixTimeMilliseconds();

        Assert.Equal(5, VerkaufDriverDocument.CountSalesDays(firstMs, lastMinuteOfDay5));
        Assert.False(VerkaufDriverDocument.IsPeriodExpiredAt(firstMs, lastMinuteOfDay5));
        Assert.True(VerkaufDriverDocument.IsPeriodExpiredAt(firstMs, lockAt));
    }

    [Fact]
    public void Roster_keeps_device_admin_separate_from_employees()
    {
        var adminSales = new VerkaufDriverDocument
        {
            PersonnelNumber = "1995",
            Name = "Admin",
            SalesDayDates = ["2026-10-12"],
            Sales =
            [
                new VerkaufSale
                {
                    Id = "s1",
                    SoldAtMs = 1_791_765_381_406,
                    TotalCents = 800,
                    Lines = []
                }
            ],
            SourceFileName = "verkauf_1995.json"
        };
        var tim = new VerkaufDriverDocument
        {
            PersonnelNumber = "1003",
            Name = "Tim Thielen",
            SalesDayDates = [],
            Sales = [],
            SourceFileName = "verkauf_1003.json"
        };
        var roster = new[]
        {
            new EmployeeRosterItem { Name = "Tim Thielen", PersonnelNumber = "1003", Password = "1902" },
            new EmployeeRosterItem { Name = "Emanuel Kemper", PersonnelNumber = "1001", Password = "2346" },
            new EmployeeRosterItem { Name = "Christopher Lambers", PersonnelNumber = "1005", Password = "1005" }
        };

        var rows = VerkaufDriverListComposer.Compose([adminSales, tim], roster);

        Assert.Equal(4, rows.Count);
        Assert.Equal("Christopher Lambers", rows[2].Name);
        Assert.Equal("1005", rows[2].PersonnelNumber);
        Assert.Equal("verkauf_1005.json", rows[2].SourceFileName);
        Assert.Empty(rows[2].Sales);
        Assert.Equal("Admin", rows[3].Name);
        Assert.Equal("1995", rows[3].PersonnelNumber);
        Assert.Equal("verkauf_1995.json", rows[3].SourceFileName);
        Assert.Single(rows[3].Sales);
    }

    [Fact]
    public void Weather_line_matches_the_day()
    {
        Assert.Equal("Heute bleibt es trocken bei 15 Grad.", MascotWeatherText.Describe(1, 15));
        Assert.Equal("Heute droht Blitzeis. Es werden heute 8 Grad.", MascotWeatherText.Describe(66, 8));
        Assert.Equal("Heute wird es nass. Es werden heute 11 Grad.", MascotWeatherText.Describe(61, 11));
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute, int second)
    {
        var unspecified = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }
}
