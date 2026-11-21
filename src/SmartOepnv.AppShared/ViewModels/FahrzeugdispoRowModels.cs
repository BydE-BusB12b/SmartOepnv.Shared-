using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>
/// Spuren für überlappende Balken. Die Überlappung gilt pro Balken:
/// ein einzelner Dienst ohne Überschneidung bleibt hoch.
/// </summary>
internal static class DispositionOverlapLanes
{
    public static void Assign(
        IReadOnlyList<(string Id, long StartEpochMs, long EndEpochMs)> assignments,
        out Dictionary<string, int> laneById,
        out int laneCount,
        out HashSet<string> overlappingIds)
    {
        laneById = new Dictionary<string, int>(StringComparer.Ordinal);
        overlappingIds = new HashSet<string>(StringComparer.Ordinal);
        laneCount = 0;
        if (assignments.Count == 0)
        {
            return;
        }

        var ordered = assignments.OrderBy(a => a.StartEpochMs).ToList();
        var laneEnds = new List<long>(4);
        var laneOccupants = new List<string>(4);
        foreach (var assignment in ordered)
        {
            var lane = -1;
            var overlaps = false;
            for (var i = 0; i < laneEnds.Count; i++)
            {
                if (laneEnds[i] <= assignment.StartEpochMs)
                {
                    if (lane < 0)
                    {
                        lane = i;
                    }
                }
                else
                {
                    overlaps = true;
                    overlappingIds.Add(laneOccupants[i]);
                }
            }

            if (overlaps)
            {
                overlappingIds.Add(assignment.Id);
            }

            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(assignment.EndEpochMs);
                laneOccupants.Add(assignment.Id);
            }
            else
            {
                laneEnds[lane] = assignment.EndEpochMs;
                laneOccupants[lane] = assignment.Id;
            }

            laneById[assignment.Id] = lane;
        }

        laneCount = laneEnds.Count;
    }
}

public sealed partial class FahrzeugdispoVehicleRowVm : ObservableObject
{
    public string PhoneKey { get; init; } = string.Empty;

    public string PersonnelNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string RowColorHex { get; init; } = string.Empty;

    public bool IsHourMode { get; init; }

    public string HourModeDateLabel { get; init; } = string.Empty;

    public ObservableCollection<FahrzeugdispoTimeCellVm> Cells { get; } = [];

    public ObservableCollection<FahrzeugdispoAssignmentBarVm> AssignmentBars { get; } = [];
}

public sealed partial class FahrzeugdispoAssignmentBarVm : ObservableObject
{
    public string AssignmentId { get; init; } = string.Empty;

    public string VehiclePhoneKey { get; init; } = string.Empty;

    public DateTime FirstVisibleDate { get; init; }

    public int SpanDays { get; init; } = 1;

    public double Left { get; init; }

    public double Width { get; init; }

    public double Top { get; init; }

    public double Height { get; init; } = 40;

    public int LaneIndex { get; init; }

    public bool IsHourViewBar { get; init; }

    public bool TimeLabelAboveBar { get; init; } = true;

    public string TimeLabel { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public double Work1Ratio { get; init; } = 1;

    public double GapRatio { get; init; }

    public double Work2Ratio { get; init; }

    public double Work1Width { get; init; }

    public double GapWidth { get; init; }

    public double Work2Width { get; init; }

    public bool IsSplitShiftBar { get; init; }

    /// <summary>Dienstnummer „Bereitschaft“ – lila Balken, flexible Zeit.</summary>
    public bool IsStandbyBar { get; init; }

    public string Tooltip { get; init; } = string.Empty;
}

public sealed partial class FahrzeugdispoDayHeaderVm : ObservableObject
{
    public string Header { get; init; } = string.Empty;

    public bool IsWeekend { get; init; }

    public DateTime Date { get; init; }

    public double CellWidth { get; init; } = 96;
}

public sealed partial class FahrzeugdispoTimeCellVm : ObservableObject
{
    public string VehiclePhoneKey { get; init; } = string.Empty;

    public string Header { get; init; } = string.Empty;

    public string Tooltip { get; init; } = string.Empty;

    public DateTime? Date { get; init; }

    public int? Hour { get; init; }

    public bool IsHourCell => Hour is not null;

    public bool IsCollapseCell { get; init; }

    public bool IsExpandedTarget { get; init; }

    public double CellWidth { get; init; } = 96;

    public ObservableCollection<VehicleDispositionAssignment> Assignments { get; init; } = [];

    public bool HasAssignments => Assignments.Count > 0;

    public string AssignmentSummary =>
        Assignments.Count == 0
            ? string.Empty
            : string.Join(
                "\n",
                Assignments.Select(FormatAssignmentLine));

    private static string FormatAssignmentLine(VehicleDispositionAssignment a)
    {
        var label = string.IsNullOrWhiteSpace(a.Label)
            ? FormatAssignmentRange(a)
            : a.Label;
        return string.IsNullOrWhiteSpace(a.DriverName)
            ? label
            : $"{label}\n{a.DriverName}";
    }

    private static string FormatAssignmentRange(VehicleDispositionAssignment a)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(a.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(a.EndEpochMs).ToLocalTime();
        return start.Date == end.Date
            ? $"{start:HH:mm}–{end:HH:mm}"
            : $"{start:dd.MM. HH:mm} – {end:dd.MM. HH:mm}";
    }
}
