using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Zeitwirtschaft;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class ZeitwirtschaftCorrectionTests
{
    [Fact]
    public void Only_the_changed_time_is_stored()
    {
        var start = Local(2026, 11, 9, 20, 53);
        var end = Local(2026, 11, 9, 21, 57);
        var pauseStart = Local(2026, 11, 9, 23, 6);
        var pauseEnd = Local(2026, 11, 9, 23, 22);
        var correctedEnd = Local(2026, 11, 10, 1, 13);
        var correctedPauseEnd = Local(2026, 11, 9, 23, 30);
        var root = Document(start, end, pauseStart, pauseEnd);

        var ok = ZeitwirtschaftDocumentEditor.TryApplyCorrection(
            root,
            "1",
            "e1",
            correctedStartMs: null,
            correctedEndMs: correctedEnd,
            pauses:
            [
                new ZeitwirtschaftDocumentEditor.PauseCorrection(pauseStart, null, correctedPauseEnd)
            ],
            "planer",
            out var error);

        Assert.True(ok, error);
        var entry = Entry(root);
        Assert.False(entry.ContainsKey("correctedStartEpochMs"));
        Assert.Equal(correctedEnd, entry["correctedEndEpochMs"]!.GetValue<long>());
        var pause = entry["pauses"]!.AsArray()[0]!.AsObject();
        Assert.Equal(pauseStart, pause["startEpochMs"]!.GetValue<long>());
        Assert.Equal(pauseEnd, pause["endEpochMs"]!.GetValue<long>());
        Assert.False(pause.ContainsKey("correctedStartEpochMs"));
        Assert.Equal(correctedPauseEnd, pause["correctedEndEpochMs"]!.GetValue<long>());
    }

    [Fact]
    public void Same_minute_is_not_shown_as_a_correction()
    {
        var stamp = Local(2026, 11, 9, 20, 53);
        var sameMinute = stamp + 40_000;
        var text = ZeitwirtschaftMergeService.FormatCellWithCorrection(stamp, null, sameMinute, null);
        Assert.DoesNotContain("→", text);
        Assert.False(ZeitwirtschaftMergeService.IsRealCorrection(stamp, sameMinute));
    }

    [Fact]
    public void Check_in_place_uses_the_vehicle_name_from_the_roster()
    {
        var map = ZeitwirtschaftVehicleLabelResolver.BuildLabelMap(
        [
            new RegisteredVehicleItem
            {
                Name = "MGVH2606",
                PhoneNumber = "4915756102908",
                PlannerDetails = { VehicleType = "Citaro O530 G" }
            }
        ]);

        Assert.Equal("MGVH2606", ZeitwirtschaftVehicleLabelResolver.TryVehicleName("4915756102908", map));
        Assert.Null(ZeitwirtschaftVehicleLabelResolver.TryVehicleName("015237183253", map));
    }

    [Fact]
    public void Forgotten_stamp_is_stored_as_a_marked_manual_entry()
    {
        var root = new JsonObject
        {
            ["type"] = "zeitwirtschaft",
            ["drivers"] = new JsonObject()
        };
        var start = Local(2026, 11, 1, 8, 0);
        var end = Local(2026, 11, 1, 16, 30);
        var pauseStart = Local(2026, 11, 1, 12, 0);
        var pauseEnd = Local(2026, 11, 1, 12, 30);

        var ok = ZeitwirtschaftDocumentEditor.TryAppendManualEntry(
            root,
            "6969",
            "Test",
            "4915756102908",
            "MGVH2606",
            start,
            end,
            pauseStart,
            pauseEnd,
            "Stempelung vergessen",
            "planer",
            out var error);

        Assert.True(ok, error);
        var entry = root["drivers"]!["6969"]!["entries"]![0]!.AsObject();
        Assert.True(entry["manual"]!.GetValue<bool>());
        Assert.Equal("Stempelung vergessen", entry["manualReason"]!.GetValue<string>());
        Assert.Equal(start, entry["startEpochMs"]!.GetValue<long>());
        Assert.Equal(end, entry["endEpochMs"]!.GetValue<long>());
        Assert.False(entry.ContainsKey("correctedStartEpochMs"));
        Assert.Equal(pauseStart, entry["pauses"]![0]!["startEpochMs"]!.GetValue<long>());
    }

    [Fact]
    public void Pauses_shorter_than_15_minutes_are_marked_ignored()
    {
        var entry = new ZeitwirtschaftMergedEntry
        {
            EntryId = "e",
            PersonnelNumber = "1",
            Name = "Test",
            DevicePhone = "1",
            StartEpochMs = Local(2026, 11, 9, 20, 53),
            EndEpochMs = Local(2026, 11, 10, 1, 13),
            Pauses =
            [
                Pause(Local(2026, 11, 9, 23, 6), Local(2026, 11, 9, 23, 22)),
                Pause(Local(2026, 11, 10, 0, 6), Local(2026, 11, 10, 0, 14)),
                Pause(Local(2026, 11, 10, 0, 17), Local(2026, 11, 10, 0, 28))
            ]
        };

        var lines = ZeitwirtschaftMergeService.DescribePauses(entry);
        Assert.Equal(3, lines.Count);
        Assert.False(lines[0].Ignored);
        Assert.True(lines[1].Ignored);
        Assert.True(lines[2].Ignored);
    }

    private static ZeitwirtschaftPause Pause(long start, long end) =>
        new() { StartEpochMs = start, EndEpochMs = end };

    private static long Local(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeZoneInfo.Local.GetUtcOffset(
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified))).ToUnixTimeMilliseconds();

    private static JsonObject Document(long start, long end, long pauseStart, long pauseEnd) =>
        new()
        {
            ["drivers"] = new JsonObject
            {
                ["1"] = new JsonObject
                {
                    ["entries"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "e1",
                            ["startEpochMs"] = start,
                            ["endEpochMs"] = end,
                            ["pauses"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["startEpochMs"] = pauseStart,
                                    ["endEpochMs"] = pauseEnd
                                }
                            }
                        }
                    }
                }
            }
        };

    private static JsonObject Entry(JsonObject root) =>
        root["drivers"]!["1"]!["entries"]![0]!.AsObject();
}
