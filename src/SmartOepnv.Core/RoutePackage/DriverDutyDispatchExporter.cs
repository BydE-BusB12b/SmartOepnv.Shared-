using System.Text.Json.Nodes;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

public static class DriverDutyDispatchExporter
{
    public static void EmbedIntoRoot(
        JsonObject root,
        IReadOnlyList<DriverDispositionAssignment> assignments,
        IReadOnlyList<EmployeeRosterItem> employees,
        IReadOnlyList<DutyTemplate> templates)
    {
        var templateById = templates
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        var employeeByKey = employees
            .Select(e => new { Key = EmployeeDispoKeys.FromEmployee(e), Employee = e })
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Employee, StringComparer.Ordinal);

        var payloads = new List<DriverDutyDispatchPayload>();
        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.DutyTemplateId))
            {
                continue;
            }

            if (!templateById.TryGetValue(assignment.DutyTemplateId, out var template))
            {
                continue;
            }

            employeeByKey.TryGetValue(assignment.DriverKey, out var employee);
            payloads.Add(BuildPayload(assignment, template, employee));
        }

        var arr = new JsonArray();
        foreach (var payload in payloads.OrderBy(p => p.StartEpochMs))
        {
            arr.Add(WritePayload(payload));
        }

        root["driverDutyDispatches"] = arr;
        root["driverDutyDispatchesMeta"] = new JsonObject
        {
            ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["count"] = arr.Count
        };
    }

    public static DriverDutyDispatchPayload BuildPayload(
        DriverDispositionAssignment assignment,
        DutyTemplate template,
        EmployeeRosterItem? employee)
    {
        var personnel = employee is not null
            ? EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber)
            : EmployeeDispoKeys.TryGetPersonnelDigits(assignment.DriverKey) ?? string.Empty;

        return new DriverDutyDispatchPayload
        {
            AssignmentId = assignment.Id,
            DriverKey = assignment.DriverKey,
            PersonnelNumber = personnel,
            DutyTemplateId = template.Id,
            DutyTemplatePartIndex = assignment.DutyTemplatePartIndex > 0 ? assignment.DutyTemplatePartIndex : 1,
            Label = string.IsNullOrWhiteSpace(assignment.DutyNumber) ? assignment.Label : assignment.DutyNumber,
            StartEpochMs = assignment.StartEpochMs,
            EndEpochMs = assignment.EndEpochMs,
            Part1EndEpochMs = assignment.Part1EndEpochMs,
            Part2StartEpochMs = assignment.Part2StartEpochMs,
            DutyNumber = !string.IsNullOrWhiteSpace(assignment.DutyNumber)
                ? assignment.DutyNumber
                : template.DutyNumber,
            DutyNumberPart2 = template.DutyNumberPart2,
            DutyNumberPart3 = template.DutyNumberPart3,
            DefaultLineCourse = template.DefaultLineCourse,
            VehicleNumber = template.VehicleNumber,
            Notes = template.Notes,
            Trips = BuildTrips(template, assignment.DutyTemplatePartIndex)
        };
    }

    private static List<DriverDutyDispatchTripPayload> BuildTrips(DutyTemplate template, int partIndex)
    {
        var trips = new List<DriverDutyDispatchTripPayload>();
        if (partIndex == 1)
        {
            AppendTrips(trips, template.Rows, 1, template.DefaultLineCourse);
        }
        else if (partIndex == 2)
        {
            AppendTrips(trips, template.Part2Rows, 2, template.DefaultLineCourse);
        }
        else if (partIndex == 3)
        {
            AppendTrips(trips, template.Part3Rows, 3, template.DefaultLineCourse);
        }
        else
        {
            AppendTrips(trips, template.Rows, 1, template.DefaultLineCourse);
            AppendTrips(trips, template.Part2Rows, 2, template.DefaultLineCourse);
            AppendTrips(trips, template.Part3Rows, 3, template.DefaultLineCourse);
        }

        return trips;
    }

    private static void AppendTrips(
        List<DriverDutyDispatchTripPayload> trips,
        IReadOnlyList<DutyTemplateRow> rows,
        int partIndex,
        string defaultLineCourse)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.FromTime) &&
                string.IsNullOrWhiteSpace(row.ToTime) &&
                string.IsNullOrWhiteSpace(row.FromStop) &&
                string.IsNullOrWhiteSpace(row.ToStop))
            {
                continue;
            }

            trips.Add(new DriverDutyDispatchTripPayload
            {
                TripNumber = row.TripNumber,
                LineCourse = string.IsNullOrWhiteSpace(row.LineCourse) ? defaultLineCourse : row.LineCourse,
                Remark = row.Remark,
                Destination = row.Destination,
                FromTime = row.FromTime,
                FromStop = row.FromStop,
                ToTime = row.ToTime,
                ToStop = row.ToStop,
                PartIndex = partIndex
            });
        }
    }

    private static JsonObject WritePayload(DriverDutyDispatchPayload payload)
    {
        var trips = new JsonArray();
        foreach (var trip in payload.Trips)
        {
            trips.Add(new JsonObject
            {
                ["tripNumber"] = trip.TripNumber,
                ["lineCourse"] = trip.LineCourse,
                ["remark"] = trip.Remark,
                ["destination"] = trip.Destination,
                ["fromTime"] = trip.FromTime,
                ["fromStop"] = trip.FromStop,
                ["toTime"] = trip.ToTime,
                ["toStop"] = trip.ToStop,
                ["partIndex"] = trip.PartIndex
            });
        }

        return new JsonObject
        {
            ["assignmentId"] = payload.AssignmentId,
            ["driverKey"] = payload.DriverKey,
            ["personnelNumber"] = payload.PersonnelNumber,
            ["dutyTemplateId"] = payload.DutyTemplateId,
            ["dutyTemplatePartIndex"] = payload.DutyTemplatePartIndex,
            ["label"] = payload.Label,
            ["startEpochMs"] = payload.StartEpochMs,
            ["endEpochMs"] = payload.EndEpochMs,
            ["part1EndEpochMs"] = payload.Part1EndEpochMs,
            ["part2StartEpochMs"] = payload.Part2StartEpochMs,
            ["dutyNumber"] = payload.DutyNumber,
            ["dutyNumberPart2"] = payload.DutyNumberPart2,
            ["dutyNumberPart3"] = payload.DutyNumberPart3,
            ["defaultLineCourse"] = payload.DefaultLineCourse,
            ["vehicleNumber"] = payload.VehicleNumber,
            ["notes"] = payload.Notes,
            ["trips"] = trips
        };
    }
}
