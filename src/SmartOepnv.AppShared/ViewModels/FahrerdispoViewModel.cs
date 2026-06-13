using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class FahrerdispoViewModel : EditorStatusViewModelBase
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

    private readonly List<DriverDispositionAssignment> _assignments = [];

    private string? _expandedDriverKey;

    private DateTime? _expandedDate;

    private bool _flushRegistered;

    private readonly EditorAreaSyncState _sync = new();

    private string? _loadedFingerprint;

    public event Action<string?, string?>? NavigateToEmployeeManagementRequested;

    public FahrerdispoViewModel()
        : base("Kalenderschnur: Fahrer links, Tage rechts – Tag anklicken für Stundenansicht.")
    {
        ViewStartDate = GetWeekStart(DateTime.Today);
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(FlushBeforeExport);
            _flushRegistered = true;
        }
    }

    private void EnsureFlushRegistered()
    {
        if (_flushRegistered || !AppServices.IsInitialized)
        {
            return;
        }

        AppServices.RegisterFlushBeforeExport(FlushBeforeExport);
        _flushRegistered = true;
    }

    private void FlushBeforeExport()
    {
        CommitChangesIfDirty();
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    /// <summary>Beim Verlassen der Ansicht oder App-Ende: Dienste sicher auf Platte schreiben.</summary>
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

    [ObservableProperty] private ObservableCollection<FahrzeugdispoDayHeaderVm> dayHeaders = [];

    [ObservableProperty] private ObservableCollection<FahrzeugdispoVehicleRowVm> driverRows = [];

    [ObservableProperty] private int driverCount;

    [ObservableProperty] private int visibleDriverCount;

    [ObservableProperty] private bool hasExpandedRow;

    partial void OnViewStartDateChanged(DateTime value) => RebuildGrid();

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
            _assignments.AddRange(AppServices.PlannerLocal.LoadDriverDisposition());
        }

        var editor = AppServices.Routes.Editor;
        DriverCount = editor?.Employees.Count ?? 0;
        if (editor is null)
        {
            DriverRows.Clear();
            DayHeaders.Clear();
            VisibleDriverCount = 0;
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            _sync.AfterRefresh();
            _loadedFingerprint = ComputeFingerprint();
            return;
        }

        _sync.AfterRefresh();
        _loadedFingerprint = ComputeFingerprint();
        ScheduleRebuildGrid();
    }

    [RelayCommand]
    private void PreviousWeek() => ViewStartDate = ViewStartDate.AddDays(-VisibleDayCount);

    [RelayCommand]
    private void NextWeek() => ViewStartDate = ViewStartDate.AddDays(VisibleDayCount);

    [RelayCommand]
    private void GoToToday() => ViewStartDate = GetWeekStart(DateTime.Today);

    [RelayCommand]
    private void AddShift()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        if (editor.Employees.Count == 0)
        {
            StatusMessage = "Keine Fahrer – bitte zuerst unter Personalverwaltung anlegen.";
            return;
        }

        if (!TryShowShiftDialog(editor.Employees.ToList(), DateTime.Today, existing: null, out var result))
        {
            return;
        }

        if (result.DeleteRequested)
        {
            return;
        }

        _assignments.Add(new DriverDispositionAssignment
        {
            DriverKey = result.DriverKey,
            StartEpochMs = result.StartEpochMs,
            EndEpochMs = result.EndEpochMs,
            Part1EndEpochMs = result.Part1EndEpochMs,
            Part2StartEpochMs = result.Part2StartEpochMs,
            Label = result.DisplayLabel,
            ReducedRestBefore = result.ReducedRestBefore,
            ExtendedDrivingDay = result.ExtendedDrivingDay,
            ReducedWeeklyRestBefore = result.ReducedWeeklyRestBefore
        });

        _sync.MarkDirty();
        SaveAndRefresh(result.StartEpochMs, "Dienst gespeichert.");
    }

    public void EditShift(string assignmentId)
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

        if (!TryShowShiftDialog(editor.Employees.ToList(), ViewStartDate, existing, out var result))
        {
            return;
        }

        if (result.DeleteRequested)
        {
            _assignments.RemoveAll(a => a.Id == assignmentId);
            _sync.MarkDirty();
            SaveAndRefresh(null, "Dienst gelöscht.");
            return;
        }

        existing.DriverKey = result.DriverKey;
        existing.StartEpochMs = result.StartEpochMs;
        existing.EndEpochMs = result.EndEpochMs;
        existing.Part1EndEpochMs = result.Part1EndEpochMs;
        existing.Part2StartEpochMs = result.Part2StartEpochMs;
        existing.Label = result.DisplayLabel;
        existing.ReducedRestBefore = result.ReducedRestBefore;
        existing.ExtendedDrivingDay = result.ExtendedDrivingDay;
        existing.ReducedWeeklyRestBefore = result.ReducedWeeklyRestBefore;
        _sync.MarkDirty();
        SaveAndRefresh(result.StartEpochMs, "Dienst aktualisiert.");
    }

    public void OpenEmployeeManagement(string driverKey, string? personnelNumber)
    {
        var personnel = !string.IsNullOrWhiteSpace(personnelNumber)
            ? personnelNumber
            : EmployeeDispoKeys.TryGetPersonnelDigits(driverKey);
        NavigateToEmployeeManagementRequested?.Invoke(personnel, driverKey);
    }

    [RelayCommand]
    private void ToggleDayCell(FahrzeugdispoTimeCellVm? cell)
    {
        if (cell is null || cell.IsCollapseCell || cell.Date is null)
        {
            return;
        }

        if (_expandedDriverKey == cell.VehiclePhoneKey && _expandedDate == cell.Date.Value.Date)
        {
            CollapseHourView();
            return;
        }

        OpenHourView(cell.VehiclePhoneKey, cell.Date.Value);
    }

    public void OpenHourViewFromAssignmentBar(string driverKey, DateTime date) =>
        OpenHourView(driverKey, date);

    private void OpenHourView(string driverKey, DateTime date)
    {
        _expandedDriverKey = driverKey;
        _expandedDate = date.Date;
        RebuildGrid();
        StatusMessage =
            $"Stundenansicht für {date:dd.MM.yyyy} – „Wochenansicht“ oder ◀ zum Schließen.";
    }

    [RelayCommand]
    private void CollapseHourView()
    {
        if (_expandedDriverKey is null && _expandedDate is null)
        {
            return;
        }

        _expandedDriverKey = null;
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

        var employees = editor.Employees
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PersonnelNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var days = BuildVisibleDays();
        var assignmentsByDriver = IndexAssignmentsByDriver();

        VisibleDriverCount = employees.Count;
        HasExpandedRow = _expandedDriverKey is not null && _expandedDate is not null;

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
        foreach (var employee in employees)
        {
            var dispoKey = EmployeeDispoKeys.FromEmployee(employee);
            var row = new FahrzeugdispoVehicleRowVm
            {
                Title = string.IsNullOrWhiteSpace(employee.Name) ? employee.PhoneNumber : employee.Name,
                Subtitle = BuildEmployeeSubtitle(employee),
                RowColorHex = string.Empty,
                PhoneKey = dispoKey,
                PersonnelNumber = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber),
                IsHourMode = MatchesExpandedHourView(employee),
                HourModeDateLabel = _expandedDate?.ToString("dd.MM.yyyy", DeCulture) ?? string.Empty
            };

            if (row.IsHourMode && _expandedDate is not null)
            {
                row.Cells.Add(CreateCollapseCell(dispoKey, _expandedDate.Value));
                for (var hour = 0; hour < 24; hour++)
                {
                    row.Cells.Add(CreateHourCell(dispoKey, _expandedDate.Value, hour));
                }

                foreach (var bar in BuildHourAssignmentBars(employee, _expandedDate.Value, assignmentsByDriver))
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

                foreach (var bar in BuildAssignmentBars(employee, days, assignmentsByDriver))
                {
                    row.AssignmentBars.Add(bar);
                }
            }

            rows.Add(row);
        }

        DriverRows = rows;
        StatusMessage =
            $"{VisibleDriverCount} von {DriverCount} Fahrern – " +
            $"{ViewStartDate:dd.MM.yyyy} bis {days[^1]:dd.MM.yyyy}. " +
            "„Neuer Dienst“ zum Eintragen, Rechtsklick auf Balken – FPersV: Ruhe-, Wochenruhe- und Lenkzeiten.";
    }

    private void SaveAndRefresh(long? focusStartEpochMs, string message)
    {
        if (focusStartEpochMs is not null)
        {
            EnsureWeekVisible(focusStartEpochMs.Value);
        }

        if (PersistAssignments())
        {
            _sync.AfterCommit();
            _loadedFingerprint = ComputeFingerprint();
            ReportSaveSuccess(message);
        }

        ScheduleRebuildGrid();
    }

    private void ScheduleRebuildGrid()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RebuildGrid();
            return;
        }

        dispatcher.BeginInvoke(RebuildGrid, DispatcherPriority.Background);
    }

    private bool TryShowShiftDialog(
        IReadOnlyList<EmployeeRosterItem> employees,
        DateTime defaultDate,
        DriverDispositionAssignment? existing,
        out FahrerdispoShiftDialogResult result)
    {
        result = default;
        var dialog = new FahrerdispoNewShiftDialog(employees, defaultDate, existing, _assignments)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        result = new FahrerdispoShiftDialogResult
        {
            DeleteRequested = dialog.DeleteRequested,
            DriverKey = dialog.SelectedDriverKey,
            StartEpochMs = dialog.StartEpochMs,
            EndEpochMs = dialog.EndEpochMs,
            Part1EndEpochMs = dialog.Part1EndEpochMs,
            Part2StartEpochMs = dialog.Part2StartEpochMs,
            DisplayLabel = dialog.ShiftName,
            ReducedRestBefore = dialog.ReducedRestBefore,
            ExtendedDrivingDay = dialog.ExtendedDrivingDay,
            ReducedWeeklyRestBefore = dialog.ReducedWeeklyRestBefore
        };
        return true;
    }

    private string ComputeFingerprint() =>
        JsonSerializer.Serialize(_assignments
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new
            {
                a.Id,
                a.DriverKey,
                a.StartEpochMs,
                a.EndEpochMs,
                a.Part1EndEpochMs,
                a.Part2StartEpochMs,
                a.Label,
                a.ReducedRestBefore,
                a.ExtendedDrivingDay,
                a.ReducedWeeklyRestBefore
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
            AppServices.PlannerLocal.SaveDriverDisposition(_assignments);
            return true;
        }
        catch (Exception ex)
        {
            ReportSaveError($"Speichern fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    private sealed class FahrerdispoShiftDialogResult
    {
        public bool DeleteRequested { get; init; }

        public string DriverKey { get; init; } = string.Empty;

        public long StartEpochMs { get; init; }

        public long EndEpochMs { get; init; }

        public long Part1EndEpochMs { get; init; }

        public long Part2StartEpochMs { get; init; }

        public string DisplayLabel { get; init; } = string.Empty;

        public bool ReducedRestBefore { get; init; }

        public bool ExtendedDrivingDay { get; init; }

        public bool ReducedWeeklyRestBefore { get; init; }
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

    private IReadOnlyList<DateTime> BuildVisibleDays()
    {
        return Enumerable.Range(0, VisibleDayCount)
            .Select(i => ViewStartDate.Date.AddDays(i))
            .ToList();
    }

    private Dictionary<string, List<DriverDispositionAssignment>> IndexAssignmentsByDriver()
    {
        return _assignments
            .GroupBy(a => a.DriverKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(a => a.StartEpochMs).ToList(),
                StringComparer.Ordinal);
    }

    private static List<DriverDispositionAssignment> GetDriverAssignments(
        Dictionary<string, List<DriverDispositionAssignment>> byDriver,
        string driverKey) =>
        byDriver.TryGetValue(driverKey, out var list) ? list : [];

    private bool MatchesExpandedHourView(EmployeeRosterItem employee) =>
        _expandedDate is not null &&
        _expandedDriverKey is not null &&
        EmployeeDispoKeys.KeysMatch(_expandedDriverKey, employee);

    private IEnumerable<FahrzeugdispoAssignmentBarVm> BuildHourAssignmentBars(
        EmployeeRosterItem employee,
        DateTime date,
        Dictionary<string, List<DriverDispositionAssignment>> assignmentsByDriver)
    {
        var dispoKey = EmployeeDispoKeys.FromEmployee(employee);
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var dayStartMs = new DateTimeOffset(dayStart).ToUnixTimeMilliseconds();
        var dayEndMs = new DateTimeOffset(dayEnd).ToUnixTimeMilliseconds();

        var visibleAssignments = GetDriverAssignments(assignmentsByDriver, dispoKey)
            .Where(a => a.StartEpochMs < dayEndMs && a.EndEpochMs > dayStartMs)
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
            var shiftName = string.IsNullOrWhiteSpace(assignment.Label) ? string.Empty : assignment.Label;

            var laneIndex = laneById.GetValueOrDefault(assignment.Id, 0);
            var hasOverlap = visibleAssignments.Any(other =>
                other.Id != assignment.Id &&
                other.StartEpochMs < assignment.EndEpochMs &&
                other.EndEpochMs > assignment.StartEpochMs);
            var (top, height, labelAbove) = GetHourAssignmentBarVerticalLayout(
                laneIndex,
                hasOverlap ? laneCount : 1);

            var visibleStartMs = new DateTimeOffset(visibleStart).ToUnixTimeMilliseconds();
            var visibleEndMs = new DateTimeOffset(visibleEnd).ToUnixTimeMilliseconds();
            var barWidth = Math.Max(durationHours * HourCellWidth, 4);
            var (work1Ratio, gapRatio, work2Ratio, work1Width, gapWidth, work2Width) =
                ComputeSplitBarLayout(assignment, visibleStartMs, visibleEndMs, barWidth);

            yield return new FahrzeugdispoAssignmentBarVm
            {
                AssignmentId = assignment.Id,
                VehiclePhoneKey = dispoKey,
                FirstVisibleDate = dayStart,
                SpanDays = 1,
                Left = HourCollapseCellWidth + (startOffsetHours * HourCellWidth),
                Width = barWidth,
                Top = top,
                Height = height,
                LaneIndex = laneIndex,
                IsHourViewBar = true,
                TimeLabelAboveBar = labelAbove,
                TimeLabel = timeLabel,
                Label = shiftName,
                IsSplitShiftBar = assignment.IsSplitShift && gapRatio > 0,
                Work1Ratio = work1Ratio,
                GapRatio = gapRatio,
                Work2Ratio = work2Ratio,
                Work1Width = work1Width,
                GapWidth = gapWidth,
                Work2Width = work2Width,
                Tooltip = string.IsNullOrWhiteSpace(shiftName)
                    ? $"{timeLabel} – Rechtsklick zum Bearbeiten"
                    : $"{shiftName} ({timeLabel}) – Rechtsklick zum Bearbeiten"
            };
        }
    }

    private FahrzeugdispoTimeCellVm CreateDayCell(string phoneKey, DateTime date)
    {
        var isExpandedTarget = _expandedDriverKey == phoneKey && _expandedDate == date.Date;
        return new FahrzeugdispoTimeCellVm
        {
            VehiclePhoneKey = phoneKey,
            Date = date,
            IsExpandedTarget = isExpandedTarget,
            CellWidth = DayCellWidth
        };
    }

    private IEnumerable<FahrzeugdispoAssignmentBarVm> BuildAssignmentBars(
        EmployeeRosterItem employee,
        IReadOnlyList<DateTime> days,
        Dictionary<string, List<DriverDispositionAssignment>> assignmentsByDriver)
    {
        if (days.Count == 0)
        {
            yield break;
        }

        var dispoKey = EmployeeDispoKeys.FromEmployee(employee);
        var weekStart = days[0].Date;
        var weekEnd = days[^1].Date;
        var weekStartMs = new DateTimeOffset(weekStart).ToUnixTimeMilliseconds();
        var weekEndMs = new DateTimeOffset(weekEnd.AddDays(1)).ToUnixTimeMilliseconds();

        var visibleAssignments = GetDriverAssignments(assignmentsByDriver, dispoKey)
            .Where(a => a.StartEpochMs < weekEndMs && a.EndEpochMs > weekStartMs)
            .ToList();

        AssignOverlapLanes(visibleAssignments, out var laneById, out var laneCount);

        var weekEndExclusive = weekEnd.AddDays(1);

        var barLayouts = new List<(
            DriverDispositionAssignment Assignment,
            DateTime VisibleStart,
            DateTime VisibleEnd,
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
                visibleEnd,
                startOffsetDays * DayCellWidth,
                Math.Max(durationDays * DayCellWidth, 4),
                laneById.GetValueOrDefault(assignment.Id, 0),
                label));
        }

        foreach (var (assignment, visibleStart, visibleEnd, left, width, laneIndex, label) in barLayouts)
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

            var barWidth = width;
            var visibleStartMs = new DateTimeOffset(visibleStart).ToUnixTimeMilliseconds();
            var visibleEndMs = new DateTimeOffset(visibleEnd).ToUnixTimeMilliseconds();
            var (work1Ratio, gapRatio, work2Ratio, work1Width, gapWidth, work2Width) =
                ComputeSplitBarLayout(assignment, visibleStartMs, visibleEndMs, barWidth);

            yield return new FahrzeugdispoAssignmentBarVm
            {
                AssignmentId = assignment.Id,
                VehiclePhoneKey = dispoKey,
                FirstVisibleDate = visibleStart.Date,
                SpanDays = Math.Max(1, (int)Math.Ceiling(barWidth / DayCellWidth)),
                Left = left,
                Width = barWidth,
                Top = top,
                Height = height,
                LaneIndex = laneIndex,
                Label = label,
                IsSplitShiftBar = assignment.IsSplitShift && gapRatio > 0,
                Work1Ratio = work1Ratio,
                GapRatio = gapRatio,
                Work2Ratio = work2Ratio,
                Work1Width = work1Width,
                GapWidth = gapWidth,
                Work2Width = work2Width,
                Tooltip = $"{label} – Linksklick für Stundenansicht, Rechtsklick zum Bearbeiten"
            };
        }
    }

    private static void AssignOverlapLanes(
        IReadOnlyList<DriverDispositionAssignment> assignments,
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

    private static string FormatAssignmentRange(DriverDispositionAssignment assignment)
    {
        if (assignment.IsSplitShift)
        {
            var p1Start = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).ToLocalTime();
            var p1End = DateTimeOffset.FromUnixTimeMilliseconds(assignment.Part1EndEpochMs).ToLocalTime();
            var p2Start = DateTimeOffset.FromUnixTimeMilliseconds(assignment.Part2StartEpochMs).ToLocalTime();
            var p2End = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).ToLocalTime();
            return $"{p1Start:HH:mm}–{p1End:HH:mm} · {p2Start:HH:mm}–{p2End:HH:mm} (geteilt)";
        }

        var start = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).ToLocalTime();
        return start.Date == end.Date
            ? $"{start:dd.MM.} {start:HH:mm}–{end:HH:mm}"
            : $"{start:dd.MM. HH:mm} – {end:dd.MM. HH:mm}";
    }

    private static (double Work1Ratio, double GapRatio, double Work2Ratio, double Work1Width, double GapWidth, double Work2Width)
        ComputeSplitBarLayout(DriverDispositionAssignment assignment, long visibleStartMs, long visibleEndMs, double totalWidth)
    {
        var (work1Ratio, gapRatio, work2Ratio) = ComputeSplitBarRatios(assignment, visibleStartMs, visibleEndMs);
        if (!assignment.IsSplitShift || gapRatio <= 0)
        {
            return (1, 0, 0, totalWidth, 0, 0);
        }

        return (
            work1Ratio,
            gapRatio,
            work2Ratio,
            totalWidth * work1Ratio,
            totalWidth * gapRatio,
            totalWidth * work2Ratio);
    }

    private static (double Work1Ratio, double GapRatio, double Work2Ratio) ComputeSplitBarRatios(
        DriverDispositionAssignment assignment,
        long visibleStartMs,
        long visibleEndMs)
    {
        if (!assignment.IsSplitShift || visibleEndMs <= visibleStartMs)
        {
            return (1, 0, 0);
        }

        var totalMs = visibleEndMs - visibleStartMs;
        var work1Start = Math.Max(assignment.StartEpochMs, visibleStartMs);
        var work1End = Math.Min(assignment.Part1EndEpochMs, visibleEndMs);
        var gapStart = Math.Max(assignment.Part1EndEpochMs, visibleStartMs);
        var gapEnd = Math.Min(assignment.Part2StartEpochMs, visibleEndMs);
        var work2Start = Math.Max(assignment.Part2StartEpochMs, visibleStartMs);
        var work2End = Math.Min(assignment.EndEpochMs, visibleEndMs);

        var work1Ms = Math.Max(0, work1End - work1Start);
        var gapMs = Math.Max(0, gapEnd - gapStart);
        var work2Ms = Math.Max(0, work2End - work2Start);
        var sum = work1Ms + gapMs + work2Ms;
        if (sum <= 0)
        {
            return (1, 0, 0);
        }

        return (work1Ms / (double)sum, gapMs / (double)sum, work2Ms / (double)sum);
    }

    private static string BuildEmployeeSubtitle(EmployeeRosterItem employee)
    {
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        return personnel.Length > 0 ? $"PN {personnel}" : employee.PhoneNumber.Trim();
    }

    private static string FormatDayHeader(DateTime d) =>
        $"{DeCulture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek)}, {d:dd.MM}";

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }
}
