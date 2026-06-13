using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class FahrerdispoNewShiftDialog : Window
{
    private static readonly Brush InputBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));
    private static readonly Brush InputForeground = Brushes.White;
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush DateFieldBackground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    private readonly ComboBox _employeeBox;
    private readonly TextBox _shiftNameBox;
    private readonly DialogDateField _dateFromField;
    private readonly DialogDateField _dateToField;
    private readonly TextBox _timeFromBox;
    private readonly TextBox _timeToBox;
    private readonly StackPanel _singleShiftPanel;
    private readonly StackPanel _splitShiftPanel;
    private readonly CheckBox _splitShiftCheck;
    private readonly DialogDateField _splitDateField;
    private readonly TextBox _part1FromBox;
    private readonly TextBox _part1ToBox;
    private readonly TextBox _part2FromBox;
    private readonly TextBox _part2ToBox;
    private readonly TextBlock _earliestStartHint;
    private readonly TextBlock _complianceHint;
    private readonly CheckBox _reducedRestCheck;
    private readonly CheckBox _extendedDrivingCheck;
    private readonly CheckBox _reducedWeeklyRestCheck;
    private readonly StackPanel _errorPanel;
    private readonly TextBlock _errorText;
    private readonly IReadOnlyList<DriverDispositionAssignment> _existingAssignments;
    private readonly string? _editingAssignmentId;
    private readonly bool _isEdit;

    public string SelectedDriverKey { get; private set; } = string.Empty;

    public long StartEpochMs { get; private set; }

    public long EndEpochMs { get; private set; }

    public string DisplayLabel { get; private set; } = string.Empty;

    public string ShiftName { get; private set; } = string.Empty;

    public bool ReducedRestBefore { get; private set; }

    public bool ExtendedDrivingDay { get; private set; }

    public bool ReducedWeeklyRestBefore { get; private set; }

    public bool IsSplitShift { get; private set; }

    public long Part1EndEpochMs { get; private set; }

    public long Part2StartEpochMs { get; private set; }

    public bool DeleteRequested { get; private set; }

    public FahrerdispoNewShiftDialog(
        IReadOnlyList<EmployeeRosterItem> employees,
        DateTime defaultDate,
        DriverDispositionAssignment? existing = null,
        IReadOnlyList<DriverDispositionAssignment>? existingAssignments = null)
    {
        _existingAssignments = existingAssignments ?? [];
        _editingAssignmentId = existing?.Id;
        _isEdit = existing is not null;
        var isEdit = _isEdit;
        var isSplitEdit = isEdit && existing!.IsSplitShift;
        var startLocal = isEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.StartEpochMs).LocalDateTime
            : defaultDate;
        var endLocal = isEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.EndEpochMs).LocalDateTime
            : defaultDate;
        var part1EndLocal = isSplitEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.Part1EndEpochMs).LocalDateTime
            : defaultDate.Date.AddHours(10);
        var part2StartLocal = isSplitEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.Part2StartEpochMs).LocalDateTime
            : defaultDate.Date.AddHours(14);
        var part2EndLocal = isSplitEdit ? endLocal : defaultDate.Date.AddHours(18);

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = isEdit ? "Dienst bearbeiten" : "Neuer Dienst";
        Width = 460;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var options = employees
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PersonnelNumber, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EmployeeOption(
                EmployeeDispoKeys.FromEmployee(e),
                BuildEmployeeLabel(e)))
            .Where(o => o.Key.Length > 0)
            .ToList();

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = isEdit ? "Dienst bearbeiten" : "Neuer Dienst",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Fahrerauswahl"));
        _employeeBox = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(EmployeeOption.Label),
            SelectedValuePath = nameof(EmployeeOption.Key),
            MinHeight = 34,
            MaxDropDownHeight = 320,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(8, 4, 8, 4),
            ItemContainerStyle = CreateComboBoxItemStyle()
        };
        if (isEdit)
        {
            _employeeBox.SelectedValue = existing!.DriverKey;
        }
        else if (options.Count > 0)
        {
            _employeeBox.SelectedIndex = 0;
        }

        root.Children.Add(_employeeBox);

        root.Children.Add(MakeLabel("Dienstname"));
        _shiftNameBox = MakeInput("z. B. Frühdienst Linie 1", isEdit ? existing!.Label : string.Empty);
        root.Children.Add(_shiftNameBox);

        _splitShiftCheck = new CheckBox
        {
            Content = "Geteilter Dienst (Teil 1 und Teil 2 mit dienstfreier Zeit)",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 8, 0, 0),
            IsChecked = isSplitEdit
        };
        _splitShiftCheck.Checked += (_, _) => ToggleSplitMode(true);
        _splitShiftCheck.Unchecked += (_, _) => ToggleSplitMode(false);
        root.Children.Add(_splitShiftCheck);

        _singleShiftPanel = new StackPanel
        {
            Visibility = isSplitEdit ? Visibility.Collapsed : Visibility.Visible
        };
        var dateRow = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        dateRow.ColumnDefinitions.Add(new ColumnDefinition());
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dateRow.ColumnDefinitions.Add(new ColumnDefinition());

        var dateFromPanel = new StackPanel();
        dateFromPanel.Children.Add(MakeLabel("Datum von"));
        _dateFromField = new DialogDateField(startLocal.Date, this);
        dateFromPanel.Children.Add(_dateFromField);
        Grid.SetColumn(dateFromPanel, 0);
        dateRow.Children.Add(dateFromPanel);

        var dateToPanel = new StackPanel();
        dateToPanel.Children.Add(MakeLabel("Datum bis"));
        _dateToField = new DialogDateField(endLocal.Date, this, () => _dateFromField.SelectedDate);
        dateToPanel.Children.Add(_dateToField);
        Grid.SetColumn(dateToPanel, 2);
        dateRow.Children.Add(dateToPanel);
        _singleShiftPanel.Children.Add(dateRow);

        _dateFromField.DateChanged += date => _dateToField.SetDate(date);

        var timeRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());

        var timeFromPanel = new StackPanel();
        timeFromPanel.Children.Add(MakeLabel("Uhrzeit von"));
        _timeFromBox = MakeInput("HH:mm", startLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        timeFromPanel.Children.Add(_timeFromBox);
        Grid.SetColumn(timeFromPanel, 0);
        timeRow.Children.Add(timeFromPanel);

        var timeToPanel = new StackPanel();
        timeToPanel.Children.Add(MakeLabel("Uhrzeit bis"));
        _timeToBox = MakeInput("HH:mm", endLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        timeToPanel.Children.Add(_timeToBox);
        Grid.SetColumn(timeToPanel, 2);
        timeRow.Children.Add(timeToPanel);
        _singleShiftPanel.Children.Add(timeRow);
        root.Children.Add(_singleShiftPanel);

        _splitShiftPanel = new StackPanel { Visibility = isSplitEdit ? Visibility.Visible : Visibility.Collapsed };
        _splitShiftPanel.Children.Add(MakeLabel("Datum"));
        _splitDateField = new DialogDateField(startLocal.Date, this);
        _splitShiftPanel.Children.Add(_splitDateField);

        var part1Row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        part1Row.ColumnDefinitions.Add(new ColumnDefinition());
        part1Row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        part1Row.ColumnDefinitions.Add(new ColumnDefinition());
        var part1FromPanel = new StackPanel();
        part1FromPanel.Children.Add(MakeLabel("Teil 1 von"));
        _part1FromBox = MakeInput("HH:mm", startLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        part1FromPanel.Children.Add(_part1FromBox);
        Grid.SetColumn(part1FromPanel, 0);
        part1Row.Children.Add(part1FromPanel);
        var part1ToPanel = new StackPanel();
        part1ToPanel.Children.Add(MakeLabel("Teil 1 bis"));
        _part1ToBox = MakeInput("HH:mm", part1EndLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        part1ToPanel.Children.Add(_part1ToBox);
        Grid.SetColumn(part1ToPanel, 2);
        part1Row.Children.Add(part1ToPanel);
        _splitShiftPanel.Children.Add(part1Row);

        var part2Row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        part2Row.ColumnDefinitions.Add(new ColumnDefinition());
        part2Row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        part2Row.ColumnDefinitions.Add(new ColumnDefinition());
        var part2FromPanel = new StackPanel();
        part2FromPanel.Children.Add(MakeLabel("Teil 2 von"));
        _part2FromBox = MakeInput("HH:mm", part2StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        part2FromPanel.Children.Add(_part2FromBox);
        Grid.SetColumn(part2FromPanel, 0);
        part2Row.Children.Add(part2FromPanel);
        var part2ToPanel = new StackPanel();
        part2ToPanel.Children.Add(MakeLabel("Teil 2 bis"));
        _part2ToBox = MakeInput("HH:mm", part2EndLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        part2ToPanel.Children.Add(_part2ToBox);
        Grid.SetColumn(part2ToPanel, 2);
        part2Row.Children.Add(part2ToPanel);
        _splitShiftPanel.Children.Add(part2Row);

        _splitShiftPanel.Children.Add(new TextBlock
        {
            Text = "TV-N: Unterbrechung mind. 60 min, jeder Teil mind. 2 h, Teil 2 nicht nach 22:00, Dienstschicht max. 14 h.",
            Foreground = Brushes.White,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });
        root.Children.Add(_splitShiftPanel);

        _earliestStartHint = new TextBlock
        {
            Foreground = Brushes.White,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12
        };
        root.Children.Add(_earliestStartHint);

        _reducedRestCheck = new CheckBox
        {
            Content = "Tägliche Ruhezeit auf 9 Stunden verkürzen (max. 2× pro Kalenderwoche)",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 8, 0, 0),
            IsChecked = isEdit && existing!.ReducedRestBefore
        };
        _reducedRestCheck.Checked += (_, _) => UpdateComplianceHints(autoFillTime: !_isEdit);
        _reducedRestCheck.Unchecked += (_, _) => UpdateComplianceHints(autoFillTime: !_isEdit);
        root.Children.Add(_reducedRestCheck);

        _extendedDrivingCheck = new CheckBox
        {
            Content = "Lenkzeit bis 10 Stunden an diesem Tag (max. 2× pro Kalenderwoche)",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 4, 0, 0),
            IsChecked = isEdit && existing!.ExtendedDrivingDay
        };
        _extendedDrivingCheck.Checked += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _extendedDrivingCheck.Unchecked += (_, _) => UpdateComplianceHints(autoFillTime: false);
        root.Children.Add(_extendedDrivingCheck);

        _reducedWeeklyRestCheck = new CheckBox
        {
            Content = "Wochenruhe auf 24 Stunden verkürzen (max. 3× bis zur nächsten 45-h-Ruhe)",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 4, 0, 0),
            IsChecked = isEdit && existing!.ReducedWeeklyRestBefore
        };
        _reducedWeeklyRestCheck.Checked += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _reducedWeeklyRestCheck.Unchecked += (_, _) => UpdateComplianceHints(autoFillTime: false);
        root.Children.Add(_reducedWeeklyRestCheck);

        _complianceHint = new TextBlock
        {
            Foreground = Brushes.White,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 11
        };
        root.Children.Add(_complianceHint);

        _employeeBox.SelectionChanged += (_, _) => UpdateComplianceHints(autoFillTime: !_isEdit);
        _dateFromField.DateChanged += _ => UpdateComplianceHints(autoFillTime: !_isEdit);
        _dateToField.DateChanged += _ => UpdateComplianceHints(autoFillTime: false);
        _timeFromBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _timeToBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _splitDateField.DateChanged += _ => UpdateComplianceHints(autoFillTime: false);
        _part1FromBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _part1ToBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _part2FromBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);
        _part2ToBox.TextChanged += (_, _) => UpdateComplianceHints(autoFillTime: false);

        _errorText = new TextBlock
        {
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _errorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _errorPanel.Children.Add(new PackIcon
        {
            Kind = PackIconKind.Alert,
            Width = 20,
            Height = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 8, 0)
        });
        _errorPanel.Children.Add(_errorText);
        root.Children.Add(_errorPanel);

        var buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = MakeDialogButton("Abbrechen", isCancel: true);
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var save = MakeDialogButton("Speichern", isDefault: true);
        save.Click += (_, _) =>
        {
            if (!TryParseInput(out var error))
            {
                ShowError(error);
                return;
            }

            if (!DriverDispositionCompliance.TryValidate(
                    _existingAssignments,
                    SelectedDriverKey,
                    StartEpochMs,
                    EndEpochMs,
                    _editingAssignmentId,
                    _reducedRestCheck.IsChecked == true,
                    _extendedDrivingCheck.IsChecked == true,
                    _reducedWeeklyRestCheck.IsChecked == true,
                    out var appliedReducedRest,
                    out var appliedExtendedDriving,
                    out var appliedReducedWeeklyRest,
                    out var complianceError,
                    Part1EndEpochMs,
                    Part2StartEpochMs))
            {
                ShowError(complianceError);
                return;
            }

            ReducedRestBefore = appliedReducedRest;
            ExtendedDrivingDay = appliedExtendedDriving;
            ReducedWeeklyRestBefore = appliedReducedWeeklyRest;
            IsSplitShift = Part1EndEpochMs > 0 && Part2StartEpochMs > Part1EndEpochMs;
            DialogResult = true;
            Close();
        };
        rightButtons.Children.Add(cancel);
        rightButtons.Children.Add(save);
        Grid.SetColumn(rightButtons, 1);
        buttons.Children.Add(rightButtons);

        if (isEdit)
        {
            var delete = MakeDialogButton("Dienst löschen");
            delete.Background = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
            delete.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.Click += (_, _) =>
            {
                DeleteRequested = true;
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(delete, 0);
            buttons.Children.Add(delete);
        }

        root.Children.Add(buttons);

        Content = root;
        UpdateComplianceHints(autoFillTime: !_isEdit);
    }

    private void UpdateComplianceHints(bool autoFillTime)
    {
        if (_employeeBox.SelectedValue is not string driverKey || driverKey.Length == 0)
        {
            _earliestStartHint.Text = string.Empty;
            _complianceHint.Text = DriverDispositionCompliance.DrivingTimeAssumptionHint;
            _reducedRestCheck.IsEnabled = false;
            _extendedDrivingCheck.IsEnabled = false;
            _reducedWeeklyRestCheck.IsEnabled = false;
            return;
        }

        if (_splitShiftCheck.IsChecked == true)
        {
            if (_splitDateField.SelectedDate is not DateTime)
            {
                _earliestStartHint.Text = string.Empty;
                _complianceHint.Text = DriverDispositionCompliance.DrivingTimeAssumptionHint;
                return;
            }
        }
        else if (_dateFromField.SelectedDate is not DateTime dateFrom)
        {
            _earliestStartHint.Text = string.Empty;
            _complianceHint.Text = DriverDispositionCompliance.DrivingTimeAssumptionHint;
            return;
        }

        var referenceDate = _splitShiftCheck.IsChecked == true && _splitDateField.SelectedDate is DateTime splitDate
            ? splitDate.Date
            : _dateFromField.SelectedDate!.Value.Date;
        var useReduced = _reducedRestCheck.IsChecked == true;
        var earliest = DriverDispositionCompliance.GetEarliestAllowedStart(
            _existingAssignments,
            driverKey,
            referenceDate,
            useReduced,
            _editingAssignmentId);

        var usedReduced = DriverDispositionCompliance.CountReducedRestUsesInCalendarWeek(
            _existingAssignments,
            driverKey,
            earliest,
            _editingAssignmentId);
        var canReduce = DriverDispositionCompliance.CanApplyReducedRest(
            _existingAssignments,
            driverKey,
            earliest,
            _editingAssignmentId);

        _reducedRestCheck.IsEnabled = canReduce || (_isEdit && _reducedRestCheck.IsChecked == true);
        if (!canReduce && _reducedRestCheck.IsChecked == true && !_isEdit)
        {
            _reducedRestCheck.IsChecked = false;
            useReduced = false;
            earliest = DriverDispositionCompliance.GetEarliestAllowedStart(
                _existingAssignments,
                driverKey,
                referenceDate,
                false,
                _editingAssignmentId);
        }

        var canExtendDriving = DriverDispositionCompliance.CanApplyExtendedDriving(
            _existingAssignments,
            driverKey,
            referenceDate,
            _editingAssignmentId);
        _extendedDrivingCheck.IsEnabled = canExtendDriving || (_isEdit && _extendedDrivingCheck.IsChecked == true);

        var earliestMs = new DateTimeOffset(earliest).ToUnixTimeMilliseconds();
        var canReduceWeekly = DriverDispositionCompliance.CanApplyReducedWeeklyRest(
            _existingAssignments,
            driverKey,
            earliestMs,
            _editingAssignmentId);
        _reducedWeeklyRestCheck.IsEnabled = canReduceWeekly || (_isEdit && _reducedWeeklyRestCheck.IsChecked == true);

        var restLabel = useReduced
            ? $"{DriverDispositionCompliance.ReducedRestHours} h (verkürzt)"
            : $"{DriverDispositionCompliance.MinimumRestHours} h";
        _earliestStartHint.Text =
            $"Frühester Dienstbeginn: {earliest:dd.MM.yyyy HH:mm} (Ruhezeit {restLabel}) · " +
            $"Tägliche Ruhe 9 h: {usedReduced}/{DriverDispositionCompliance.MaxReducedRestPerCalendarWeek} · " +
            $"Lenkzeit 10 h: {DriverDispositionCompliance.CountExtendedDrivingDaysInCalendarWeek(_existingAssignments, driverKey, referenceDate, _editingAssignmentId)}/{DriverDispositionCompliance.MaxExtendedDrivingDaysPerCalendarWeek}";

        long? previewStartMs = null;
        long? previewEndMs = null;
        if (TryGetPreviewTimes(out var previewStart, out var previewEnd))
        {
            previewStartMs = new DateTimeOffset(previewStart).ToUnixTimeMilliseconds();
            previewEndMs = new DateTimeOffset(previewEnd).ToUnixTimeMilliseconds();
        }

        _complianceHint.Text = DriverDispositionCompliance.BuildComplianceSummary(
            _existingAssignments,
            driverKey,
            previewStartMs,
            previewEndMs,
            _editingAssignmentId,
            GetPreviewPart1EndEpochMs(),
            GetPreviewPart2StartEpochMs());

        if (autoFillTime && _splitShiftCheck.IsChecked != true)
        {
            if (earliest.Date > referenceDate)
            {
                _dateFromField.SetDate(earliest.Date);
                _dateToField.SetDate(earliest.Date);
                UpdateComplianceHints(autoFillTime: true);
                return;
            }

            _timeFromBox.Text = earliest.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    private void ToggleSplitMode(bool isSplit)
    {
        _singleShiftPanel.Visibility = isSplit ? Visibility.Collapsed : Visibility.Visible;
        _splitShiftPanel.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
        _errorPanel.Visibility = Visibility.Collapsed;
        UpdateComplianceHints(autoFillTime: false);
    }

    private bool TryGetPreviewTimes(out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        if (_splitShiftCheck.IsChecked == true)
        {
            if (_splitDateField.SelectedDate is not DateTime splitDate ||
                !TryParseTime(_part1FromBox.Text, out var part1From) ||
                !TryParseTime(_part2ToBox.Text, out var part2To))
            {
                return false;
            }

            start = splitDate.Date.Add(part1From);
            end = splitDate.Date.Add(part2To);
            return end > start;
        }

        if (_dateFromField.SelectedDate is not DateTime dateFrom ||
            _dateToField.SelectedDate is not DateTime dateTo ||
            !TryParseTime(_timeFromBox.Text, out var timeFrom) ||
            !TryParseTime(_timeToBox.Text, out var timeTo))
        {
            return false;
        }

        start = dateFrom.Date.Add(timeFrom);
        end = dateTo.Date.Add(timeTo);
        return end > start;
    }

    private long GetPreviewPart1EndEpochMs()
    {
        if (_splitShiftCheck.IsChecked != true ||
            _splitDateField.SelectedDate is not DateTime splitDate ||
            !TryParseTime(_part1ToBox.Text, out var part1To))
        {
            return 0;
        }

        return new DateTimeOffset(splitDate.Date.Add(part1To)).ToUnixTimeMilliseconds();
    }

    private long GetPreviewPart2StartEpochMs()
    {
        if (_splitShiftCheck.IsChecked != true ||
            _splitDateField.SelectedDate is not DateTime splitDate ||
            !TryParseTime(_part2FromBox.Text, out var part2From))
        {
            return 0;
        }

        return new DateTimeOffset(splitDate.Date.Add(part2From)).ToUnixTimeMilliseconds();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorPanel.Visibility = Visibility.Visible;
    }

    private bool TryParseInput(out string error)
    {
        error = string.Empty;

        if (_employeeBox.SelectedValue is not string driverKey || driverKey.Length == 0)
        {
            error = "Bitte einen Fahrer auswählen.";
            return false;
        }

        SelectedDriverKey = driverKey;
        ShiftName = _shiftNameBox.Text.Trim();
        DisplayLabel = ShiftName;
        Part1EndEpochMs = 0;
        Part2StartEpochMs = 0;

        if (_splitShiftCheck.IsChecked == true)
        {
            if (_splitDateField.SelectedDate is not DateTime splitDate)
            {
                error = "Datum ist ungültig.";
                return false;
            }

            if (!TryParseTime(_part1FromBox.Text, out var part1From) ||
                !TryParseTime(_part1ToBox.Text, out var part1To) ||
                !TryParseTime(_part2FromBox.Text, out var part2From) ||
                !TryParseTime(_part2ToBox.Text, out var part2To))
            {
                error = "Uhrzeiten sind ungültig (Format: HH:mm).";
                return false;
            }

            var part1Start = splitDate.Date.Add(part1From);
            var part1End = splitDate.Date.Add(part1To);
            var part2Start = splitDate.Date.Add(part2From);
            var part2End = splitDate.Date.Add(part2To);

            if (part1End <= part1Start || part2End <= part2Start || part2Start <= part1End)
            {
                error = SplitShiftRules.PartOrderMessage;
                return false;
            }

            StartEpochMs = new DateTimeOffset(part1Start).ToUnixTimeMilliseconds();
            EndEpochMs = new DateTimeOffset(part2End).ToUnixTimeMilliseconds();
            Part1EndEpochMs = new DateTimeOffset(part1End).ToUnixTimeMilliseconds();
            Part2StartEpochMs = new DateTimeOffset(part2Start).ToUnixTimeMilliseconds();
            IsSplitShift = true;
            return true;
        }

        if (_dateFromField.SelectedDate is not DateTime dateFrom)
        {
            error = "Datum von ist ungültig.";
            return false;
        }

        if (_dateToField.SelectedDate is not DateTime dateTo)
        {
            error = "Datum bis ist ungültig.";
            return false;
        }

        if (!TryParseTime(_timeFromBox.Text, out var timeFrom))
        {
            error = "Uhrzeit von ist ungültig (Format: HH:mm).";
            return false;
        }

        if (!TryParseTime(_timeToBox.Text, out var timeTo))
        {
            error = "Uhrzeit bis ist ungültig (Format: HH:mm).";
            return false;
        }

        var start = dateFrom.Date.Add(timeFrom);
        var end = dateTo.Date.Add(timeTo);
        if (end <= start)
        {
            error = "Ende muss nach dem Beginn liegen.";
            return false;
        }

        StartEpochMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        EndEpochMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        IsSplitShift = false;
        return true;
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        time = default;
        var trimmed = text.Trim();
        if (TimeSpan.TryParseExact(trimmed, "hh\\:mm", CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        return TimeSpan.TryParseExact(trimmed, "h\\:mm", CultureInfo.InvariantCulture, out time);
    }

    private static string BuildEmployeeLabel(EmployeeRosterItem employee)
    {
        var name = employee.Name.Trim();
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(personnel))
        {
            return $"{name} (PN {personnel})";
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(employee.PhoneNumber)
            ? "Fahrer ohne Bezeichnung"
            : employee.PhoneNumber;
    }

    private static string FormatDate(DateTime date) =>
        date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 8, 0, 4)
        };

    private static TextBox MakeInput(string placeholder, string initial) =>
        new()
        {
            Text = initial,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x66)),
            ToolTip = placeholder
        };

    private static Button MakeDialogButton(string text, bool isDefault = false, bool isCancel = false) =>
        new()
        {
            Content = text,
            MinWidth = 100,
            MinHeight = 36,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7A, 0xC7)),
            IsDefault = isDefault,
            IsCancel = isCancel
        };

    private static bool TryPickDate(
        Window owner,
        DateTime? current,
        DateTime? pinnedDate,
        out DateTime? selected)
    {
        selected = current;
        var initial = current ?? DateTime.Today;
        DateTime? pickedDate = null;

        var picker = new Window
        {
            Title = "Datum wählen",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = DialogBackground
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Datum wählen",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var calendar = new SimpleMonthCalendar(initial, pinnedDate?.Date);
        root.Children.Add(calendar);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = MakeDialogButton("Abbrechen", isCancel: true);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) =>
        {
            picker.DialogResult = false;
            picker.Close();
        };
        var ok = MakeDialogButton("Übernehmen", isDefault: true);
        ok.Click += (_, _) =>
        {
            pickedDate = calendar.SelectedDate ?? initial.Date;
            picker.DialogResult = true;
            picker.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        picker.Content = root;
        if (picker.ShowDialog() != true)
        {
            return false;
        }

        selected = pickedDate;
        return pickedDate is not null;
    }

    /// <summary>Eigener Monatskalender – unabhängig vom MaterialDesign-Theme (Tage immer sichtbar).</summary>
    private sealed class SimpleMonthCalendar : StackPanel
    {
        private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");
        private static readonly string[] WeekdayHeaders = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];

        private readonly TextBlock _monthLabel;
        private readonly Grid _daysGrid;
        private DateTime _displayMonth;
        private readonly DateTime? _pinnedDate;

        public DateTime? SelectedDate { get; private set; }

        public SimpleMonthCalendar(DateTime initial, DateTime? pinnedDate = null)
        {
            SelectedDate = initial.Date;
            _pinnedDate = pinnedDate?.Date;
            _displayMonth = new DateTime(initial.Year, initial.Month, 1);
            if (_pinnedDate is not null &&
                (_pinnedDate.Value.Year != _displayMonth.Year || _pinnedDate.Value.Month != _displayMonth.Month))
            {
                _displayMonth = new DateTime(_pinnedDate.Value.Year, _pinnedDate.Value.Month, 1);
            }

            Margin = new Thickness(0, 0, 0, 12);

            var nav = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prev = MakeNavButton("◀");
            prev.Click += (_, _) =>
            {
                _displayMonth = _displayMonth.AddMonths(-1);
                RebuildDays();
            };
            Grid.SetColumn(prev, 0);
            nav.Children.Add(prev);

            _monthLabel = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15
            };
            Grid.SetColumn(_monthLabel, 1);
            nav.Children.Add(_monthLabel);

            var next = MakeNavButton("▶");
            next.Click += (_, _) =>
            {
                _displayMonth = _displayMonth.AddMonths(1);
                RebuildDays();
            };
            Grid.SetColumn(next, 2);
            nav.Children.Add(next);
            Children.Add(nav);

            _daysGrid = new Grid();
            Children.Add(_daysGrid);
            RebuildDays();
        }

        private void RebuildDays()
        {
            _monthLabel.Text = _displayMonth.ToString("MMMM yyyy", DeCulture);
            _daysGrid.Children.Clear();
            _daysGrid.RowDefinitions.Clear();
            _daysGrid.ColumnDefinitions.Clear();

            for (var c = 0; c < 7; c++)
            {
                _daysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (var r = 0; r < 7; r++)
            {
                _daysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (var c = 0; c < 7; c++)
            {
                var header = new TextBlock
                {
                    Text = WeekdayHeaders[c],
                    Foreground = Brushes.White,
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 4),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                _daysGrid.Children.Add(header);
            }

            var col = GetMondayBasedColumn(_displayMonth);
            var row = 1;
            var daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
                var isSelected = SelectedDate?.Date == date;
                var isPinned = _pinnedDate?.Date == date;
                var isActive = isSelected || isPinned;
                var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isInRange = false;
                if (_pinnedDate is not null &&
                    SelectedDate is not null &&
                    _pinnedDate.Value.Date != SelectedDate.Value.Date &&
                    _pinnedDate.Value.Date <= SelectedDate.Value.Date)
                {
                    isInRange = date > _pinnedDate.Value.Date && date < SelectedDate.Value.Date;
                }

                var btn = new Button
                {
                    Content = day.ToString(DeCulture),
                    MinWidth = 34,
                    MinHeight = 30,
                    Margin = new Thickness(2),
                    Padding = new Thickness(0),
                    FontSize = 12,
                    Foreground = isActive ? Brushes.White : Brushes.Black,
                    Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E))
                        : isInRange
                            ? new SolidColorBrush(Color.FromArgb(0x88, 0x1E, 0x5A, 0x9E))
                            : Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xCC, 0xDD))
                };
                if (isWeekend && !isActive && !isInRange)
                {
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                }
                if (isInRange)
                {
                    btn.Foreground = Brushes.White;
                }

                btn.Click += (_, _) =>
                {
                    SelectedDate = date;
                    RebuildDays();
                };

                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                _daysGrid.Children.Add(btn);

                col++;
                if (col > 6)
                {
                    col = 0;
                    row++;
                }
            }
        }

        private static int GetMondayBasedColumn(DateTime date) =>
            ((int)date.DayOfWeek + 6) % 7;

        private static Button MakeNavButton(string content) =>
            new()
            {
                Content = content,
                MinWidth = 36,
                MinHeight = 30,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7A, 0xC7)),
                Padding = new Thickness(0)
            };
    }

    private sealed class DialogDateField : Grid
    {
        private readonly TextBlock _dateText;
        private readonly Window _owner;
        private Func<DateTime?>? _getPinnedDate;

        public DateTime? SelectedDate { get; private set; }

        public event Action<DateTime>? DateChanged;

        public DialogDateField(DateTime initial, Window owner, Func<DateTime?>? getPinnedDate = null)
        {
            _owner = owner;
            _getPinnedDate = getPinnedDate;
            SelectedDate = initial.Date;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var displayBorder = new Border
            {
                Background = DateFieldBackground,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x77, 0x99)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                MinHeight = 34,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _dateText = new TextBlock
            {
                Text = FormatDate(initial),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            displayBorder.Child = _dateText;
            displayBorder.MouseLeftButtonUp += (_, _) => OpenPicker();
            Grid.SetColumn(displayBorder, 0);
            Children.Add(displayBorder);

            var pickButton = new Button
            {
                Content = "📅",
                MinWidth = 40,
                MinHeight = 34,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7A, 0xC7)),
                ToolTip = "Datum wählen"
            };
            pickButton.Click += (_, _) => OpenPicker();
            Grid.SetColumn(pickButton, 1);
            Children.Add(pickButton);
        }

        public void SetDate(DateTime date)
        {
            SelectedDate = date.Date;
            _dateText.Text = FormatDate(date);
        }

        public void SetPinnedDateProvider(Func<DateTime?> getPinnedDate) =>
            _getPinnedDate = getPinnedDate;

        private void OpenPicker()
        {
            var pinned = _getPinnedDate?.Invoke()?.Date;
            if (!TryPickDate(_owner, SelectedDate, pinned, out var picked) || picked is null)
            {
                return;
            }

            SetDate(picked.Value);
            DateChanged?.Invoke(picked.Value);
        }
    }

    private static Style CreateComboBoxItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x14, 0x24, 0x3A))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        return style;
    }

    private sealed record EmployeeOption(string Key, string Label);
}
