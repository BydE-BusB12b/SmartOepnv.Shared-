using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Dienstvorlagen;
using SmartOepnv.AppShared.Views;
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
    private bool _suppressValidDateSync;
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

        AppServices.RegisterFlushBeforeExport(FlushBeforeExport);
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

    [ObservableProperty] private string validFrom = string.Empty;

    [ObservableProperty] private string validTo = string.Empty;

    [ObservableProperty] private string validDateRangeDisplay = string.Empty;

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

    [ObservableProperty] private bool isSplitShift;

    [ObservableProperty] private string splitShiftValidationMessage = string.Empty;

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

    [ObservableProperty] private bool part1ExceedsHardMax;

    [ObservableProperty] private bool part2ExceedsHardMax;

    [ObservableProperty] private bool part3ExceedsHardMax;

    public bool HasMaxDurationViolation =>
        Part1ExceedsMaxDuration || Part2ExceedsMaxDuration || Part3ExceedsMaxDuration;

    public string MaxDutyDurationHint =>
        $"Aufteilung max. {DutyTemplateSplitter.MaxSplitPartDurationLabel} pro Teil · " +
        $"Dienstlänge bis {DutyTemplateSplitter.MaxDutyPartDurationLabel} (ab {DutyTemplateSplitter.StandardDutyPartDurationLabel} Bestätigung)";

    public string MaxDurationViolationMessage =>
        HasHardMaxDurationViolation
            ? $"Dienstlänge überschreitet {DutyTemplateSplitter.MaxDutyPartHours} Stunden " +
              "(inkl. Leerfahrten in der Nachbearbeitung) – bitte anpassen oder aufteilen."
            : HasMaxDurationViolation
                ? $"Dienstlänge über {DutyTemplateSplitter.StandardDutyPartHours} Stunden – beim Speichern Bestätigung erforderlich (FPersV-Ausnahme bis {DutyTemplateSplitter.MaxDutyPartHours} h)."
                : string.Empty;

    public bool HasHardMaxDurationViolation =>
        Part1ExceedsHardMax || Part2ExceedsHardMax || Part3ExceedsHardMax;

    [ObservableProperty] private string importFileName = string.Empty;

    [ObservableProperty] private string selectedCompanyLogoId = string.Empty;

    public ObservableCollection<DutyTemplate> SavedTemplates { get; } = [];

    public ObservableCollection<CompanyLogoOption> CompanyLogoOptions { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Rows { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Part2Rows { get; } = [];

    public ObservableCollection<DutyTemplateRowItem> Part3Rows { get; } = [];

    public bool IsSplitDuty => Part2Rows.Count > 0 || Part3Rows.Count > 0;

    public bool IsDutyDivision => IsSplitDuty && !IsSplitShift;

    public bool IsThreePartDuty => Part3Rows.Count > 0 && !IsSplitShift;

    public string SplitShiftHint =>
        $"Geteilter Dienst (eine Nummer): je Arbeitsteil Vorbereitung und Nachbereitung, dienstfreie Pause mind. {SplitShiftRules.MinBreakMinutes / 60} h, " +
        $"Dienstschicht max. {SplitShiftRules.MaxServiceShiftHours} h. TV-N: jeder Teil mind. {SplitShiftRules.MinPartHours} h, " +
        $"Teil 2 nicht nach {SplitShiftRules.Part2LatestStartHour}:00.";

    public bool ShowOverallDutyStats => !IsDutyDivision;

    public bool HasSplitShiftValidationMessage => !string.IsNullOrWhiteSpace(SplitShiftValidationMessage);

    public string ExportPart1ButtonLabel => IsDutyDivision
        ? "Dienstvorlage erstellen (Teil 1)"
        : "Dienstvorlage erstellen";

    public ObservableCollection<DutyTemplateImportItem> ImportRows { get; } = [];

    public ObservableCollection<OperatingDayOptionItem> OperatingDaySelections { get; } = [];

    public bool HasOperatingDayDisplay => !string.IsNullOrWhiteSpace(OperatingDay);

    public bool HasValidDateRangeDisplay => !string.IsNullOrWhiteSpace(ValidDateRangeDisplay);

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

    partial void OnValidFromChanged(string value)
    {
        if (_suppressValidDateSync)
        {
            return;
        }

        SyncValidDateRangeDisplay();
        ScheduleEditorSessionSave();
    }

    partial void OnValidToChanged(string value)
    {
        if (_suppressValidDateSync)
        {
            return;
        }

        SyncValidDateRangeDisplay();
        ScheduleEditorSessionSave();
    }

    partial void OnValidDateRangeDisplayChanged(string value) =>
        OnPropertyChanged(nameof(HasValidDateRangeDisplay));

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
        if (selectedRows.Count == 0)
        {
            return;
        }

        _activeGridSelection = selectedRows.ToList();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        SplitShiftAtSelectionCommand.NotifyCanExecuteChanged();
    }

    public void CaptureActiveGridSelection(IReadOnlyList<DutyTemplateRowItem> selectedRows)
    {
        _activeGridSelection = selectedRows.ToList();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        SplitShiftAtSelectionCommand.NotifyCanExecuteChanged();
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
        LoadValidDateRangeFromStrings(null, null);
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
        IsSplitShift = false;
        SplitShiftValidationMessage = string.Empty;
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
            Remark = DutyTemplateRemarkHelper.ResolveLeerfahrtRemark(AllRowModels().Select(item => item.Remark)),
            LineCourse = DefaultLineCourse.Trim()
        };
        InsertRow(row);
        StatusMessage = "Leerfahrt eingefügt – Zeiten und Haltestellen eintragen.";
    }

    [RelayCommand]
    private void ApplyIntelligentEmptyRuns()
    {
        var dialog = new DienstvorlagenEmptyRunDialog
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var inserted = ApplyIntelligentEmptyRunsWithRules(dialog.Rules);
        if (inserted > 0)
        {
            StatusMessage = $"{inserted} Leerfahrt(en) automatisch eingefügt.";
        }
    }

    private int ApplyIntelligentEmptyRunsWithRules(
        IReadOnlyList<DutyTemplateEmptyRunRule> rules,
        bool showNoMatchMessage = true)
    {
        var validRules = rules.Where(rule => rule.IsValid).ToList();
        if (validRules.Count == 0)
        {
            if (showNoMatchMessage)
            {
                StatusMessage =
                    "Bitte mindestens eine Leerfahrt-Regel angeben (von Haltestelle, nach Haltestelle, Minuten).";
            }

            return 0;
        }

        var leerfahrtRemark = DutyTemplateRemarkHelper.ResolveLeerfahrtRemark(AllRowModels().Select(item => item.Remark));
        var lineCourse = DefaultLineCourse.Trim();
        var inserted = 0;
        inserted += ReplaceWithEmptyRuns(Rows, validRules, leerfahrtRemark, lineCourse);
        inserted += ReplaceWithEmptyRuns(Part2Rows, validRules, leerfahrtRemark, lineCourse);
        inserted += ReplaceWithEmptyRuns(Part3Rows, validRules, leerfahrtRemark, lineCourse);

        if (inserted > 0)
        {
            RefreshStats();
            ScheduleEditorSessionSave();
        }
        else if (showNoMatchMessage)
        {
            StatusMessage = BuildEmptyRunMissMessage(validRules);
        }

        return inserted;
    }

    private string BuildEmptyRunMissMessage(IReadOnlyList<DutyTemplateEmptyRunRule> rules)
    {
        var models = Rows.Select(row => row.ToModel()).ToList();
        var diagnostics = DutyTemplateEmptyRunInserter.AnalyzeMisses(models, rules);

        if (diagnostics.StopMatches == 0)
        {
            return
                "Keine passende Lücke: Eine Fahrt muss an der Von-Haltestelle enden und die nächste an der Nach-Haltestelle starten. " +
                "Zwischen 3087 und 3094 z. B. Vohwinkel Bstg 2 → Vohwinkel Bstg 1, nicht Gerresheim.";
        }

        if (diagnostics.TimeTooShort > 0)
        {
            var shortest = diagnostics.ShortestGapMinutes is int minutes
                ? $" (kürzeste passende Haltestellen-Pause: {minutes} Min.)"
                : string.Empty;
            var required = rules.Max(rule => rule.DurationMinutes);
            return
                $"Haltestellen passen, aber die Pause ist zu kurz für {required} Min.{shortest} " +
                "– an Gerresheim sind es meist 3 Min., an Vohwinkel z. B. 20 Min. zwischen 3087 und 3094.";
        }

        return "Keine passenden Lücken gefunden – evtl. sind die Leerfahrten bereits eingefügt.";
    }

    private int ReplaceWithEmptyRuns(
        ObservableCollection<DutyTemplateRowItem> collection,
        IReadOnlyList<DutyTemplateEmptyRunRule> rules,
        string leerfahrtRemark,
        string lineCourse)
    {
        if (collection.Count < 2)
        {
            return 0;
        }

        var models = collection.Select(row => row.ToModel()).ToList();
        var (result, inserted) = DutyTemplateEmptyRunInserter.InsertEmptyRuns(
            models,
            rules,
            leerfahrtRemark,
            lineCourse);
        if (inserted <= 0)
        {
            return 0;
        }

        collection.Clear();
        foreach (var row in result)
        {
            var item = DutyTemplateRowItem.FromModel(row);
            AttachRowHandler(item);
            collection.Add(item);
        }

        return inserted;
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

        var wasSplitShift = IsSplitShift;

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
        IsSplitShift = false;
        SplitShiftValidationMessage = string.Empty;
        SortRowsByOperatingDay();
        RefreshStats();
        StatusMessage = wasSplitShift
            ? "Geteilter Dienst zusammengeführt."
            : "Dienstaufteilung zusammengeführt.";
    }

    [RelayCommand]
    private void IntelligentSplitDuty()
    {
        IntelligentSplitDutyInternal();
    }

    [RelayCommand]
    private void SplitDutyAtSelection()
    {
        if (IsSplitShift)
        {
            StatusMessage = "Geteilter Dienst ist aktiv – zuerst „Zusammenführen“.";
            return;
        }

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
            StatusMessage = "Bitte zwei Zeilen im gleichen Dienstteil markieren (Strg+Klick).";
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

    [RelayCommand(CanExecute = nameof(CanSplitShiftAtSelection))]
    private void SplitShiftAtSelection()
    {
        if (IsSplitShift)
        {
            StatusMessage = "Geteilter Dienst ist bereits markiert – zuerst „Zusammenführen“.";
            return;
        }

        if (IsDutyDivision)
        {
            StatusMessage = "Dienstaufteilung ist aktiv – zuerst „Zusammenführen“.";
            return;
        }

        if (_activeGridSelection.Count != 2)
        {
            StatusMessage = "Bitte genau zwei Zeilen im Fahrplan markieren.";
            return;
        }

        if (_activeDutyPart != 1 || !TryGetActivePartCollection(out var source) || !ReferenceEquals(source, Rows))
        {
            StatusMessage = "Geteilter Dienst: zwei Zeilen in Teil 1 markieren (Strg+Klick).";
            return;
        }

        var indices = _activeGridSelection
            .Select(row => Rows.IndexOf(row))
            .Where(index => index >= 0)
            .OrderBy(index => index)
            .Distinct()
            .ToList();

        if (indices.Count != 2)
        {
            StatusMessage = "Bitte zwei Zeilen in Teil 1 markieren (Strg+Klick).";
            return;
        }

        var splitAfterIndex = indices[0];
        var moveFromIndex = splitAfterIndex + 1;
        if (moveFromIndex >= Rows.Count)
        {
            StatusMessage = "Trennung nicht möglich – nach der ersten markierten Zeile folgt keine weitere Fahrt.";
            return;
        }

        MoveRowsToNextPart(Rows, moveFromIndex, asSplitShift: true);
        StatusMessage = "Geteilter Dienst markiert – eine Dienstnummer, FPersV-Regeln in den Kennzahlen.";
    }

    private bool CanSplitShiftAtSelection() =>
        !IsSplitShift && !IsDutyDivision && _activeGridSelection.Count == 2 && _activeDutyPart == 1;

    private bool CanSplitDutyAtSelection() => !IsSplitShift && _activeGridSelection.Count == 2;

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

    private void MoveRowsToNextPart(
        ObservableCollection<DutyTemplateRowItem> source,
        int fromIndex,
        bool asSplitShift = false)
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

            if (asSplitShift)
            {
                IsSplitShift = true;
                DutyNumberPart2 = string.Empty;
                DutyNumberPart3 = string.Empty;
                Part3Rows.Clear();
            }
            else
            {
                IsSplitShift = false;
                SuggestDutyNumberPart2();
            }
        }
        else if (ReferenceEquals(source, Part2Rows))
        {
            if (asSplitShift)
            {
                StatusMessage = "Geteilter Dienst hat nur zwei Arbeitsteile.";
                foreach (var row in toMove)
                {
                    source.Add(row);
                }

                return;
            }

            foreach (var row in toMove)
            {
                Part3Rows.Add(row);
            }

            IsSplitShift = false;
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
        if (!asSplitShift)
        {
            StatusMessage =
                $"Dienst getrennt – {toMove.Count} Fahrt(en) in Teil {(ReferenceEquals(source, Rows) ? 2 : 3)} verschoben.";
        }
    }

    private void NotifySplitStateChanged()
    {
        OnPropertyChanged(nameof(IsSplitDuty));
        OnPropertyChanged(nameof(IsDutyDivision));
        OnPropertyChanged(nameof(IsThreePartDuty));
        OnPropertyChanged(nameof(ExportPart1ButtonLabel));
        OnPropertyChanged(nameof(SplitShiftHint));
        IntelligentSplitDutyCommand.NotifyCanExecuteChanged();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        SplitShiftAtSelectionCommand.NotifyCanExecuteChanged();
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
        if (!DutyTemplateSplitter.TryValidateTemplate(template, out var durationError))
        {
            StatusMessage = durationError ?? $"Dienstlänge überschreitet {DutyTemplateSplitter.MaxDutyPartHours} Stunden.";
            return;
        }

        if (DutyTemplateSplitter.RequiresExtendedShiftConfirmation(template, out var extendedParts))
        {
            var partList = string.Join(", ", extendedParts);
            var confirm = MessageBox.Show(
                $"Folgende Teile überschreiten {DutyTemplateSplitter.StandardDutyPartHours} Stunden " +
                $"(FPersV-Ausnahme bis {DutyTemplateSplitter.MaxDutyPartHours} h): {partList}.\n\nTrotzdem speichern?",
                "Dienstlänge bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusMessage = "Speichern abgebrochen – Dienstlänge nicht bestätigt.";
                return;
            }
        }

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
        if (!DutyTemplatePdfExport.TryGetPart(template, part, out _, out var dutyNumber))
        {
            StatusMessage = $"Teil {part} enthält keine Fahrten.";
            return;
        }

        if (string.IsNullOrWhiteSpace(dutyNumber))
        {
            StatusMessage = $"Bitte Dienstnummer für Teil {part} eingeben.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = DutyTemplatePdfExport.BuildDefaultFileName(dutyNumber, part),
            DefaultExt = ".pdf",
            Title = $"Dienstvorlage für Teil {part} speichern"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DutyTemplatePdfExport.ExportPart(dialog.FileName, template, part);
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
        if (IsSplitShift)
        {
            StatusMessage = "Geteilter Dienst ist aktiv – „Intelligent Aufteilen“ gilt nur für die Dienstaufteilung (G1/G2).";
            return;
        }

        if (IsSplitDuty)
        {
            StatusMessage = "Dienst ist bereits aufgeteilt – zuerst „Zusammenführen“ oder manuell trennen.";
            return;
        }

        if (Rows.Count < 2)
        {
            StatusMessage = "Mindestens zwei Fahrten nötig für „Intelligent Aufteilen“.";
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
            StatusMessage = $"Dienst ist unter {DutyTemplateSplitter.MaxSplitPartHours} Stunden – Aufteilung nicht erforderlich.";
            return;
        }

        if (!result.FoundValidSplit)
        {
            StatusMessage = result.WarningMessage ?? "Dienst konnte nicht intelligent aufgeteilt werden.";
            return;
        }

        if (result.PartCount == 3)
        {
            if (!ApplyThreePartSplit(result.SplitAfterIndex, result.SecondSplitAfterIndex, orderedRows))
            {
                StatusMessage = "Aufteilung in 3 Teile fehlgeschlagen – bitte manuell trennen.";
                return;
            }

            RefreshStats();
            StatusMessage =
                $"Dienst intelligent in 3 Teile aufgeteilt (max. {DutyTemplateSplitter.MaxSplitPartDurationLabel} pro Teil). " +
                $"Teil 1: {Rows.Count}, Teil 2: {Part2Rows.Count}, Teil 3: {Part3Rows.Count} Fahrt(en).";
            return;
        }

        if (!ApplySplitAt(result.SplitAfterIndex, orderedRows))
        {
            StatusMessage = "Aufteilung fehlgeschlagen – bitte zwei Zeilen markieren und „Dienst trennen“ verwenden.";
            return;
        }

        RefreshStats();
        StatusMessage =
            $"Dienst intelligent in 2 Teile aufgeteilt (max. {DutyTemplateSplitter.MaxSplitPartDurationLabel} pro Teil). " +
            $"Teil 1: {Rows.Count} Fahrt(en), Teil 2: {Part2Rows.Count} Fahrt(en).";
    }

    private void TryAutoSplitDuty() => IntelligentSplitDutyInternal();

    private bool ApplySplitAt(int splitAfterIndex, IReadOnlyList<DutyTemplateRow>? orderedRows = null)
    {
        orderedRows ??= DutyTemplateCalculator.OrderRows(Rows.Select(row => row.ToModel()));
        if (splitAfterIndex <= 0 || splitAfterIndex >= orderedRows.Count)
        {
            return false;
        }

        var part1Ids = orderedRows.Take(splitAfterIndex).Select(row => row.Id).ToHashSet();
        var moveToPart2 = Rows.Where(row => !part1Ids.Contains(row.Id)).ToList();
        if (moveToPart2.Count == 0 || part1Ids.Count == 0)
        {
            return false;
        }

        foreach (var row in moveToPart2)
        {
            Rows.Remove(row);
            Part2Rows.Add(row);
        }

        SortPart2RowsByOperatingDay();
        IsSplitShift = false;
        SuggestDutyNumberPart2();
        NotifySplitStateChanged();
        return true;
    }

    private bool ApplyThreePartSplit(
        int firstSplitIndex,
        int secondSplitIndex,
        IReadOnlyList<DutyTemplateRow>? orderedRows = null)
    {
        orderedRows ??= DutyTemplateCalculator.OrderRows(Rows.Select(row => row.ToModel()));
        if (firstSplitIndex <= 0
            || secondSplitIndex <= firstSplitIndex
            || secondSplitIndex >= orderedRows.Count)
        {
            return false;
        }

        var part2Ids = orderedRows
            .Skip(firstSplitIndex)
            .Take(secondSplitIndex - firstSplitIndex)
            .Select(row => row.Id)
            .ToHashSet();
        var part3Ids = orderedRows
            .Skip(secondSplitIndex)
            .Select(row => row.Id)
            .ToHashSet();
        if (part2Ids.Count == 0 || part3Ids.Count == 0)
        {
            return false;
        }

        var moveToPart2 = Rows.Where(row => part2Ids.Contains(row.Id)).ToList();
        var moveToPart3 = Rows.Where(row => part3Ids.Contains(row.Id)).ToList();
        if (moveToPart2.Count == 0 || moveToPart3.Count == 0)
        {
            return false;
        }

        foreach (var row in moveToPart2.Concat(moveToPart3).ToList())
        {
            Rows.Remove(row);
        }

        foreach (var row in moveToPart2)
        {
            Part2Rows.Add(row);
        }

        foreach (var row in moveToPart3)
        {
            Part3Rows.Add(row);
        }

        SortPart2RowsByOperatingDay();
        SortPart3RowsByOperatingDay();
        IsSplitShift = false;
        SuggestDutyNumberPart2();
        SuggestDutyNumberPart3();
        NotifySplitStateChanged();
        return true;
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
        var (validFrom, validTo) = ResolveValidDatesForSave();
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
            ValidFrom = validFrom,
            ValidTo = validTo,
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
            IsSplitShift = IsSplitShift,
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
        LoadValidDateRangeFromStrings(template.ValidFrom, template.ValidTo);
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
        IsSplitShift = template.IsSplitShift;
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
            Interval = TimeSpan.FromSeconds(2)
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

    public void FlushBeforeExport()
    {
        PersistEditorSession();
        TryAutoSaveCatalogTemplateForExport();
    }

    private void FlushEditorSessionNow() => FlushBeforeExport();

    private void TryAutoSaveCatalogTemplateForExport()
    {
        var store = AppServices.DutyTemplates;
        if (store is null)
        {
            return;
        }

        var name = TemplateName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var duplicate = SavedTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) &&
            t.Id != _loadedTemplateId);
        if (duplicate is not null)
        {
            return;
        }

        var template = BuildCurrentTemplate();
        if (!DutyTemplateSplitter.TryValidateTemplate(template, out _))
        {
            return;
        }

        store.Save(template);
        _loadedTemplateId = template.Id;
        ReloadTemplateList();
    }

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

        try
        {
            var session = BuildEditorSession();
            if (!session.HasContent())
            {
                store.Clear();
                return;
            }

            store.Save(session);
        }
        catch (IOException ex)
        {
            StatusMessage = $"Entwurf konnte nicht gespeichert werden: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Entwurf konnte nicht gespeichert werden: {ex.Message}";
        }
    }

    private void ClearEditorSession() => TryGetSessionStore()?.Clear();

    private DutyTemplateEditorSession BuildEditorSession()
    {
        var (validFrom, validTo) = ResolveValidDatesForSave();
        return new DutyTemplateEditorSession
        {
        LoadedTemplateId = _loadedTemplateId,
        TemplateName = TemplateName.Trim(),
        CompanyLogoId = SelectedCompanyLogoId.Trim(),
        DutyNumber = DutyNumber.Trim(),
        DutyNumberPart2 = DutyNumberPart2.Trim(),
        DutyNumberPart3 = DutyNumberPart3.Trim(),
        Contractor = Contractor.Trim(),
        OperatingDay = OperatingDay.Trim(),
        ValidFrom = validFrom,
        ValidTo = validTo,
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
        IsSplitShift = IsSplitShift,
        Rows = Rows.Select(row => row.ToModel()).ToList(),
        Part2Rows = Part2Rows.Select(row => row.ToModel()).ToList(),
        Part3Rows = Part3Rows.Select(row => row.ToModel()).ToList()
        };
    }

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
            LoadValidDateRangeFromStrings(session.ValidFrom, session.ValidTo);
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
            IsSplitShift = session.IsSplitShift;
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

    private void LoadValidDateRangeFromStrings(string? from, string? to)
    {
        _suppressValidDateSync = true;
        try
        {
            if (!RouteDateRange.TryParse(from, to, out var range))
            {
                ValidFrom = from?.Trim() ?? string.Empty;
                ValidTo = to?.Trim() ?? string.Empty;
                ValidDateRangeDisplay = string.Empty;
                return;
            }

            ValidFrom = range.From is { } parsedFrom ? RouteDateRange.FormatDate(parsedFrom) : string.Empty;
            ValidTo = range.To is { } parsedTo ? RouteDateRange.FormatDate(parsedTo) : string.Empty;
            ValidDateRangeDisplay = RouteDateRange.FormatDisplay(range);
        }
        finally
        {
            _suppressValidDateSync = false;
        }

        OnPropertyChanged(nameof(HasValidDateRangeDisplay));
    }

    private void SyncValidDateRangeDisplay()
    {
        if (!RouteDateRange.TryParse(ValidFrom, ValidTo, out var range))
        {
            ValidDateRangeDisplay = string.Empty;
            OnPropertyChanged(nameof(HasValidDateRangeDisplay));
            return;
        }

        ValidDateRangeDisplay = RouteDateRange.FormatDisplay(range);
        OnPropertyChanged(nameof(HasValidDateRangeDisplay));
    }

    private (string From, string To) ResolveValidDatesForSave()
    {
        if (!RouteDateRange.TryParse(ValidFrom, ValidTo, out var range) || !range.IsRestricted)
        {
            return (string.Empty, string.Empty);
        }

        return (
            range.From is { } from ? RouteDateRange.FormatDate(from) : string.Empty,
            range.To is { } to ? RouteDateRange.FormatDate(to) : string.Empty);
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

        if (IsSplitShift)
        {
            var splitStats = DutyTemplateCalculator.ComputeSplitShiftSummary(
                template.Rows,
                template.Part2Rows,
                prep,
                followUp,
                deductionPart1);
            var part1Stats = DutyTemplateCalculator.ComputePart(template.Rows, prep, followUp, 0);
            var part2Stats = DutyTemplateCalculator.ComputePart(template.Part2Rows, prep, followUp, 0);

            ServiceDurationDisplay = splitStats.ServiceDurationDisplay;
            ServiceStartDisplay = DutyTemplateCalculator.GetServiceStartDisplay(template.Rows, prep) ?? "–";
            ServiceEndDisplay = DutyTemplateCalculator.GetServiceEndDisplay(template.Part2Rows, followUp) ?? "–";
            PayHoursDisplay = splitStats.PayHoursDisplay;
            BreaksDisplay = splitStats.BreaksDisplay;
            PureDrivingDisplay = splitStats.PureDrivingDisplay;
            PureBreakDisplay = splitStats.PureBreakDisplay;

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

            Part3ServiceDurationDisplay = "0:00";
            Part3PayHoursDisplay = "0:00";
            Part3BreaksDisplay = "–";
            Part3PureDrivingDisplay = "0:00";
            Part3PureBreakDisplay = "–";
            Part3ExceedsMaxDuration = false;
            Part3ExceedsHardMax = false;

            var maxSplitShiftMinutes = SplitShiftRules.MaxServiceShiftHours * 60;
            Part1ExceedsMaxDuration = false;
            Part2ExceedsMaxDuration = false;
            Part1ExceedsHardMax = splitStats.ServiceDurationMinutes > maxSplitShiftMinutes;
            Part2ExceedsHardMax = false;

            SplitShiftValidationMessage = DutyTemplateDispositionMapper.TryValidateSplitShiftStructure(
                template,
                out var splitShiftError)
                ? string.Empty
                : splitShiftError;
        }
        else if (IsDutyDivision)
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
            Part1ExceedsMaxDuration = part1Stats.ServiceDurationMinutes > DutyTemplateSplitter.StandardDutyPartMinutes;
            Part2ExceedsMaxDuration = part2Stats.ServiceDurationMinutes > DutyTemplateSplitter.StandardDutyPartMinutes;
            Part1ExceedsHardMax = part1Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyPartMinutes;
            Part2ExceedsHardMax = part2Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyPartMinutes;

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
                Part3ExceedsMaxDuration = part3Stats.ServiceDurationMinutes > DutyTemplateSplitter.StandardDutyPartMinutes;
                Part3ExceedsHardMax = part3Stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyPartMinutes;
            }
            else
            {
                Part3ServiceDurationDisplay = "0:00";
                Part3PayHoursDisplay = "0:00";
                Part3BreaksDisplay = "–";
                Part3PureDrivingDisplay = "0:00";
                Part3PureBreakDisplay = "–";
                Part3ExceedsMaxDuration = false;
                Part3ExceedsHardMax = false;
            }

            ServiceDurationDisplay = "–";
            ServiceStartDisplay = "–";
            ServiceEndDisplay = "–";
            PayHoursDisplay = "–";
            BreaksDisplay = "–";
            PureDrivingDisplay = "–";
            PureBreakDisplay = "–";
            SplitShiftValidationMessage = string.Empty;
        }
        else
        {
            SplitShiftValidationMessage = string.Empty;
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
            Part1ExceedsMaxDuration = stats.ServiceDurationMinutes > DutyTemplateSplitter.StandardDutyPartMinutes;
            Part1ExceedsHardMax = stats.ServiceDurationMinutes > DutyTemplateSplitter.MaxDutyPartMinutes;
            Part2ExceedsMaxDuration = false;
            Part2ExceedsHardMax = false;
        }

        OnPropertyChanged(nameof(HasMaxDurationViolation));
        OnPropertyChanged(nameof(HasHardMaxDurationViolation));
        OnPropertyChanged(nameof(MaxDutyDurationHint));
        OnPropertyChanged(nameof(MaxDurationViolationMessage));

        OnPropertyChanged(nameof(IsSplitDuty));
        OnPropertyChanged(nameof(IsDutyDivision));
        OnPropertyChanged(nameof(IsThreePartDuty));
        OnPropertyChanged(nameof(ExportPart1ButtonLabel));
        OnPropertyChanged(nameof(SplitShiftHint));
        OnPropertyChanged(nameof(SplitShiftValidationMessage));
        OnPropertyChanged(nameof(HasSplitShiftValidationMessage));
        ExportPart1PdfCommand.NotifyCanExecuteChanged();
        ExportPart2PdfCommand.NotifyCanExecuteChanged();
        ExportPart3PdfCommand.NotifyCanExecuteChanged();
        IntelligentSplitDutyCommand.NotifyCanExecuteChanged();
        SplitDutyAtSelectionCommand.NotifyCanExecuteChanged();
        SplitShiftAtSelectionCommand.NotifyCanExecuteChanged();
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
