using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public sealed partial class FahrzeugdispoVehicleRowVm : ObservableObject
{
    public string PhoneKey { get; init; } = string.Empty;

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
