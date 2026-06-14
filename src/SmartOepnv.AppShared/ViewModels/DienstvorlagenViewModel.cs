using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Dienstvorlagen;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DutyTemplateRowItem : ObservableObject
{
    [ObservableProperty] private string tripNumber = string.Empty;

    [ObservableProperty] private string lineCourse = string.Empty;

    [ObservableProperty] private string remark = string.Empty;

    [ObservableProperty] private string destination = string.Empty;

    [ObservableProperty] private string fromTime = string.Empty;

    [ObservableProperty] private string fromStop = string.Empty;

    [ObservableProperty] private string toTime = string.Empty;

    [ObservableProperty] private string toStop = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public static DutyTemplateRowItem FromModel(DutyTemplateRow row) => new()
    {
        Id = row.Id,
        TripNumber = row.TripNumber,
        LineCourse = row.LineCourse,
        Remark = row.Remark,
        Destination = row.Destination,
        FromTime = row.FromTime,
        FromStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(row.FromStop),
        ToTime = row.ToTime,
        ToStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(row.ToStop)
    };

    public DutyTemplateRow ToModel() => new()
    {
        Id = Id,
        TripNumber = TripNumber.Trim(),
        LineCourse = LineCourse.Trim(),
        Remark = Remark.Trim(),
        Destination = Destination.Trim(),
        FromTime = FromTime.Trim(),
        FromStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(FromStop),
        ToTime = ToTime.Trim(),
        ToStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(ToStop)
    };
}

public partial class DutyTemplateImportItem(DutyTemplateImportRow source) : ObservableObject
{
    public DutyTemplateImportRow Source { get; } = source;

    public int SourceLineNumber => Source.SourceLineNumber;

    public string Preview => Source.Preview;

    public string RawLine => Source.RawLine;

    [ObservableProperty] private bool isSelected = true;
}

public partial class OperatingDayOptionItem(DutyOperatingDay day, string label) : ObservableObject
{
    public DutyOperatingDay Day { get; } = day;

    public string Label { get; } = label;

    [ObservableProperty] private bool isSelected;
}

public sealed class CompanyLogoOption
{
    public CompanyLogoOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
}

public partial class DienstvorlagenViewModel : EditorStatusViewModelBase
{
    private string? _loadedTemplateId;
    private bool _suppressSessionSave;
    private bool _suppressOperatingDaySync;
    private DispatcherTimer? _sessionSaveTimer;
    private int _activeDutyPart = 1;
    private DutyTemplateRowItem? _lastPart1Row;
    private DutyTemplateRowItem? _lastPart2Row;
    private DutyTemplateRowItem? _lastPart3Row;
    private List<DutyTemplateRowItem> _activeGridSelection = [];

    public static IReadOnlyList<string> ContractorSuggestions { get; } =
    [
        "Regiobahn",
        "nationalExpress"
    ];

    public DienstvorlagenViewModel() : base("Dienstvorlagen erstellen, aus Fahrplan importieren und als PDF exportieren.")
    {
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var item = new OperatingDayOptionItem(day, name);
            AttachOperatingDayHandler(item);
            OperatingDaySelections.Add(item);
        }

