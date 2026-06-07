using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class FahrzeugdispoViewModel : EditorStatusViewModelBase
{
    public const int VisibleDayCount = 7;

    public const double DayCellWidth = 96;

    public const double HourCellWidth = 24;

    public const double HourCollapseCellWidth = 24;

    public const double AssignmentBarRowHeight = 56;

    public const double AssignmentBarVerticalMargin = 4;

    public const double AssignmentBarLaneGap = 2;

    public const double HourBarStripHeight = 10;

    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    private readonly List<VehicleDispositionAssignment> _assignments = [];

    private string? _expandedPhoneKey;

    private DateTime? _expandedDate;

    private bool _flushRegistered;

    private readonly EditorAreaSyncState _sync = new();

    private string? _loadedFingerprint;

    public FahrzeugdispoViewModel()
        : base("Kalenderschnur: Fahrzeuge links, Tage rechts – Tag anklicken für Stundenansicht.")
    {
        ViewStartDate = GetWeekStart(DateTime.Today);
        EnsureFlushRegistered();
    }

    private void EnsureFlushRegistered()
    {
        if (_flushRegistered || !AppServices.IsInitialized)
        {
            return;
        }

        AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        _flushRegistered = true;
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    /// <summary>Beim Verlassen der Ansicht oder App-Ende: Fahrten sicher auf Platte schreiben.</summary>
    public void CommitChangesIfDirty()
    {
        var fingerprint = ComputeFingerprint();
        if (!_sync.ShouldCommit(fingerprint, _loadedFingerprint))
        {
            return;
        }

        if (PersistAssignments())
        {
            _sync.AfterCommit();
            _loadedFingerprint = fingerprint;
        }
    }

    public void CommitChanges() => CommitChangesIfDirty();

    [ObservableProperty] private DateTime viewStartDate;

    [ObservableProperty] private bool showOnlyActiveVehicles = true;

    [ObservableProperty] private ObservableCollection<FahrzeugdispoDayHeaderVm> dayHeaders = [];

    [ObservableProperty] private ObservableCollection<FahrzeugdispoVehicleRowVm> vehicleRows = [];

    [ObservableProperty] private int vehicleCount;

    [ObservableProperty] private int visibleVehicleCount;

    [ObservableProperty] private bool hasExpandedRow;

    partial void OnViewStartDateChanged(DateTime value) => RebuildGrid();

    partial void OnShowOnlyActiveVehiclesChanged(bool value) => RebuildGrid();

    public void RefreshFromEditorIfNeeded()
    {
        if (!_sync.ShouldRefresh(_assignments.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
    {
        EnsureFlushRegistered();
        _assignments.Clear();
        if (AppServices.PlannerLocal is not null)
        {
            _assignments.AddRange(AppServices.PlannerLocal.LoadVehicleDisposition());
        }

        var editor = AppServices.Routes.Editor;
        VehicleCount = editor?.RegisteredVehicles.Count ?? 0;
        if (editor is null)
        {
            VehicleRows.Clear();
            DayHeaders.Clear();
            VisibleVehicleCount = 0;
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            _sync.AfterRefresh();
            _loadedFingerprint = ComputeFingerprint();
            return;
        }

        RebuildGrid();
        _sync.AfterRefresh();
        _loadedFingerprint = ComputeFingerprint();
    }

    [RelayCommand]
    private void PreviousWeek() => ViewStartDate = ViewStartDate.AddDays(-VisibleDayCount);

    [RelayCommand]
    private void NextWeek() => ViewStartDate = ViewStartDate.AddDays(VisibleDayCount);

    [RelayCommand]
    private void GoToToday() => ViewStartDate = GetWeekStart(DateTime.Today);

    [RelayCommand]
    private void AddTrip()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        if (editor.RegisteredVehicles.Count == 0)
        {
            StatusMessage = "Keine Fahrzeuge – bitte zuerst unter Fahrzeugverwaltung anlegen.";
            return;
        }

        if (!TryShowTripDialog(editor.RegisteredVehicles.ToList(), ViewStartDate, existing: null, out var result))
        {
            return;
        }

        if (result.DeleteRequested)
        {
            return;
        }

        _assignments.Add(new VehicleDispositionAssignment
        {
            VehiclePhone = result.PhoneKey,
            StartEpochMs = result.StartEpochMs,
            EndEpochMs = result.EndEpochMs,
            Label = result.DisplayLabel
        });

        SaveAndRefresh(result.StartEpochMs, "Fahrt gespeichert.");
    }

    public void EditTrip(string assignmentId)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var existing = _assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (existing is null)
        {
            return;
        }

        if (!TryShowTripDialog(editor.RegisteredVehicles.ToList(), ViewStartDate, existing, out var result))
        {
            return;
        }

        if (result.DeleteRequested)
        {
            _assignments.RemoveAll(a => a.Id == assignmentId);
            SaveAndRefresh(null, "Fahrt gelöscht.");
            return;
        }

        existing.VehiclePhone = result.PhoneKey;
        existing.StartEpochMs = result.StartEpochMs;
        existing.EndEpochMs = result.EndEpochMs;
        existing.Label = result.DisplayLabel;
        SaveAndRefresh(result.StartEpochMs, "Fahrt aktualisiert.");
    }

    [RelayCommand]
    private void ToggleDayCell(FahrzeugdispoTimeCellVm? cell)
    {
        if (cell is null || cell.IsCollapseCell || cell.Date is null)
        {
            return;
        }

        if (_expandedPhoneKey == cell.VehiclePhoneKey && _expandedDate == cell.Date.Value.Date)
        {
            CollapseHourView();
            return;
        }

        OpenHourView(cell.VehiclePhoneKey, cell.Date.Value);
    }

    public void OpenHourViewFromAssignmentBar(string vehiclePhoneKey, DateTime date) =>
        OpenHourView(vehiclePhoneKey, date);

    private void OpenHourView(string vehiclePhoneKey, DateTime date)
    {
        _expandedPhoneKey = vehiclePhoneKey;
        _expandedDate = date.Date;
        RebuildGrid();
        StatusMessage =
            $"Stundenansicht für {date:dd.MM.yyyy} – „Wochenansicht“ oder ◀ zum Schließen.";
    }

    [RelayCommand]
    private void CollapseHourView()
    {
        if (_expandedPhoneKey is null && _expandedDate is null)
        {
            return;
        }

        _expandedPhoneKey = null;
        _expandedDate = null;
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        var days = Enumerable.Range(0, VisibleDayCount)
            .Select(i => ViewStartDate.Date.AddDays(i))
            .ToList();

        var vehicles = editor.RegisteredVehicles
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.PhoneNumber, StringComparer.OrdinalIgnoreCase)
            .Where(v => !ShowOnlyActiveVehicles || v.PlannerDetails.IsActive)
            .ToList();

        VisibleVehicleCount = vehicles.Count;
        HasExpandedRow = _expandedPhoneKey is not null && _expandedDate is not null;

        if (HasExpandedRow && _expandedDate is not null)
        {
            var hourTimelineWidth = HourCollapseCellWidth + (24 * HourCellWidth);
            DayHeaders =
            [
                new FahrzeugdispoDayHeaderVm
                {
                    Header = FormatDayHeader(_expandedDate.Value),
                    IsWeekend = _expandedDate.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    Date = _expandedDate.Value,
                    CellWidth = hourTimelineWidth
                }
            ];
        }
        else
        {
            DayHeaders =
            [
                .. days.Select(d => new FahrzeugdispoDayHeaderVm
                {
                    Header = FormatDayHeader(d),
                    IsWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    Date = d,
                    CellWidth = DayCellWidth
                })
            ];
        }

        var rows = new ObservableCollection<FahrzeugdispoVehicleRowVm>();
        foreach (var vehicle in vehicles)
        {
            var dispoKey = RegisteredVehicleDispoKeys.FromVehicle(vehicle);
            var row = new FahrzeugdispoVehicleRowVm
            {
                Title = string.IsNullOrWhiteSpace(vehicle.Name) ? vehicle.PhoneNumber : vehicle.Name,
                Subtitle = BuildVehicleSubtitle(vehicle),
                RowColorHex = vehicle.PlannerDetails.DispoRowColor,
                PhoneKey = dispoKey,
                IsHourMode = _expandedPhoneKey == dispoKey && _expandedDate is not null,
                HourModeDateLabel = _expandedDate?.ToString("dd.MM.yyyy", DeCulture) ?? string.Empty
            };

            if (row.IsHourMode && _expandedDate is not null)
            {
                row.Cells.Add(CreateCollapseCell(dispoKey, _expandedDate.Value));
                for (var hour = 0; hour < 24; hour++)
                {
                    row.Cells.Add(CreateHourCell(dispoKey, _expandedDate.Value, hour));
                }

                foreach (var bar in BuildHourAssignmentBars(dispoKey, _expandedDate.Value))
                {
                    row.AssignmentBars.Add(bar);
                }
            }
            else
            {
                foreach (var day in days)
                {
                    row.Cells.Add(CreateDayCell(dispoKey, day));
                }

                foreach (var bar in BuildAssignmentBars(dispoKey, days))
                {
                    row.AssignmentBars.Add(bar);
                }
            }

            rows.Add(row);
        }

        VehicleRows = rows;
        StatusMessage =
            $"{VisibleVehicleCount} von {VehicleCount} Fahrzeugen – " +
            $"{ViewStartDate:dd.MM.yyyy} bis {days[^1]:dd.MM.yyyy}. " +
            "„Neue Fahrt“ zum Eintragen, Rechtsklick auf Balken zum Bearbeiten – wird automatisch gespeichert.";
    }

    private void SaveAndRefresh(long? focusStartEpochMs, string message)
    {
        if (focusStartEpochMs is not null)
        {
            EnsureWeekVisible(focusStartEpochMs.Value);
        }

        RebuildGrid();
        ReportSaveSuccess(message);
        _ = PersistAssignmentsAsync();
    }

    private async Task PersistAssignmentsAsync()
    {
        var success = await Task.Run(PersistAssignments).ConfigureAwait(true);
        if (success)
        {
            _sync.AfterCommit();
            _loadedFingerprint = ComputeFingerprint();
        }
    }

    private bool TryShowTripDialog(
        IReadOnlyList<RegisteredVehicleItem> vehicles,
        DateTime defaultDate,
        VehicleDispositionAssignment? existing,
        out FahrzeugdispoTripDialogResult result)
    {
        result = default;
        var dialog = new FahrzeugdispoNewTripDialog(vehicles, defaultDate, existing, _assignments)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        result = new FahrzeugdispoTripDialogResult
        {
            DeleteRequested = dialog.DeleteRequested,
            PhoneKey = dialog.SelectedPhoneKey,
            StartEpochMs = dialog.StartEpochMs,
            EndEpochMs = dialog.EndEpochMs,
            DisplayLabel = dialog.TripName
        };
        return true;
    }

    private string ComputeFingerprint() =>
        JsonSerializer.Serialize(_assignments
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new
            {
                a.Id,
                a.VehiclePhone,
                a.StartEpochMs,
                a.EndEpochMs,
                a.Label
            }));

    private bool PersistAssignments()
    {
        if (AppServices.PlannerLocal is null)
        {
            ReportSaveError("Disposition konnte nicht gespeichert werden (Planer-Overlay fehlt).");
            return false;
        }

        try
        {
            AppServices.PlannerLocal.SaveVehicleDisposition(_assignments);
            return true;
        }
        catch (Exception ex)
        {
            ReportSaveError($"Speichern fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    private sealed class FahrzeugdispoTripDialogResult
    {
        public bool DeleteRequested { get; init; }

        public string PhoneKey { get; init; } = string.Empty;

        public long StartEpochMs { get; init; }

        public long EndEpochMs { get; init; }

        public string DisplayLabel { get; init; } = string.Empty;
    }

    private void EnsureWeekVisible(long startEpochMs)
    {
        var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime.Date;
        var weekEnd = ViewStartDate.Date.AddDays(VisibleDayCount - 1);
        if (startLocal < ViewStartDate.Date || startLocal > weekEnd)
        {
            ViewStartDate = GetWeekStart(startLocal);
        }
    }

    private FahrzeugdispoTimeCellVm CreateCollapseCell(string phoneKey, DateTime date) =>
        new()
        {
            VehiclePhoneKey = phoneKey,
            Header = "◀",
            Tooltip = $"Zurück zur Tagesansicht ({date:dd.MM.yyyy})",
            IsCollapseCell = true,
            CellWidth = HourCollapseCellWidth,
            Date = date
        };

    private FahrzeugdispoTimeCellVm CreateHourCell(string phoneKey, DateTime date, int hour) =>
        new()
        {
            VehiclePhoneKey = phoneKey,
            Header = $"{hour:00}",
            Hour = hour,
            Date = date,
            CellWidth = HourCellWidth
        };

    private IEnumerable<FahrzeugdispoAssignmentBarVm> BuildHourAssignmentBars(string dispoKey, DateTime date)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var dayStartMs = new DateTimeOffset(dayStart).ToUnixTimeMilliseconds();
        var dayEndMs = new DateTimeOffset(dayEnd).ToUnixTimeMilliseconds();

        var visibleAssignments = _assignments
            .Where(a => string.Equals(a.VehiclePhone, dispoKey, StringComparison.Ordinal))
            .Where(a => a.StartEpochMs < dayEndMs && a.EndEpochMs > dayStartMs)
            .OrderBy(a => a.StartEpochMs)
            .ToList();

        AssignOverlapLanes(visibleAssignments, out var laneById, out var laneCount);

        foreach (var assignment in visibleAssignments)
        {
            var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).LocalDateTime;
            var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).LocalDateTime;

            var visibleStart = startLocal < dayStart ? dayStart : startLocal;
            var visibleEnd = endLocal > dayEnd ? dayEnd : endLocal;
            if (visibleEnd <= visibleStart)
            {
                continue;
            }

            var startOffsetHours = (visibleStart - dayStart).TotalHours;
            var durationHours = (visibleEnd - visibleStart).TotalHours;
            var timeLabel = FormatAssignmentRange(assignment);
            var tripName = string.IsNullOrWhiteSpace(assignment.Label) ? string.Empty : assignment.Label;

            var laneIndex = laneById.GetValueOrDefault(assignment.Id, 0);
            var hasOverlap = visibleAssignments.Any(other =>
                other.Id != assignment.Id &&
                other.StartEpochMs < assignment.EndEpochMs &&
                other.EndEpochMs > assignment.StartEpochMs);
            var (top, height, labelAbove) = GetHourAssignmentBarVerticalLayout(
                laneIndex,
                hasOverlap ? laneCount : 1);

            yield return new FahrzeugdispoAssignmentBarVm
            {
                AssignmentId = assignment.Id,
                VehiclePhoneKey = dispoKey,
                FirstVisibleDate = dayStart,
                SpanDays = 1,
                Left = HourCollapseCellWidth + (startOffsetHours * HourCellWidth),
                Width = Math.Max(durationHours * HourCellWidth, 4),
                Top = top,
                Height = height,
                LaneIndex = laneIndex,
                IsHourViewBar = true,
                TimeLabelAboveBar = labelAbove,
                TimeLabel = timeLabel,
                Label = tripName,
                Tooltip = string.IsNullOrWhiteSpace(tripName)
                    ? $"{timeLabel} – Rechtsklick zum Bearbeiten"
                    : $"{tripName} ({timeLabel}) – Rechtsklick zum Bearbeiten"
            };
        }
    }

    private FahrzeugdispoTimeCellVm CreateDayCell(string phoneKey, DateTime date)
    {
        var isExpandedTarget = _expandedPhoneKey == phoneKey && _expandedDate == date.Date;
        return new FahrzeugdispoTimeCellVm
        {
            VehiclePhoneKey = phoneKey,
            Date = date,
            IsExpandedTarget = isExpandedTarget,
            CellWidth = DayCellWidth
        };
    }

    private IEnumerable<FahrzeugdispoAssignmentBarVm> BuildAssignmentBars(
        string dispoKey,
        IReadOnlyList<DateTime> days)
    {
        if (days.Count == 0)
        {
            yield break;
        }

        var weekStart = days[0].Date;
        var weekEnd = days[^1].Date;
        var weekStartMs = new DateTimeOffset(weekStart).ToUnixTimeMilliseconds();
        var weekEndMs = new DateTimeOffset(weekEnd.AddDays(1)).ToUnixTimeMilliseconds();

        var visibleAssignments = _assignments
            .Where(a => string.Equals(a.VehiclePhone, dispoKey, StringComparison.Ordinal))
            .Where(a => a.StartEpochMs < weekEndMs && a.EndEpochMs > weekStartMs)
            .OrderBy(a => a.StartEpochMs)
            .ToList();

        AssignOverlapLanes(visibleAssignments, out var laneById, out var laneCount);

        var weekEndExclusive = weekEnd.AddDays(1);

        var barLayouts = new List<(
            VehicleDispositionAssignment Assignment,
            DateTime VisibleStart,
            double Left,
            double Width,
            int LaneIndex,
            string Label)>();

        foreach (var assignment in visibleAssignments)
        {
            var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).LocalDateTime;
            var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).LocalDateTime;

            var visibleStart = startLocal < weekStart ? weekStart : startLocal;
            var visibleEnd = endLocal > weekEndExclusive ? weekEndExclusive : endLocal;
            if (visibleEnd <= visibleStart)
            {
                continue;
            }

            var startOffsetDays = (visibleStart - weekStart).TotalDays;
            var durationDays = (visibleEnd - visibleStart).TotalDays;
            var label = string.IsNullOrWhiteSpace(assignment.Label)
                ? FormatAssignmentRange(assignment)
                : assignment.Label;

            barLayouts.Add((
                assignment,
                visibleStart,
                startOffsetDays * DayCellWidth,
                Math.Max(durationDays * DayCellWidth, 4),
                laneById.GetValueOrDefault(assignment.Id, 0),
                label));
        }

        foreach (var (assignment, visibleStart, left, width, laneIndex, label) in barLayouts)
        {
            var hasOverlap = barLayouts.Any(other =>
                other.Assignment.Id != assignment.Id &&
                other.Assignment.StartEpochMs < assignment.EndEpochMs &&
                other.Assignment.EndEpochMs > assignment.StartEpochMs &&
                left < other.Left + other.Width &&
                left + width > other.Left);
            var (top, height) = hasOverlap
                ? GetAssignmentBarVerticalLayout(laneIndex, laneCount)
                : GetAssignmentBarVerticalLayout(0, 1);

            yield return new FahrzeugdispoAssignmentBarVm
            {
                AssignmentId = assignment.Id,
                VehiclePhoneKey = dispoKey,
                FirstVisibleDate = visibleStart.Date,
                SpanDays = Math.Max(1, (int)Math.Ceiling(width / DayCellWidth)),
                Left = left,
                Width = width,
                Top = top,
                Height = height,
                LaneIndex = laneIndex,
                Label = label,
                Tooltip = $"{label} – Linksklick für Stundenansicht, Rechtsklick zum Bearbeiten"
            };
        }
    }

    private static void AssignOverlapLanes(
        IReadOnlyList<VehicleDispositionAssignment> assignments,
        out Dictionary<string, int> laneById,
        out int laneCount)
    {
        laneById = new Dictionary<string, int>(StringComparer.Ordinal);
        laneCount = 0;
        if (assignments.Count == 0)
        {
            return;
        }

        var laneEnds = new List<long>();
        foreach (var assignment in assignments)
        {
            var lane = -1;
            for (var i = 0; i < laneEnds.Count; i++)
            {
                if (laneEnds[i] <= assignment.StartEpochMs)
                {
                    lane = i;
                    laneEnds[i] = assignment.EndEpochMs;
                    break;
                }
            }

            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(assignment.EndEpochMs);
            }

            laneById[assignment.Id] = lane;
        }

        laneCount = laneEnds.Count;
    }

    private static (double Top, double Height, bool LabelAbove) GetHourAssignmentBarVerticalLayout(
        int laneIndex,
        int laneCount)
    {
        if (laneCount <= 1)
        {
            return (0, AssignmentBarRowHeight, true);
        }

        var laneHeight = (AssignmentBarRowHeight - AssignmentBarLaneGap) / 2;
        return laneIndex == 0
            ? (0, laneHeight, true)
            : (laneHeight + AssignmentBarLaneGap, laneHeight, false);
    }

    private static (double Top, double Height) GetAssignmentBarVerticalLayout(int laneIndex, int laneCount)
    {
        if (laneCount <= 1)
        {
            return (8, AssignmentBarRowHeight - 16);
        }

        var available = AssignmentBarRowHeight - (2 * AssignmentBarVerticalMargin);
        var height = (available - ((laneCount - 1) * AssignmentBarLaneGap)) / laneCount;
        var top = AssignmentBarVerticalMargin + (laneIndex * (height + AssignmentBarLaneGap));
        return (top, height);
    }

    private static string FormatAssignmentRange(VehicleDispositionAssignment assignment)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).ToLocalTime();
        return start.Date == end.Date
            ? $"{start:dd.MM.} {start:HH:mm}–{end:HH:mm}"
            : $"{start:dd.MM. HH:mm} – {end:dd.MM. HH:mm}";
    }

    private static string BuildVehicleSubtitle(RegisteredVehicleItem v) =>
        v.PlannerDetails.VehicleType.Trim();

    private static string FormatDayHeader(DateTime d) =>
        $"{DeCulture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek)}, {d:dd.MM}";

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }
}
