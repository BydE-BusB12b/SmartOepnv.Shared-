using SmartOepnv.Core.VehicleTracking;
using Xunit;

namespace SmartOepnv.Core.Tests;

public sealed class GpsTripTraceArchiveTests
{
    [Fact]
    public void Absorb_keeps_days_that_left_the_seven_day_transfer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gps-trace-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var olderDay = Day(-10);
            var newerDay = Day(-1);
            var older = Trace("49111", olderDay, Stamp(-10, 8), 1000);
            var newerWindow = Trace("49111", newerDay, Stamp(-1, 9), 2000);

            GpsTripTraceArchive.Absorb(dir, "49111", older);
            var full = GpsTripTraceArchive.Absorb(dir, "49111", newerWindow);
            var parsed = GpsTripTraceParser.TryParse(full, "gps_trace_49111.json");

            Assert.NotNull(parsed);
            Assert.Equal(2, parsed!.Days.Count);
            Assert.Equal(olderDay, parsed.Days[0].Date);
            Assert.Equal(newerDay, parsed.Days[1].Date);
            Assert.Single(parsed.Days[1].Points);
            Assert.Equal(2, parsed.Events.Count);
            Assert.Equal(2000, parsed.UpdatedAtEpochMs);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Absorb_drops_days_older_than_30()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gps-trace-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stale = Trace("49111", Day(-40), Stamp(-40, 8), 1000);
            var fresh = Trace("49111", Day(-2), Stamp(-2, 9), 2000);

            GpsTripTraceArchive.Absorb(dir, "49111", stale);
            var full = GpsTripTraceArchive.Absorb(dir, "49111", fresh);
            var parsed = GpsTripTraceParser.TryParse(full, "gps_trace_49111.json");

            Assert.NotNull(parsed);
            Assert.Single(parsed!.Days);
            Assert.Equal(Day(-2), parsed.Days[0].Date);
            Assert.Single(parsed.Events);
            Assert.Equal(Stamp(-2, 9), parsed.Events[0].TimestampEpochMs);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static string Trace(string phone, string day, long stamp, long updatedAt) =>
        "{\"v\":1,\"phone\":\"" + phone + "\",\"name\":\"Bus 12\",\"updatedAt\":" + updatedAt +
        ",\"days\":[{\"d\":\"" + day + "\",\"p\":[{\"t\":" + stamp + ",\"lat\":51.2,\"lon\":6.8,\"v\":40},{\"t\":" + stamp + ",\"lat\":51.9,\"lon\":6.9,\"v\":1}]}]," +
        "\"events\":[{\"t\":" + stamp + ",\"k\":\"line\",\"u\":\"Anna\",\"d\":\"SB1\"}]}";

    private static string Day(int offset) =>
        BerlinToday().AddDays(offset).ToString("yyyy-MM-dd");

    private static long Stamp(int dayOffset, int hour)
    {
        var berlin = BerlinZone();
        var local = BerlinToday().AddDays(dayOffset).AddHours(hour);
        return new DateTimeOffset(local, berlin.GetUtcOffset(local)).ToUnixTimeMilliseconds();
    }

    private static DateTime BerlinToday() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BerlinZone()).Date;

    private static TimeZoneInfo BerlinZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }
}