        AppServices.RegisterFlushBeforeExport(FlushEditorSessionNow);
        ReloadCompanyLogos();
        ReloadTemplateList();
        if (!TryRestoreEditorSession())
        {
            RefreshStats();
        }
    }

    [ObservableProperty] private string templateName = string.Empty;

    [ObservableProperty] private string dutyNumber = string.Empty;

    [ObservableProperty] private string dutyNumberPart2 = string.Empty;

    [ObservableProperty] private string dutyNumberPart3 = string.Empty;

    [ObservableProperty] private string contractor = string.Empty;

    [ObservableProperty] private string operatingDay = string.Empty;

    [ObservableProperty] private string vehicleNumber = string.Empty;

    [ObservableProperty] private string defaultLineCourse = string.Empty;

    [ObservableProperty] private string importedLine = string.Empty;

    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty] private bool subtractUnpaidBreak30Minutes;

    [ObservableProperty] private bool subtractUnpaidBreak30MinutesPart2;

    [ObservableProperty] private bool subtractUnpaidBreak30MinutesPart3;

    [ObservableProperty] private string customUnpaidBreakDeductionMinutes = string.Empty;

    [ObservableProperty] private string workPreparationMinutes = DutyTemplateCalculator.DefaultWorkPreparationMinutes.ToString();

    [ObservableProperty] private string workFollowUpMinutes = DutyTemplateCalculator.DefaultWorkFollowUpMinutes.ToString();

    [ObservableProperty] private DutyTemplate? selectedTemplate;

    [ObservableProperty] private DutyTemplateRowItem? selectedRow;

    [ObservableProperty] private DutyTemplateRowItem? selectedPart2Row;

    [ObservableProperty] private DutyTemplateRowItem? selectedPart3Row;

    [ObservableProperty] private string serviceDurationDisplay = "0:00";

    [ObservableProperty] private string serviceStartDisplay = "–";

    [ObservableProperty] private string serviceEndDisplay = "–";

    [ObservableProperty] private string payHoursDisplay = "0:00";

    [ObservableProperty] private string breaksDisplay = "–";

    [ObservableProperty] private string pureDrivingDisplay = "0:00";

    [ObservableProperty] private string pureBreakDisplay = "–";

    [ObservableProperty] private string part1ServiceDurationDisplay = "0:00";

    [ObservableProperty] private string part1PayHoursDisplay = "0:00";

    [ObservableProperty] private string part1BreaksDisplay = "–";

    [ObservableProperty] private string part1PureDrivingDisplay = "0:00";

    [ObservableProperty] private string part1PureBreakDisplay = "–";

    [ObservableProperty] private string part2ServiceDurationDisplay = "0:00";

    [ObservableProperty] private string part2PayHoursDisplay = "0:00";

    [ObservableProperty] private string part2BreaksDisplay = "–";

    [ObservableProperty] private string part2PureDrivingDisplay = "0:00";

    [ObservableProperty] private string part2PureBreakDisplay = "–";

    [ObservableProperty] private bool part1ExceedsMaxDuration;

    [ObservableProperty] private bool part2ExceedsMaxDuration;

    [ObservableProperty] private string part3ServiceDurationDisplay = "0:00";

    [ObservableProperty] private string part3PayHoursDisplay = "0:00";

    [ObservableProperty] private string part3BreaksDisplay = "–";

    [ObservableProperty] private string part3PureDrivingDisplay = "0:00";

    [ObservableProperty] private string part3PureBreakDisplay = "–";

    [ObservableProperty] private bool part3ExceedsMaxDuration;

    [ObservableProperty] private string importFileName = string.Empty;

    [ObservableProperty] private string selectedCompanyLogoId = string.Empty;

    public ObservableCollection<DutyTemplate> SavedTemplates { get; } = [];

    public ObservableCollection<CompanyLogoOption> CompanyLogoOptions { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Rows { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Part2Rows { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Part3Rows { get; } = [];

    public bool IsSplitDuty => Part2Rows.Count > 0 || Part3Rows.Count > 0;

    public bool IsThreePartDuty => Part3Rows.Count > 0;

    public string ExportPart1ButtonLabel => IsSplitDuty
        ? "Dienstvorlage erstellen (Teil 1)"
        : "Dienstvorlage erstellen";

    public ObservableCollection<DutyTemplateImportItem> ImportRows { get; } = [];

    public ObservableCollection<OperatingDayOptionItem> OperatingDaySelections { get; } = [];

    public bool HasOperatingDayDisplay => !string.IsNullOrWhiteSpace(OperatingDay);

    public bool HasSavedTemplates => SavedTemplates.Count > 0;

    public bool HasImportRows => ImportRows.Count > 0;

    public bool HasRemarkLegend => RemarkLegend.Count > 0;

    public bool HasImportedLine => !string.IsNullOrWhiteSpace(ImportedLine);

    public bool HasCompanyLogos => CompanyLogoOptions.Count > 0;

    public IReadOnlyList<DutyTemplateRemarkEntry> RemarkLegend =>
        DutyTemplateRemarkHelper.BuildLegend(AllRowModels());

    private IEnumerable<DutyTemplateRow> AllRowModels() =>
        Rows.Select(row => row.ToModel())
            .Concat(Part2Rows.Select(row => row.ToModel()))
            .Concat(Part3Rows.Select(row => row.ToModel()));

    public bool CanUpdateLoadedTemplate => !string.IsNullOrEmpty(_loadedTemplateId);

    private bool _suppressLineCourseApply;

    partial void OnDefaultLineCourseChanged(string value)
    {
        if (_suppressLineCourseApply)
        {
            return;
        }

        var formatted = RouteDisplayHelper.FormatLineCourseInput(value);
        if (!string.Equals(formatted, value, StringComparison.Ordinal))
        {
            _suppressLineCourseApply = true;
            try
            {
                DefaultLineCourse = formatted;
            }
            finally
            {
                _suppressLineCourseApply = false;
            }
        }

        ApplyDefaultLineCourseToRows();
        ScheduleEditorSessionSave();
    }

    partial void OnTemplateNameChanged(string value)
    {
        SaveTemplateCommand.NotifyCanExecuteChanged();

        if (_loadedTemplateId is null)
        {
            ScheduleEditorSessionSave();
            return;
        }

        var loaded = GetLoadedTemplate();
        if (loaded is not null && !string.Equals(loaded.Name, value.Trim(), StringComparison.Ordinal))
        {
            _loadedTemplateId = null;
            OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
            UpdateLoadedTemplateCommand.NotifyCanExecuteChanged();
        }

        ScheduleEditorSessionSave();
    }

    partial void OnDutyNumberChanged(string value) => ScheduleEditorSessionSave();

    partial void OnDutyNumberPart2Changed(string value) => ScheduleEditorSessionSave();

    partial void OnDutyNumberPart3Changed(string value) => ScheduleEditorSessionSave();

    partial void OnContractorChanged(string value) => ScheduleEditorSessionSave();

    partial void OnOperatingDayChanged(string value)
    {
        OnPropertyChanged(nameof(HasOperatingDayDisplay));
        ScheduleEditorSessionSave();
    }

    partial void OnVehicleNumberChanged(string value) => ScheduleEditorSessionSave();

    partial void OnImportedLineChanged(string value) => ScheduleEditorSessionSave();

    partial void OnNotesChanged(string value) => ScheduleEditorSessionSave();

    partial void OnSelectedCompanyLogoIdChanged(string value) => ScheduleEditorSessionSave();

    partial void OnSubtractUnpaidBreak30MinutesChanged(bool value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    partial void OnSubtractUnpaidBreak30MinutesPart2Changed(bool value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    partial void OnSubtractUnpaidBreak30MinutesPart3Changed(bool value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    partial void OnCustomUnpaidBreakDeductionMinutesChanged(string value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    partial void OnWorkPreparationMinutesChanged(string value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    partial void OnWorkFollowUpMinutesChanged(string value)
    {
        RefreshStats();
        ScheduleEditorSessionSave();
    }

    public void RefreshFromEditor()
    {
        ReloadCompanyLogos();
        ReloadTemplateList();
    }

    public void SetActiveDutyPart(int part)
    {
        if (part is 1 or 2 or 3)
        {
            _activeDutyPart = part;
        }
    }

    public void UpdateActiveGridSelection(IReadOnlyList<DutyTemplateRowItem> selectedRows)
    {
        _activeGridSelection = selectedRows.ToList();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ReloadCompanyLogos()
    {
        CompanyLogoOptions.Clear();
        if (!AppServices.IsPlannerApp)
        {
            SelectedCompanyLogoId = string.Empty;
            OnPropertyChanged(nameof(HasCompanyLogos));
            return;
        }

        foreach (var logo in PlanerBrandingWorkspace.GetLogos(AppServices.SettingsSubfolder))
        {
            CompanyLogoOptions.Add(new CompanyLogoOption(logo.Id, logo.Name));
        }

        if (!string.IsNullOrWhiteSpace(SelectedCompanyLogoId) &&
            CompanyLogoOptions.Any(option => option.Id == SelectedCompanyLogoId))
        {
            OnPropertyChanged(nameof(HasCompanyLogos));
            return;
        }

        SelectedCompanyLogoId = CompanyLogoOptions.FirstOrDefault()?.Id ?? string.Empty;
        OnPropertyChanged(nameof(HasCompanyLogos));
    }

    [RelayCommand]
    private void NewTemplate()
    {
        ResetEditorFields();
        RefreshStats();
        StatusMessage = "Neue Dienstvorlage – Abschnitte manuell hinzufügen oder aus Fahrplan importieren.";
    }

    private void ResetEditorFields()
    {
        _loadedTemplateId = null;
        TemplateName = string.Empty;
        DutyNumber = string.Empty;
        DutyNumberPart2 = string.Empty;
        DutyNumberPart3 = string.Empty;
        Contractor = string.Empty;
        ApplyOperatingDayToSelection(string.Empty);
        OperatingDay = string.Empty;
        VehicleNumber = string.Empty;
        DefaultLineCourse = string.Empty;
        ImportedLine = string.Empty;
        Notes = string.Empty;
        SubtractUnpaidBreak30Minutes = false;
        SubtractUnpaidBreak30MinutesPart2 = false;
        SubtractUnpaidBreak30MinutesPart3 = false;
        CustomUnpaidBreakDeductionMinutes = string.Empty;
        WorkPreparationMinutes = DutyTemplateCalculator.DefaultWorkPreparationMinutes.ToString();
        WorkFollowUpMinutes = DutyTemplateCalculator.DefaultWorkFollowUpMinutes.ToString();
        Rows.Clear();
        Part2Rows.Clear();
        Part3Rows.Clear();
        SelectedRow = null;
        SelectedPart2Row = null;
        SelectedPart3Row = null;
        _activeGridSelection.Clear();
        SelectedTemplate = null;
        ImportRows.Clear();
        ImportFileName = string.Empty;
        SelectedCompanyLogoId = CompanyLogoOptions.FirstOrDefault()?.Id ?? string.Empty;
        ClearEditorSession();
        OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
        OnPropertyChanged(nameof(HasImportedLine));
        OnPropertyChanged(nameof(HasImportRows));
    }

    [RelayCommand]
    private void AddEmptyTripRow()
    {
        var row = new DutyTemplateRowItem
        {
            Remark = $"{DutyTemplateRemarkHelper.GetNextCode(AllRowModels().Select(item => item.Remark))}=Leerfahrt",
            LineCourse = DefaultLineCourse.Trim()
        };
        InsertRow(row);
        StatusMessage = "Leerfahrt eingefügt – Zeiten und Haltestellen eintragen.";
    }

    [RelayCommand]
    private void AddEmptyLineRow()
    {
        var row = new DutyTemplateRowItem
        {
            Remark = $"{DutyTemplateRemarkHelper.GetNextCode(AllRowModels().Select(item => item.Remark))}=Leerzeile",
            LineCourse = DefaultLineCourse.Trim()
        };
        InsertRow(row);
        StatusMessage = "Leerzeile eingefügt – bei Bedarf Zeiten/Haltestellen ergänzen.";
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new DutyTemplateRowItem
        {
            LineCourse = DefaultLineCourse.Trim()
        };
        InsertRow(row);
        StatusMessage = "Neue Fahrt-Zeile eingefügt.";
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedRowUp))]
    private void MoveSelectedRowUp()
    {
        if (!TryGetSelectedRowContext(out var collection, out var index))
        {
            StatusMessage = "Bitte zuerst eine Zeile auswählen.";
            return;
        }

        if (index <= 0)
        {
            return;
        }

        collection.Move(index, index - 1);
        RefreshStats();
        StatusMessage = "Zeile nach oben verschoben.";
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedRowDown))]
    private void MoveSelectedRowDown()
    {
        if (!TryGetSelectedRowContext(out var collection, out var index))
        {
            StatusMessage = "Bitte zuerst eine Zeile auswählen.";
            return;
        }

        if (index >= collection.Count - 1)
        {
            return;
        }

        collection.Move(index, index + 1);
        RefreshStats();
        StatusMessage = "Zeile nach unten verschoben.";
    }

    private bool CanMoveSelectedRowUp() =>
        TryGetSelectedRowContext(out var collection, out var index) && index > 0;

    private bool CanMoveSelectedRowDown() =>
        TryGetSelectedRowContext(out var collection, out var index) && index >= 0 && index < collection.Count - 1;

    private void InsertRow(DutyTemplateRowItem row)
    {
        AttachRowHandler(row);

        if (TryGetSelectedRowContext(out var collection, out var index))
        {
            collection.Insert(index + 1, row);
            if (ReferenceEquals(collection, Part3Rows))
            {
                SelectedPart3Row = row;
            }
            else if (ReferenceEquals(collection, Part2Rows))
            {
                SelectedPart2Row = row;
            }
            else
            {
                SelectedRow = row;
            }
        }
        else if (IsSplitDuty && _activeDutyPart == 3)
        {
            Part3Rows.Add(row);
            SelectedPart3Row = row;
        }
        else if (IsSplitDuty && _activeDutyPart == 2)
        {
            Part2Rows.Add(row);
            SelectedPart2Row = row;
        }
        else
        {
            Rows.Add(row);
            SelectedRow = row;
        }

        RefreshStats();
    }

    private bool TryGetSelectedRowContext(
        out ObservableCollection<DutyTemplateRowItem> collection,
        out int index)
    {
        if (SelectedPart3Row is not null)
        {
            collection = Part3Rows;
            index = Part3Rows.IndexOf(SelectedPart3Row);
            return index >= 0;
        }

        if (SelectedPart2Row is not null)
        {
            collection = Part2Rows;
            index = Part2Rows.IndexOf(SelectedPart2Row);
            return index >= 0;
        }

        if (SelectedRow is not null)
        {
            collection = Rows;
            index = Rows.IndexOf(SelectedRow);
            return index >= 0;
        }

        collection = Rows;
        index = -1;
        return false;
    }

    [RelayCommand]
    private void RemoveSelectedRow()
    {
        if (!TryResolveRowToRemove(out var collection, out var row))
        {
            StatusMessage = "Bitte zuerst eine Fahrt auswählen.";
            return;
        }

        collection.Remove(row);

        if (ReferenceEquals(collection, Part3Rows))
        {
            SelectedPart3Row = null;
            _lastPart3Row = null;
            if (Part3Rows.Count == 0)
            {
                DutyNumberPart3 = string.Empty;
            }
        }
        else if (ReferenceEquals(collection, Part2Rows))
        {
            SelectedPart2Row = null;
            _lastPart2Row = null;
            if (Part2Rows.Count == 0)
            {
                DutyNumberPart2 = string.Empty;
            }
        }
        else
        {
            SelectedRow = null;
            _lastPart1Row = null;
        }

        RefreshStats();
        StatusMessage = "Zeile entfernt.";
    }

    private bool TryResolveRowToRemove(
        out ObservableCollection<DutyTemplateRowItem> collection,
        out DutyTemplateRowItem row)
    {
        if (SelectedPart3Row is not null && Part3Rows.Contains(SelectedPart3Row))
        {
            collection = Part3Rows;
            row = SelectedPart3Row;
            return true;
        }

        if (SelectedPart2Row is not null && Part2Rows.Contains(SelectedPart2Row))
        {
            collection = Part2Rows;
            row = SelectedPart2Row;
            return true;
        }

        if (SelectedRow is not null && Rows.Contains(SelectedRow))
        {
            collection = Rows;
            row = SelectedRow;
            return true;
        }

        if (_activeDutyPart == 3 && _lastPart3Row is not null && Part3Rows.Contains(_lastPart3Row))
        {
            collection = Part3Rows;
            row = _lastPart3Row;
            return true;
        }

        if (_activeDutyPart == 2 && _lastPart2Row is not null && Part2Rows.Contains(_lastPart2Row))
        {
            collection = Part2Rows;
            row = _lastPart2Row;
            return true;
        }

        if (_lastPart1Row is not null && Rows.Contains(_lastPart1Row))
        {
            collection = Rows;
            row = _lastPart1Row;
            return true;
        }

        if (_lastPart2Row is not null && Part2Rows.Contains(_lastPart2Row))
        {
            collection = Part2Rows;
            row = _lastPart2Row;
            return true;
        }

        row = null!;
        collection = Rows;
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanMoveRowToPart2))]
    private void MoveSelectedRowToPart2()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var row = SelectedRow;
        Rows.Remove(row);
        SelectedRow = null;
        Part2Rows.Add(row);
        var wasFirstPart2Row = Part2Rows.Count == 1;
        SelectedPart2Row = row;
        _activeDutyPart = 2;
        if (wasFirstPart2Row)
        {
            SuggestDutyNumberPart2();
        }
        RefreshStats();
        StatusMessage = "Fahrt nach Teil 2 verschoben.";
    }

    private bool CanMoveRowToPart2() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanMoveRowToPart1))]
    private void MoveSelectedRowToPart1()
    {
        if (SelectedPart2Row is null)
        {
            return;
        }

        var row = SelectedPart2Row;
        Part2Rows.Remove(row);
        SelectedPart2Row = null;
        Rows.Add(row);
        SelectedRow = row;
        _activeDutyPart = 1;
        RefreshStats();
        StatusMessage = "Fahrt nach Teil 1 verschoben.";
    }

    private bool CanMoveRowToPart1() => SelectedPart2Row is not null;

    [RelayCommand]
    private void MergeSplitDuty()
    {
        if (!IsSplitDuty)
        {
            return;
        }

        foreach (var row in Part2Rows.ToList())
        {
            Rows.Add(row);
        }

        foreach (var row in Part3Rows.ToList())
        {
            Rows.Add(row);
        }

        Part2Rows.Clear();
        Part3Rows.Clear();
        SelectedPart2Row = null;
        SelectedPart3Row = null;
        DutyNumberPart2 = string.Empty;
        DutyNumberPart3 = string.Empty;
        SortRowsByOperatingDay();
        RefreshStats();
        StatusMessage = "Geteilter Dienst zusammengeführt.";
    }

    [RelayCommand(CanExecute = nameof(CanIntelligentSplitDuty))]
    private void IntelligentSplitDuty()
    {
        IntelligentSplitDutyInternal();
    }

    private bool CanIntelligentSplitDuty() => !IsSplitDuty && Rows.Count >= 2;

    [RelayCommand(CanExecute = nameof(CanSplitDutyAtSelection))]
    private void SplitDutyAtSelection()
    {
        if (_activeGridSelection.Count != 2)
        {
            StatusMessage = "Bitte genau zwei Zeilen im gleichen Dienstteil markieren.";
            return;
        }

        if (!TryGetActivePartCollection(out var source))
        {
            StatusMessage = "Bitte zuerst einen Dienstteil auswählen.";
            return;
        }

        var indices = _activeGridSelection
            .Select(row => source.IndexOf(row))
            .Where(index => index >= 0)
            .OrderBy(index => index)
            .Distinct()
            .ToList();

        if (indices.Count != 2)
        {
            StatusMessage = "Bitte zwei Zeilen im gleichen Dienstteil markieren.";
            return;
        }

        var splitAfterIndex = indices[0];
        var moveFromIndex = splitAfterIndex + 1;
        if (moveFromIndex >= source.Count)
        {
            StatusMessage = "Trennung nicht möglich – nach der ersten markierten Zeile folgt keine weitere Fahrt.";
            return;
        }

        MoveRowsToNextPart(source, moveFromIndex);
    }

    private bool CanSplitDutyAtSelection() => _activeGridSelection.Count == 2;

    private bool TryGetActivePartCollection(out ObservableCollection<DutyTemplateRowItem> collection)
    {
        collection = _activeDutyPart switch
        {
            3 => Part3Rows,
            2 => Part2Rows,
            _ => Rows
        };
        return collection.Count > 0;
    }

    private void MoveRowsToNextPart(ObservableCollection<DutyTemplateRowItem> source, int fromIndex)
    {
        var toMove = source.Skip(fromIndex).ToList();
        foreach (var row in toMove)
        {
            source.Remove(row);
        }

        if (ReferenceEquals(source, Rows))
        {
            foreach (var row in toMove)
            {
                Part2Rows.Add(row);
            }

            SuggestDutyNumberPart2();
        }
        else if (ReferenceEquals(source, Part2Rows))
        {
            foreach (var row in toMove)
            {
                Part3Rows.Add(row);
            }

            SuggestDutyNumberPart3();
        }
        else
        {
            StatusMessage = "Teil 3 kann nicht weiter getrennt werden.";
            foreach (var row in toMove)
            {
                source.Add(row);
            }

            return;
        }

        NotifySplitStateChanged();
        RefreshStats();
        StatusMessage =
            $"Dienst getrennt – {toMove.Count} Fahrt(en) in Teil {(ReferenceEquals(source, Rows) ? 2 : 3)} verschoben.";
    }

    private void NotifySplitStateChanged()
    {
        OnPropertyChanged(nameof(IsSplitDuty));
        OnPropertyChanged(nameof(IsThreePartDuty));
        OnPropertyChanged(nameof(ExportPart1ButtonLabel));
        IntelligentSplitDutyCommand.NotifyCanExecuteChanged();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        ExportPart1PdfCommand.NotifyCanExecuteChanged();
        ExportPart2PdfCommand.NotifyCanExecuteChanged();
        ExportPart3PdfCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ImportFahrplan()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Fahrplan (*.pdf;*.xlsx;*.xlsm;*.csv;*.txt;*.tsv)|*.pdf;*.xlsx;*.xlsm;*.csv;*.txt;*.tsv|Excel Ersatzfahrplan (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|PDF Ersatzfahrplan (*.pdf)|*.pdf|CSV/TXT (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|Alle Dateien (*.*)|*.*",
            Title = "Fahrplan importieren (Excel, PDF, CSV oder TXT)"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ResetEditorFields();

        try
        {
            var import = DutyTemplateFahrplanParser.ParseFileWithHints(dialog.FileName);
            var parsed = import.Rows;
            ImportRows.Clear();
            foreach (var row in parsed)
            {
                ImportRows.Add(new DutyTemplateImportItem(row));
            }

            OnPropertyChanged(nameof(HasImportRows));
            ImportFileName = Path.GetFileName(dialog.FileName);
            if (parsed.Count == 0)
            {
                StatusMessage = $"Keine verwertbaren Zeilen in „{ImportFileName}“ gefunden.";
                return;
            }

            ApplyImportHints(import.Hints);

            foreach (var item in ImportRows)
            {
                item.IsSelected = true;
            }

            ApplySelectedImportRows();
            ApplyDefaultLineCourseToRows();
            var lineHint = !string.IsNullOrWhiteSpace(ImportedLine)
                ? $" Fahrplan-Linie: {ImportedLine.Trim()} – bei Bedarf Linie/Kurs (Betrieb) anpassen (z. B. 128/03)."
                : string.Empty;
            if (!IsSplitDuty)
            {
                StatusMessage = $"{parsed.Count} Fahrt(en) aus „{ImportFileName}“ importiert – ein Dienstteil.{lineHint}";
            }
            else if (!StatusMessage.Contains("automatisch", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"{parsed.Count} Fahrt(en) importiert.{lineHint}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fahrplan-Import fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ApplySelectedImportRows()
    {
        var selected = ImportRows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Bitte mindestens eine importierte Zeile auswählen.";
            return;
        }

        foreach (var item in selected)
        {
            var row = DutyTemplateRowItem.FromModel(item.Source.ToTemplateRow());
            AttachRowHandler(row);
            Rows.Add(row);
        }

        SortRowsByOperatingDay();
        RefreshStats();
        StatusMessage = $"{selected.Count} Fahrt(en) in die Dienstvorlage übernommen.";
    }

    [RelayCommand]
    private void SelectAllImportRows()
    {
        foreach (var row in ImportRows)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void LoadSelectedTemplate()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine gespeicherte Vorlage auswählen.";
            return;
        }

        LoadTemplate(SelectedTemplate);
        StatusMessage = $"Vorlage „{SelectedTemplate.Name}“ geladen – „Aktualisieren“ überschreibt nur diese Vorlage.";
    }

    [RelayCommand]
    private void ApplyLineCourseToAllRows()
    {
        ApplyDefaultLineCourseToRows();
        var lineCourse = DefaultLineCourse.Trim();
        StatusMessage = string.IsNullOrWhiteSpace(lineCourse)
            ? "Bitte Linie/Kurs (Betrieb) eingeben."
            : $"Linie/Kurs „{lineCourse}“ auf alle {Rows.Count} Fahrt(en) angewendet.";
    }

    [RelayCommand]
    private void DeselectAllImportRows()
    {
        foreach (var row in ImportRows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private void SaveTemplate()
    {
        var store = AppServices.DutyTemplates;
        if (store is null)
        {
            StatusMessage = "Dienstvorlagen sind nur im Planer verfügbar.";
            return;
        }

        var name = TemplateName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Bitte einen Namen für die Dienstvorlage eingeben.";
            return;
        }

        var duplicate = SavedTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) &&
            t.Id != _loadedTemplateId);
        if (duplicate is not null)
        {
            StatusMessage = $"Der Name „{name}“ ist bereits vergeben.";
            return;
        }

        var template = BuildCurrentTemplate();
        var isNew = _loadedTemplateId is null;
        store.Save(template);
        _loadedTemplateId = template.Id;
        ReloadTemplateList();
        SelectedTemplate = SavedTemplates.FirstOrDefault(t => t.Id == template.Id);
        OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
        PersistEditorSession();
        ReportSaveSuccess(isNew
            ? $"Neue Vorlage „{template.Name}“ gespeichert."
            : $"Vorlage „{template.Name}“ aktualisiert.");
    }

    private bool CanSaveTemplate() => !string.IsNullOrWhiteSpace(TemplateName);

    [RelayCommand(CanExecute = nameof(CanUpdateLoadedTemplate))]
    private void UpdateLoadedTemplate() => SaveTemplate();

    [RelayCommand]
    private void DeleteSelectedTemplate()
    {
        var store = AppServices.DutyTemplates;
        if (store is null)
        {
            StatusMessage = "Dienstvorlagen sind nur im Planer verfügbar.";
            return;
        }

        if (SelectedTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine gespeicherte Vorlage auswählen.";
            return;
        }

        var name = SelectedTemplate.Name;
        var id = SelectedTemplate.Id;
        if (!store.Delete(id))
        {
            ReportSaveError("Vorlage konnte nicht gelöscht werden.");
            return;
        }

        if (string.Equals(_loadedTemplateId, id, StringComparison.Ordinal))
        {
            _loadedTemplateId = null;
            OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
        }

        SelectedTemplate = null;
        ReloadTemplateList();
        ReportSaveSuccess($"Vorlage „{name}“ gelöscht.");
    }

    [RelayCommand(CanExecute = nameof(CanExportPart1Pdf))]
    private void ExportPart1Pdf() => ExportSinglePartPdf(1);

    [RelayCommand(CanExecute = nameof(CanExportPart2Pdf))]
    private void ExportPart2Pdf() => ExportSinglePartPdf(2);

    [RelayCommand(CanExecute = nameof(CanExportPart3Pdf))]
    private void ExportPart3Pdf() => ExportSinglePartPdf(3);

    private void ExportSinglePartPdf(int part)
    {
        var template = BuildCurrentTemplate();
        var (rows, dutyNumber, partRows) = part switch
        {
            3 => (template.Part3Rows, template.DutyNumberPart3, template.Part3Rows),
            2 => (template.Part2Rows, template.DutyNumberPart2, template.Part2Rows),
            _ => (template.Rows, template.DutyNumber, template.Rows)
        };

        if (partRows.Count == 0)
        {
            StatusMessage = $"Teil {part} enthält keine Fahrten.";
            return;
        }

        if (string.IsNullOrWhiteSpace(dutyNumber))
        {
            StatusMessage = $"Bitte Dienstnummer für Teil {part} eingeben.";
            return;
        }

        var safeName = SanitizeFileName(dutyNumber);
        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = string.IsNullOrWhiteSpace(safeName) ? $"dienst-teil{part}.pdf" : $"{safeName}.pdf",
            DefaultExt = ".pdf",
            Title = $"Dienstvorlage für Teil {part} speichern"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DienstvorlagenPdfGenerator.GeneratePart(dialog.FileName, template, rows, dutyNumber, part);
            ReportSaveSuccess($"Dienstvorlage Teil {part} erstellt: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ReportSaveError($"PDF-Erstellung fehlgeschlagen: {ex.Message}");
        }
    }

    private bool CanExportPart1Pdf() => Rows.Count > 0;

    private bool CanExportPart2Pdf() => Part2Rows.Count > 0;

    private bool CanExportPart3Pdf() => Part3Rows.Count > 0;

    private static string SanitizeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

    [RelayCommand]
    private void RecalculateStats()
    {
        SortRowsByOperatingDay();
        SortPart2RowsByOperatingDay();
        SortPart3RowsByOperatingDay();
        RefreshStats();
    }

    private void SortPart2RowsByOperatingDay()
    {
        if (Part2Rows.Count <= 1)
        {
            return;
        }

        var ordered = DutyTemplateCalculator.OrderRows(Part2Rows.Select(row => row.ToModel()));
        Part2Rows.Clear();
        foreach (var row in ordered)
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Part2Rows.Add(item);
        }
    }

    private void SortPart3RowsByOperatingDay()
    {
        if (Part3Rows.Count <= 1)
        {
            return;
        }

        var ordered = DutyTemplateCalculator.OrderRows(Part3Rows.Select(row => row.ToModel()));
        Part3Rows.Clear();
        foreach (var row in ordered)
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Part3Rows.Add(item);
        }
    }

    private void IntelligentSplitDutyInternal()
    {
        if (IsSplitDuty)
        {
            return;
        }

        var prep = DutyTemplateCalculator.ParseNonNegativeMinutes(
            WorkPreparationMinutes,
            DutyTemplateCalculator.DefaultWorkPreparationMinutes);
        var followUp = DutyTemplateCalculator.ParseNonNegativeMinutes(
            WorkFollowUpMinutes,
            DutyTemplateCalculator.DefaultWorkFollowUpMinutes);
        var allRows = Rows.Select(row => row.ToModel()).ToList();
        var orderedRows = DutyTemplateCalculator.OrderRows(allRows);
        var result = DutyTemplateSplitter.Analyze(orderedRows, prep, followUp);

        if (!result.RequiresSplit)
        {
            StatusMessage = "Dienst ist unter 9 Stunden – Aufteilung nicht erforderlich.";
            return;
        }

        if (!result.FoundValidSplit)
        {
            StatusMessage = result.WarningMessage ?? "Dienst konnte nicht intelligent aufgeteilt werden.";
            return;
        }

        ApplySplitAt(result.SplitAfterIndex, orderedRows);
        StatusMessage =
            $"Dienst intelligent in 2 Teile aufgeteilt (max. 9 h pro Teil). " +
            $"Teil 1: {Rows.Count} Fahrt(en), Teil 2: {Part2Rows.Count} Fahrt(en).";
    }

    private void TryAutoSplitDuty() => IntelligentSplitDutyInternal();

    private void ApplySplitAt(int splitAfterIndex, IReadOnlyList<DutyTemplateRow>? orderedRows = null)
    {
        orderedRows ??= DutyTemplateCalculator.OrderRows(Rows.Select(row => row.ToModel()));
        if (splitAfterIndex <= 0 || splitAfterIndex >= orderedRows.Count)
        {
            return;
        }

        var part1Ids = orderedRows.Take(splitAfterIndex).Select(row => row.Id).ToHashSet();
        var moveToPart2 = Rows.Where(row => !part1Ids.Contains(row.Id)).ToList();
        foreach (var row in moveToPart2)
        {
            Rows.Remove(row);
            Part2Rows.Add(row);
        }

        SortPart2RowsByOperatingDay();
        SuggestDutyNumberPart2();
        NotifySplitStateChanged();
    }

    private void SuggestDutyNumberPart3()
    {
        if (!string.IsNullOrWhiteSpace(DutyNumberPart3))
        {
            return;
        }

        var suggested = SuggestNextDutyNumber(
            string.IsNullOrWhiteSpace(DutyNumberPart2) ? DutyNumber : DutyNumberPart2);
        if (!string.IsNullOrWhiteSpace(suggested))
        {
            DutyNumberPart3 = suggested;
        }
    }

    private void SuggestDutyNumberPart2()
    {
        if (!string.IsNullOrWhiteSpace(DutyNumberPart2))
        {
            return;
        }

        var suggested = SuggestNextDutyNumber(DutyNumber);
        if (!string.IsNullOrWhiteSpace(suggested))
        {
            DutyNumberPart2 = suggested;
        }
    }

    private static string? SuggestNextDutyNumber(string dutyNumber)
    {
        var trimmed = dutyNumber.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var end = trimmed.Length - 1;
        while (end >= 0 && char.IsDigit(trimmed[end]))
        {
            end--;
        }

        if (end == trimmed.Length - 1)
        {
            return null;
        }

        var prefix = trimmed[..(end + 1)];
        var digits = trimmed[(end + 1)..];
        if (!long.TryParse(digits, out var number))
        {
            return null;
        }

        var next = (number + 1).ToString().PadLeft(digits.Length, '0');
        return prefix + next;
    }

    private void SortRowsByOperatingDay()
    {
        if (Rows.Count <= 1)
        {
            return;
        }

        var ordered = DutyTemplateCalculator.OrderRows(Rows.Select(row => row.ToModel()));
        Rows.Clear();
        foreach (var row in ordered)
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Rows.Add(item);
        }
    }

    private void ApplyImportHints(DutyTemplateImportHints hints)
    {
        if (!string.IsNullOrWhiteSpace(hints.VehicleNumber))
        {
            VehicleNumber = hints.VehicleNumber.Trim();
        }

        ImportedLine = hints.Line.Trim();
        OnPropertyChanged(nameof(HasImportedLine));

        if (string.IsNullOrWhiteSpace(TemplateName) && !string.IsNullOrWhiteSpace(hints.Line))
        {
            TemplateName = string.IsNullOrWhiteSpace(hints.Route)
                ? $"Dienst {hints.Line}"
                : $"Dienst {hints.Line} {hints.Route}";
        }
        if (!string.IsNullOrWhiteSpace(hints.LineCourse))
        {
            _suppressLineCourseApply = true;
            try
            {
                DefaultLineCourse = hints.LineCourse.Trim();
            }
            finally
            {
                _suppressLineCourseApply = false;
            }
        }
    }

    private void ApplyDefaultLineCourseToRows()
    {
        var lineCourse = DefaultLineCourse.Trim();
        if (string.IsNullOrWhiteSpace(lineCourse))
        {
            return;
        }

        foreach (var row in Rows)
        {
            row.LineCourse = lineCourse;
        }

        foreach (var row in Part2Rows)
        {
            row.LineCourse = lineCourse;
        }

        foreach (var row in Part3Rows)
        {
            row.LineCourse = lineCourse;
        }
    }

    private DutyTemplate BuildCurrentTemplate()
    {
        var template = new DutyTemplate
        {
            Id = _loadedTemplateId ?? Guid.NewGuid().ToString("N"),
            Name = TemplateName.Trim(),
            CompanyLogoId = SelectedCompanyLogoId.Trim(),
            DutyNumber = DutyNumber.Trim(),
            DutyNumberPart2 = DutyNumberPart2.Trim(),
            DutyNumberPart3 = DutyNumberPart3.Trim(),
            Contractor = Contractor.Trim(),
            OperatingDay = OperatingDay.Trim(),
            VehicleNumber = VehicleNumber.Trim(),
            DefaultLineCourse = DefaultLineCourse.Trim(),
            ImportedLine = ImportedLine.Trim(),
            Notes = Notes.Trim(),
            SubtractUnpaidBreak30Minutes = SubtractUnpaidBreak30Minutes,
            SubtractUnpaidBreak30MinutesPart2 = SubtractUnpaidBreak30MinutesPart2,
            SubtractUnpaidBreak30MinutesPart3 = SubtractUnpaidBreak30MinutesPart3,
            CustomUnpaidBreakDeductionMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
                CustomUnpaidBreakDeductionMinutes,
                0),
            WorkPreparationMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
                WorkPreparationMinutes,
                DutyTemplateCalculator.DefaultWorkPreparationMinutes),
            WorkFollowUpMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
                WorkFollowUpMinutes,
                DutyTemplateCalculator.DefaultWorkFollowUpMinutes),
            Rows = Rows.Select(r => r.ToModel()).ToList(),
            Part2Rows = Part2Rows.Select(r => r.ToModel()).ToList(),
            Part3Rows = Part3Rows.Select(r => r.ToModel()).ToList()
        };
        return template;
    }

    private void LoadTemplate(DutyTemplate template)
    {
        _loadedTemplateId = template.Id;
        TemplateName = template.Name;
        SelectedCompanyLogoId = ResolveCompanyLogoSelection(template.CompanyLogoId);
        DutyNumber = template.DutyNumber;
        DutyNumberPart2 = template.DutyNumberPart2;
        DutyNumberPart3 = template.DutyNumberPart3;
        Contractor = template.Contractor;
        ApplyOperatingDayToSelection(template.OperatingDay);
        OperatingDay = DutyOperatingDayHelper.FormatDisplay(
            OperatingDaySelections.Where(option => option.IsSelected).Select(option => option.Day));
        VehicleNumber = template.VehicleNumber;
        _suppressLineCourseApply = true;
        try
        {
            DefaultLineCourse = !string.IsNullOrWhiteSpace(template.DefaultLineCourse)
                ? template.DefaultLineCourse.Trim()
                : template.Rows.Select(row => row.LineCourse.Trim())
                    .FirstOrDefault(lineCourse => !string.IsNullOrWhiteSpace(lineCourse)) ?? string.Empty;
            ImportedLine = template.ImportedLine.Trim();
        }
        finally
        {
            _suppressLineCourseApply = false;
        }

        OnPropertyChanged(nameof(HasImportedLine));
        Notes = template.Notes;
        SubtractUnpaidBreak30Minutes = template.SubtractUnpaidBreak30Minutes;
        SubtractUnpaidBreak30MinutesPart2 = template.SubtractUnpaidBreak30MinutesPart2;
        SubtractUnpaidBreak30MinutesPart3 = template.SubtractUnpaidBreak30MinutesPart3;
        CustomUnpaidBreakDeductionMinutes = template.CustomUnpaidBreakDeductionMinutes > 0
            ? template.CustomUnpaidBreakDeductionMinutes.ToString()
            : string.Empty;
        WorkPreparationMinutes = DutyTemplateCalculator.ResolvePreparationMinutes(template.WorkPreparationMinutes).ToString();
        WorkFollowUpMinutes = DutyTemplateCalculator.ResolveFollowUpMinutes(template.WorkFollowUpMinutes).ToString();
        Rows.Clear();
        Part2Rows.Clear();
        Part3Rows.Clear();
        foreach (var row in DutyTemplateCalculator.OrderRows(template.Rows))
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Rows.Add(item);
        }

        foreach (var row in DutyTemplateCalculator.OrderRows(template.Part2Rows))
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Part2Rows.Add(item);
        }

        foreach (var row in DutyTemplateCalculator.OrderRows(template.Part3Rows))
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            Part3Rows.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(DefaultLineCourse))
        {
            ApplyDefaultLineCourseToRows();
        }

        RefreshStats();
        OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
        StatusMessage = $"Vorlage „{template.Name}“ geladen.";
        PersistEditorSession();
    }

    private DutyTemplateEditorSessionStore? TryGetSessionStore() =>
        AppServices.IsInitialized ? AppServices.DutyTemplateEditorSession : null;

    private void ScheduleEditorSessionSave()
    {
        _sessionSaveTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _sessionSaveTimer.Tick -= OnSessionSaveTimerTick;
        _sessionSaveTimer.Tick += OnSessionSaveTimerTick;
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void OnSessionSaveTimerTick(object? sender, EventArgs e)
    {
        _sessionSaveTimer?.Stop();
        PersistEditorSession();
    }

    private void FlushEditorSessionNow() => PersistEditorSession();

    private void PersistEditorSession()
    {
        if (_suppressSessionSave)
        {
            return;
        }

        var store = TryGetSessionStore();
        if (store is null)
        {
            return;
        }

        var session = BuildEditorSession();
        if (!session.HasContent())
        {
            store.Clear();
            return;
        }

        store.Save(session);
    }

    private void ClearEditorSession() => TryGetSessionStore()?.Clear();

    private DutyTemplateEditorSession BuildEditorSession() => new()
    {
        LoadedTemplateId = _loadedTemplateId,
        TemplateName = TemplateName.Trim(),
        CompanyLogoId = SelectedCompanyLogoId.Trim(),
        DutyNumber = DutyNumber.Trim(),
        DutyNumberPart2 = DutyNumberPart2.Trim(),
        DutyNumberPart3 = DutyNumberPart3.Trim(),
        Contractor = Contractor.Trim(),
        OperatingDay = OperatingDay.Trim(),
        VehicleNumber = VehicleNumber.Trim(),
        DefaultLineCourse = DefaultLineCourse.Trim(),
        ImportedLine = ImportedLine.Trim(),
        Notes = Notes.Trim(),
        ImportFileName = ImportFileName.Trim(),
        SubtractUnpaidBreak30Minutes = SubtractUnpaidBreak30Minutes,
        SubtractUnpaidBreak30MinutesPart2 = SubtractUnpaidBreak30MinutesPart2,
        SubtractUnpaidBreak30MinutesPart3 = SubtractUnpaidBreak30MinutesPart3,
        CustomUnpaidBreakDeductionMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
            CustomUnpaidBreakDeductionMinutes,
            0),
        WorkPreparationMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
            WorkPreparationMinutes,
            DutyTemplateCalculator.DefaultWorkPreparationMinutes),
        WorkFollowUpMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(
            WorkFollowUpMinutes,
            DutyTemplateCalculator.DefaultWorkFollowUpMinutes),
        Rows = Rows.Select(row => row.ToModel()).ToList(),
        Part2Rows = Part2Rows.Select(row => row.ToModel()).ToList(),
        Part3Rows = Part3Rows.Select(row => row.ToModel()).ToList()
    };

    private bool TryRestoreEditorSession()
    {
        var store = TryGetSessionStore();
        if (store is null)
        {
            return false;
        }

        var session = store.Load();
        if (session is null || !session.HasContent())
        {
            return false;
        }

        _suppressSessionSave = true;
        _suppressLineCourseApply = true;
        try
        {
            _loadedTemplateId = string.IsNullOrWhiteSpace(session.LoadedTemplateId)
                ? null
                : session.LoadedTemplateId.Trim();
            TemplateName = session.TemplateName;
            SelectedCompanyLogoId = ResolveCompanyLogoSelection(session.CompanyLogoId);
            DutyNumber = session.DutyNumber;
            DutyNumberPart2 = session.DutyNumberPart2;
            DutyNumberPart3 = session.DutyNumberPart3;
            Contractor = session.Contractor;
            ApplyOperatingDayToSelection(session.OperatingDay);
            OperatingDay = DutyOperatingDayHelper.FormatDisplay(
                OperatingDaySelections.Where(option => option.IsSelected).Select(option => option.Day));
            VehicleNumber = session.VehicleNumber;
            DefaultLineCourse = session.DefaultLineCourse;
            ImportedLine = session.ImportedLine;
            Notes = session.Notes;
            ImportFileName = session.ImportFileName;
            SubtractUnpaidBreak30Minutes = session.SubtractUnpaidBreak30Minutes;
            SubtractUnpaidBreak30MinutesPart2 = session.SubtractUnpaidBreak30MinutesPart2;
            SubtractUnpaidBreak30MinutesPart3 = session.SubtractUnpaidBreak30MinutesPart3;
            CustomUnpaidBreakDeductionMinutes = session.CustomUnpaidBreakDeductionMinutes > 0
                ? session.CustomUnpaidBreakDeductionMinutes.ToString()
                : string.Empty;
            WorkPreparationMinutes = DutyTemplateCalculator.ResolvePreparationMinutes(session.WorkPreparationMinutes).ToString();
            WorkFollowUpMinutes = DutyTemplateCalculator.ResolveFollowUpMinutes(session.WorkFollowUpMinutes).ToString();
            Rows.Clear();
            Part2Rows.Clear();
            Part3Rows.Clear();
            foreach (var row in DutyTemplateCalculator.OrderRows(session.Rows))
            {
                var item = DutyTemplateRowItem.FromModel(row);
                AttachRowHandler(item);
                Rows.Add(item);
            }

            foreach (var row in DutyTemplateCalculator.OrderRows(session.Part2Rows))
            {
                var item = DutyTemplateRowItem.FromModel(row);
                AttachRowHandler(item);
                Part2Rows.Add(item);
            }

            foreach (var row in DutyTemplateCalculator.OrderRows(session.Part3Rows))
            {
                var item = DutyTemplateRowItem.FromModel(row);
                AttachRowHandler(item);
                Part3Rows.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(_loadedTemplateId))
            {
                SelectedTemplate = SavedTemplates.FirstOrDefault(t => t.Id == _loadedTemplateId);
            }
        }
        finally
        {
            _suppressLineCourseApply = false;
            _suppressSessionSave = false;
        }

        OnPropertyChanged(nameof(HasImportedLine));
        OnPropertyChanged(nameof(CanUpdateLoadedTemplate));
        RefreshStats();
        StatusMessage = "Letzte Bearbeitung wiederhergestellt.";
        return true;
    }

    private void ReloadTemplateList()
    {
        SavedTemplates.Clear();
        var store = AppServices.DutyTemplates;
        if (store is null)
        {
            OnPropertyChanged(nameof(HasSavedTemplates));
            return;
        }

        foreach (var template in store.LoadAll())
        {
            SavedTemplates.Add(template);
        }

        OnPropertyChanged(nameof(HasSavedTemplates));
    }

    private string ResolveCompanyLogoSelection(string? logoId)
    {
        var trimmed = logoId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(trimmed) &&
            CompanyLogoOptions.Any(option => option.Id == trimmed))
        {
            return trimmed;
        }

        return CompanyLogoOptions.FirstOrDefault()?.Id ?? string.Empty;
    }

    private DutyTemplate? GetLoadedTemplate() =>
        string.IsNullOrEmpty(_loadedTemplateId)
            ? null
            : SavedTemplates.FirstOrDefault(t => t.Id == _loadedTemplateId);

    private void AttachOperatingDayHandler(OperatingDayOptionItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OperatingDayOptionItem.IsSelected) || _suppressOperatingDaySync)
            {
                return;
            }

            SyncOperatingDayFromSelection();
        };
    }

    private void SyncOperatingDayFromSelection()
    {
        var selected = OperatingDaySelections
            .Where(option => option.IsSelected)
            .Select(option => option.Day);
        OperatingDay = DutyOperatingDayHelper.FormatDisplay(selected);
    }

    private void ApplyOperatingDayToSelection(string? operatingDay)
    {
        _suppressOperatingDaySync = true;
        try
        {
            var days = DutyOperatingDayHelper.Parse(operatingDay);
            foreach (var option in OperatingDaySelections)
            {
                option.IsSelected = days.Contains(option.Day);
            }
        }
        finally
        {
            _suppressOperatingDaySync = false;
        }

        OnPropertyChanged(nameof(HasOperatingDayDisplay));
    }

    private void AttachRowHandler(DutyTemplateRowItem row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DutyTemplateRowItem.Remark))
            {
                OnPropertyChanged(nameof(RemarkLegend));
                OnPropertyChanged(nameof(HasRemarkLegend));
            }
            else if (e.PropertyName is nameof(DutyTemplateRowItem.FromTime)
                     or nameof(DutyTemplateRowItem.ToTime))
            {
                RefreshStats();
            }

            ScheduleEditorSessionSave();
        };
    }

    partial void OnSelectedRowChanged(DutyTemplateRowItem? value)
    {
        if (value is not null)
        {
            _activeDutyPart = 1;
            _lastPart1Row = value;
            SelectedPart2Row = null;
            SelectedPart3Row = null;
        }

        MoveSelectedRowToPart2Command.NotifyCanExecuteChanged();
        MoveSelectedRowUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRowDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPart2RowChanged(DutyTemplateRowItem? value)
    {
        if (value is not null)
        {
            _activeDutyPart = 2;
            _lastPart2Row = value;
            SelectedRow = null;
            SelectedPart3Row = null;
        }

        MoveSelectedRowToPart1Command.NotifyCanExecuteChanged();
        MoveSelectedRowUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRowDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPart3RowChanged(DutyTemplateRowItem? value)
    {
        if (value is not null)
        {
            _activeDutyPart = 3;
            _lastPart3Row = value;
            SelectedRow = null;
            SelectedPart2Row = null;
        }

        MoveSelectedRowToPart1Command.NotifyCanExecuteChanged();
        MoveSelectedRowUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRowDownCommand.NotifyCanExecuteChanged();
    }

    private void RefreshStats()
    {
        var template = BuildCurrentTemplate();
        var prep = DutyTemplateCalculator.ResolvePreparationMinutes(template.WorkPreparationMinutes);
        var followUp = DutyTemplateCalculator.ResolveFollowUpMinutes(template.WorkFollowUpMinutes);
        var deductionPart1 = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, 1);
        var deductionPart2 = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, 2);
        var deductionPart3 = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, 3);

        if (IsSplitDuty)
        {
            var part1Stats = DutyTemplateCalculator.ComputePart(template.Rows, prep, followUp, deductionPart1);
            var part2Stats = DutyTemplateCalculator.ComputePart(template.Part2Rows, prep, followUp, deductionPart2);
            ApplyPartStats(
                part1Stats,
                value => Part1ServiceDurationDisplay = value,
                value => Part1PayHoursDisplay = value,
                value => Part1BreaksDisplay = value,
                value => Part1PureDrivingDisplay = value,
                value => Part1PureBreakDisplay = value);
            ApplyPartStats(
                part2Stats,
                value => Part2ServiceDurationDisplay = value,
                value => Part2PayHoursDisplay = value,
                value => Part2BreaksDisplay = value,
                value => Part2PureDrivingDisplay = value,
                value => Part2PureBreakDisplay = value);
            Part1ExceedsMaxDuration = part1Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyMinutes;
            Part2ExceedsMaxDuration = part2Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyMinutes;

            if (IsThreePartDuty)
            {
                var part3Stats = DutyTemplateCalculator.ComputePart(template.Part3Rows, prep, followUp, deductionPart3);
                ApplyPartStats(
                    part3Stats,
                    value => Part3ServiceDurationDisplay = value,
                    value => Part3PayHoursDisplay = value,
                    value => Part3BreaksDisplay = value,
                    value => Part3PureDrivingDisplay = value,
                    value => Part3PureBreakDisplay = value);
                Part3ExceedsMaxDuration = part3Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyMinutes;
            }
            else
            {
                Part3ServiceDurationDisplay = "0:00";
                Part3PayHoursDisplay = "0:00";
                Part3BreaksDisplay = "–";
                Part3PureDrivingDisplay = "0:00";
                Part3PureBreakDisplay = "–";
                Part3ExceedsMaxDuration = false;
            }

            ServiceDurationDisplay = "–";
            ServiceStartDisplay = "–";
            ServiceEndDisplay = "–";
            PayHoursDisplay = "–";
            BreaksDisplay = "–";
            PureDrivingDisplay = "–";
            PureBreakDisplay = "–";
        }
        else
        {
            var stats = DutyTemplateCalculator.ComputePart(template.Rows, prep, followUp, deductionPart1);
            ServiceDurationDisplay = stats.ServiceDurationDisplay;
            ServiceStartDisplay = DutyTemplateCalculator.GetServiceStartDisplay(template.Rows, prep) ?? "–";
            ServiceEndDisplay = DutyTemplateCalculator.GetServiceEndDisplay(template.Rows, followUp) ?? "–";
            PayHoursDisplay = stats.PayHoursDisplay;
            BreaksDisplay = stats.BreaksDisplay;
            PureDrivingDisplay = stats.PureDrivingDisplay;
            PureBreakDisplay = stats.PureBreakDisplay;

            ApplyPartStats(
                stats,
                value => Part1ServiceDurationDisplay = value,
                value => Part1PayHoursDisplay = value,
                value => Part1BreaksDisplay = value,
                value => Part1PureDrivingDisplay = value,
                value => Part1PureBreakDisplay = value);
            Part2ServiceDurationDisplay = "0:00";
            Part2PayHoursDisplay = "0:00";
            Part2BreaksDisplay = "–";
            Part2PureDrivingDisplay = "0:00";
            Part2PureBreakDisplay = "–";
            Part1ExceedsMaxDuration = stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyMinutes;
            Part2ExceedsMaxDuration = false;
        }

        OnPropertyChanged(nameof(IsSplitDuty));
        OnPropertyChanged(nameof(IsThreePartDuty));
        OnPropertyChanged(nameof(ExportPart1ButtonLabel));
        ExportPart1PdfCommand.NotifyCanExecuteChanged();
        ExportPart2PdfCommand.NotifyCanExecuteChanged();
        ExportPart3PdfCommand.NotifyCanExecuteChanged();
        IntelligentSplitDutyCommand.NotifyCanExecuteChanged();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
        MoveSelectedRowToPart2Command.NotifyCanExecuteChanged();
        MoveSelectedRowToPart1Command.NotifyCanExecuteChanged();
        MoveSelectedRowUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRowDownCommand.NotifyCanExecuteChanged();
        MergeSplitDutyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RemarkLegend));
        OnPropertyChanged(nameof(HasRemarkLegend));
        NotifyAllStatsDisplaysChanged();
        ScheduleEditorSessionSave();
    }

    private void NotifyAllStatsDisplaysChanged()
    {
        OnPropertyChanged(nameof(ServiceDurationDisplay));
        OnPropertyChanged(nameof(ServiceStartDisplay));
        OnPropertyChanged(nameof(ServiceEndDisplay));
        OnPropertyChanged(nameof(PayHoursDisplay));
        OnPropertyChanged(nameof(BreaksDisplay));
        OnPropertyChanged(nameof(PureDrivingDisplay));
        OnPropertyChanged(nameof(PureBreakDisplay));
        OnPropertyChanged(nameof(Part1ServiceDurationDisplay));
        OnPropertyChanged(nameof(Part1PayHoursDisplay));
        OnPropertyChanged(nameof(Part1BreaksDisplay));
        OnPropertyChanged(nameof(Part1PureDrivingDisplay));
        OnPropertyChanged(nameof(Part1PureBreakDisplay));
        OnPropertyChanged(nameof(Part2ServiceDurationDisplay));
        OnPropertyChanged(nameof(Part2PayHoursDisplay));
        OnPropertyChanged(nameof(Part2BreaksDisplay));
        OnPropertyChanged(nameof(Part2PureDrivingDisplay));
        OnPropertyChanged(nameof(Part2PureBreakDisplay));
        OnPropertyChanged(nameof(Part3ServiceDurationDisplay));
        OnPropertyChanged(nameof(Part3PayHoursDisplay));
        OnPropertyChanged(nameof(Part3BreaksDisplay));
        OnPropertyChanged(nameof(Part3PureDrivingDisplay));
        OnPropertyChanged(nameof(Part3PureBreakDisplay));
    }

    private static void ApplyPartStats(
        DutyTemplateStats stats,
        Action<string> setServiceDuration,
        Action<string> setPayHours,
        Action<string> setBreaks,
        Action<string> setPureDriving,
        Action<string> setPureBreak)
    {
        setServiceDuration(stats.ServiceDurationDisplay);
        setPayHours(stats.PayHoursDisplay);
        setBreaks(stats.BreaksDisplay);
        setPureDriving(stats.PureDrivingDisplay);
        setPureBreak(stats.PureBreakDisplay);
    }
}
