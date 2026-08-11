using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class FahrzeugdispoNewTripDialog : Window
{
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush DateFieldBackground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    private readonly ComboBox _vehicleBox;
    private readonly TextBox _tripNameBox;
    private readonly DialogDateField _dateFromField;
    private readonly DialogDateField _dateToField;
    private readonly TextBox _timeFromBox;
    private readonly TextBox _timeToBox;
    private readonly StackPanel _errorPanel;
    private readonly TextBlock _errorText;
    private readonly IReadOnlyList<VehicleDispositionAssignment> _existingAssignments;
    private readonly string? _editingAssignmentId;

    public string SelectedPhoneKey { get; private set; } = string.Empty;

    public long StartEpochMs { get; private set; }

    public long EndEpochMs { get; private set; }

    public string DisplayLabel { get; private set; } = string.Empty;

    public string TripName { get; private set; } = string.Empty;

    public bool DeleteRequested { get; private set; }

    public FahrzeugdispoNewTripDialog(
        IReadOnlyList<RegisteredVehicleItem> vehicles,
        DateTime defaultDate,
        VehicleDispositionAssignment? existing = null,
        IReadOnlyList<VehicleDispositionAssignment>? existingAssignments = null)
    {
        _existingAssignments = existingAssignments ?? [];
        _editingAssignmentId = existing?.Id;
        var isEdit = existing is not null;
        var startLocal = isEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.StartEpochMs).LocalDateTime
            : defaultDate;
        var endLocal = isEdit
            ? DateTimeOffset.FromUnixTimeMilliseconds(existing!.EndEpochMs).LocalDateTime
            : defaultDate;

        Title = isEdit ? "Fahrt bearbeiten" : "Neue Fahrt";
        Width = 460;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var options = vehicles
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.PhoneNumber, StringComparer.OrdinalIgnoreCase)
            .Select(v => new VehicleOption(
                RegisteredVehicleDispoKeys.FromVehicle(v),
                BuildVehicleLabel(v)))
            .Where(o => o.Key.Length > 0)
            .ToList();

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = isEdit ? "Fahrt bearbeiten" : "Neue Fahrt",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Fahrzeugauswahl"));
        _vehicleBox = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(VehicleOption.Label),
            SelectedValuePath = nameof(VehicleOption.Key),
            MinHeight = 34,
            MaxDropDownHeight = 320,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(8, 4, 8, 4),
            ItemContainerStyle = CreateComboBoxItemStyle()
        };
        if (isEdit)
        {
            _vehicleBox.SelectedValue = existing!.VehiclePhone;
        }
        else if (options.Count > 0)
        {
            _vehicleBox.SelectedIndex = 0;
        }

        root.Children.Add(_vehicleBox);

        root.Children.Add(MakeLabel("Fahrtname"));
        _tripNameBox = MakeInput("z. B. Linienfahrt Stadt", isEdit ? existing!.Label : string.Empty);
        root.Children.Add(_tripNameBox);

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
        root.Children.Add(dateRow);

        _dateFromField.DateChanged += date => _dateToField.SetDate(date);

        var timeRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());

        var timeFromPanel = new StackPanel();
        timeFromPanel.Children.Add(MakeLabel("Uhrzeit von"));
        _timeFromBox = MakeInput("HH:mm", startLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        AttachTimeNormalization(_timeFromBox);
        timeFromPanel.Children.Add(_timeFromBox);
        Grid.SetColumn(timeFromPanel, 0);
        timeRow.Children.Add(timeFromPanel);

        var timeToPanel = new StackPanel();
        timeToPanel.Children.Add(MakeLabel("Uhrzeit bis"));
        _timeToBox = MakeInput("HH:mm", endLocal.ToString("HH:mm", CultureInfo.InvariantCulture));
        AttachTimeNormalization(_timeToBox);
        timeToPanel.Children.Add(_timeToBox);
        Grid.SetColumn(timeToPanel, 2);
        timeRow.Children.Add(timeToPanel);
        root.Children.Add(timeRow);

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

            if (VehicleDispositionOverlap.HasConflict(
                    _existingAssignments,
                    SelectedPhoneKey,
                    StartEpochMs,
                    EndEpochMs,
                    _editingAssignmentId))
            {
                ShowError(VehicleDispositionOverlap.ConflictMessage);
                return;
            }

            DialogResult = true;
            Close();
        };
        rightButtons.Children.Add(cancel);
        rightButtons.Children.Add(save);
        Grid.SetColumn(rightButtons, 1);
        buttons.Children.Add(rightButtons);

        if (isEdit)
        {
            var delete = MakeDialogButton("Fahrt löschen");
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
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorPanel.Visibility = Visibility.Visible;
    }

    private bool TryParseInput(out string error)
    {
        error = string.Empty;

        if (_vehicleBox.SelectedValue is not string vehicleKey || vehicleKey.Length == 0)
        {
            error = "Bitte ein Fahrzeug auswählen.";
            return false;
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
            error = "Uhrzeit von ist ungültig (z. B. 04:29 oder 429).";
            return false;
        }

        if (!TryParseTime(_timeToBox.Text, out var timeTo))
        {
            error = "Uhrzeit bis ist ungültig (z. B. 04:29 oder 429).";
            return false;
        }

        var start = dateFrom.Date.Add(timeFrom);
        var end = dateTo.Date.Add(timeTo);
        if (end <= start)
        {
            error = "Ende muss nach dem Beginn liegen.";
            return false;
        }

        SelectedPhoneKey = vehicleKey;
        StartEpochMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        EndEpochMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        TripName = _tripNameBox.Text.Trim();
        DisplayLabel = TripName;
        return true;
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        time = default;
        var normalized = RouteScheduleTimeCalculator.NormalizeTimeInput(text);
        if (!RouteScheduleTimeCalculator.TryParseTime(normalized, out var timeOnly))
        {
            return false;
        }

        time = timeOnly.ToTimeSpan();
        return true;
    }

    private static void AttachTimeNormalization(TextBox box)
    {
        box.LostFocus += (_, _) =>
        {
            var normalized = RouteScheduleTimeCalculator.NormalizeTimeInput(box.Text);
            if (!string.Equals(box.Text, normalized, StringComparison.Ordinal))
            {
                box.Text = normalized;
            }
        };
    }

    private static string BuildVehicleLabel(RegisteredVehicleItem vehicle)
    {
        var name = vehicle.Name.Trim();
        var type = vehicle.PlannerDetails.VehicleType.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type))
        {
            return $"{name} – {type}";
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (!string.IsNullOrEmpty(type))
        {
            return type;
        }

        return string.IsNullOrWhiteSpace(vehicle.PhoneNumber)
            ? "Fahrzeug ohne Bezeichnung"
            : vehicle.PhoneNumber;
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

    private sealed record VehicleOption(string Key, string Label);
}
