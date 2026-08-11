using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class AddRouteDialog : Window
{
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;

    private readonly TextBox _routeNameBox;
    private readonly TextBox _lineCourseBox;
    private readonly TextBox _tripNumberBox;
    private readonly TextBox _passengerLineBox;
    private readonly TextBlock _errorText;
    private readonly CheckBox _itcsRouteListCheck;
    private readonly CheckBox _mainDeviceOnlyCheck;
    private readonly List<CheckBox> _operatingDayChecks = [];
    private readonly TextBox _dateFromBox;
    private readonly TextBox _dateToBox;
    private readonly TextBox _operatingDatesBox;
    private readonly IDictionary<string, RouteDateRange> _dateRangesByRoute;
    private readonly IDictionary<string, HashSet<DateOnly>> _operatingDatesByRoute;
    private bool _formattingLineCourse;

    public RouteDefinition? ResultDefinition { get; private set; }
    public IReadOnlyList<DutyOperatingDay> ResultOperatingDays { get; private set; } = [];
    public RouteDateRange? ResultDateRange { get; private set; }
    public IReadOnlyList<DateOnly> ResultOperatingDates { get; private set; } = [];
    public string? CopyStopsFromRouteKey { get; private set; }
    public string? EditingRouteKey { get; }
    public bool ResultItcsRouteListEnabled { get; private set; }
    public bool ResultMainDeviceOnly { get; private set; }

    public AddRouteDialog(EditableRoutePackage package, RouteDefinition? initial = null, string? copyFromRouteKey = null, string? editingRouteKey = null)
        : this(
            package.RouteNames.ToList(),
            package.RouteOperatingDaysByRoute,
            package.RouteDateRangesByRoute,
            package.RouteOperatingDatesByRoute,
            initial,
            copyFromRouteKey,
            editingRouteKey,
            editingRouteKey is not null && package.IsRouteInItcsRouteList(editingRouteKey),
            editingRouteKey is not null && package.IsRouteMainDeviceOnly(editingRouteKey))
    {
    }

    public AddRouteDialog(
        IReadOnlyList<string> existingRoutes,
        IDictionary<string, HashSet<DutyOperatingDay>> operatingDaysByRoute,
        IDictionary<string, RouteDateRange> dateRangesByRoute,
        IDictionary<string, HashSet<DateOnly>> operatingDatesByRoute,
        RouteDefinition? initial = null,
        string? copyFromRouteKey = null,
        string? editingRouteKey = null,
        bool initialItcsRouteListEnabled = false,
        bool initialMainDeviceOnly = false)
    {
        _dateRangesByRoute = dateRangesByRoute;
        _operatingDatesByRoute = operatingDatesByRoute;
        EditingRouteKey = string.IsNullOrWhiteSpace(editingRouteKey) ? null : editingRouteKey.Trim();
        var isEdit = EditingRouteKey is not null;

        Title = isEdit ? "Route bearbeiten" : "Neue Route hinzufügen";
        Width = 460;
        MinWidth = 420;
        MinHeight = 480;
        MaxHeight = SystemParameters.WorkArea.Height * 0.88;
        Height = Math.Min(700, MaxHeight);
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        initial ??= isEdit && EditingRouteKey is not null
            ? RouteDisplayHelper.Parse(EditingRouteKey)
            : new RouteDefinition(string.Empty);
        CopyStopsFromRouteKey = copyFromRouteKey;

        var shell = new DockPanel { Margin = new Thickness(24) };
        shell.Children.Add(new TextBlock
        {
            Text = isEdit ? "Route bearbeiten" : "Neue Route hinzufügen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 16)
        });
        DockPanel.SetDock(shell.Children[^1], Dock.Top);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var add = new Button { Content = isEdit ? "Speichern" : "Hinzufügen", MinWidth = 110, IsDefault = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        add.Click += (_, _) => ConfirmSave(existingRoutes, operatingDaysByRoute);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        shell.Children.Add(buttons);
        DockPanel.SetDock(buttons, Dock.Bottom);

        _errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        shell.Children.Add(_errorText);
        DockPanel.SetDock(_errorText, Dock.Bottom);

        var form = new StackPanel();
        form.Children.Add(MakeLabel("Routenname"));
        _routeNameBox = MakeInput("Routenname (z.B. Hamburg/Berlin)", initial.Name);
        form.Children.Add(_routeNameBox);

        form.Children.Add(MakeLabel("Linie/Kurs"));
        _lineCourseBox = MakeInput(string.Empty, initial.LineCourse);
        _lineCourseBox.TextChanged += (_, _) => FormatLineCourseField();
        form.Children.Add(_lineCourseBox);

        var split = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        split.ColumnDefinitions.Add(new ColumnDefinition());
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        split.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        left.Children.Add(MakeLabel("Fahrtnummer"));
        _tripNumberBox = MakeInput(string.Empty, initial.TripNumber ?? string.Empty);
        left.Children.Add(_tripNumberBox);
        Grid.SetColumn(left, 0);
        split.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(MakeLabel("Linie Fahrgastraum"));
        _passengerLineBox = MakeInput("z.B. RE7", initial.PassengerDisplayLine ?? string.Empty);
        right.Children.Add(_passengerLineBox);
        Grid.SetColumn(right, 2);
        split.Children.Add(right);
        form.Children.Add(split);

        form.Children.Add(MakeLabel("Verkehrstage"));
        form.Children.Add(new TextBlock
        {
            Text = "Gleiche Linie/Kurs + Fahrt nur einmal pro Tag · Betriebstag 03:00–02:59 Folgetag",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var dayWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = true,
                Tag = day,
                Foreground = LabelForeground,
                Margin = new Thickness(0, 0, 12, 4)
            };
            _operatingDayChecks.Add(check);
            dayWrap.Children.Add(check);
        }
        form.Children.Add(dayWrap);
        if (isEdit)
        {
            ApplyOperatingDaysToChecks(
                RouteOperatingDaysEditor.GetDaysForRoute(operatingDaysByRoute, EditingRouteKey!));
        }

        form.Children.Add(MakeLabel("Gültigkeit (optional)"));
        form.Children.Add(new TextBlock
        {
            Text = "Kalenderdatum von/bis – leer = unbegrenzt (TT.MM.JJJJ)",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var dateGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _dateFromBox = MakeInput("Datum von (TT.MM.JJJJ)", string.Empty);
        _dateToBox = MakeInput("Datum bis (TT.MM.JJJJ)", string.Empty);
        Grid.SetColumn(_dateFromBox, 0);
        Grid.SetColumn(_dateToBox, 2);
        dateGrid.Children.Add(_dateFromBox);
        dateGrid.Children.Add(_dateToBox);
        form.Children.Add(dateGrid);
        form.Children.Add(MakeLabel("Einzelne Betriebstage (optional)"));
        form.Children.Add(new TextBlock
        {
            Text = "Mehrere Tage kommagetrennt, z. B. 28.07, 30.07 oder 10.08-14.08, 17.08-19.08 – " +
                   "Route nur an diesen Tagen sichtbar (zusätzlich zu Verkehrstagen und von/bis). Leer = alle Tage im Zeitraum.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _operatingDatesBox = MakeInput("z. B. 28.07, 30.07.2026, 31.07, 07.08", string.Empty);
        _operatingDatesBox.AcceptsReturn = false;
        form.Children.Add(_operatingDatesBox);
        if (isEdit)
        {
            ApplyDateRangeToFields(
                RouteDateRangeEditor.GetRangeForRoute(_dateRangesByRoute, EditingRouteKey!));
            ApplyOperatingDatesToField(
                RouteOperatingDatesEditor.GetDatesForRoute(_operatingDatesByRoute, EditingRouteKey!));
        }

        _itcsRouteListCheck = new CheckBox
        {
            Content = "In ITCS-Routenliste (Route wählen)",
            IsChecked = initialItcsRouteListEnabled,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 4, 0, 4)
        };
        form.Children.Add(_itcsRouteListCheck);
        _mainDeviceOnlyCheck = new CheckBox
        {
            Content = "Route nur für Hauptnutzergeräte",
            IsChecked = initialMainDeviceOnly,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        };
        form.Children.Add(_mainDeviceOnlyCheck);
        form.Children.Add(new TextBlock
        {
            Text = "Hauptnutzer-Routen sind auf Mitarbeitergeräten weder in der ITCS-Routenliste noch in der Linie/Kurs-Suche sichtbar.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var copyButton = new Button
        {
            Content = "Route kopieren",
            MinHeight = 36,
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = isEdit ? Visibility.Collapsed : Visibility.Visible
        };
        copyButton.Click += (_, _) => PickRouteToCopy(existingRoutes, operatingDaysByRoute);
        form.Children.Add(copyButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = form
        };
        shell.Children.Add(scroll);

        Content = shell;
        Loaded += (_, _) => _routeNameBox.Focus();
    }

    private void PickRouteToCopy(
        IReadOnlyList<string> existingRoutes,
        IDictionary<string, HashSet<DutyOperatingDay>> operatingDaysByRoute)
    {
        var routes = existingRoutes
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (routes.Count == 0)
        {
            ShowError("Keine Route zum Kopieren vorhanden.");
            return;
        }

        var picker = new Window
        {
            Title = "Route kopieren",
            Width = 520,
            Height = 420,
            MinWidth = 420,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Background
        };
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = "Quellroute wählen – Haltestellen werden in die neue Route übernommen.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(hint, 0);
        grid.Children.Add(hint);

        var searchBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 32,
            Padding = new Thickness(8, 4, 8, 4),
            Background = InputBackground,
            Foreground = InputForeground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            BorderThickness = new Thickness(1),
            ToolTip = "Suche (Name, Linie/Kurs, Fahrt…)"
        };
        // Platzhalter über Tag + leeren Start – sichtbarer Hinweis als Label darüber
        var searchLabel = new TextBlock
        {
            Text = "Suche (Name, Linie/Kurs, Fahrt…)",
            Foreground = LabelForeground,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        searchPanel.Children.Add(searchLabel);
        searchPanel.Children.Add(searchBox);
        Grid.SetRow(searchPanel, 1);
        grid.Children.Add(searchPanel);

        var list = new ListBox { ItemsSource = routes };
        Grid.SetRow(list, 2);
        grid.Children.Add(list);

        void ApplyRouteFilter()
        {
            var query = (searchBox.Text ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                list.ItemsSource = routes;
                return;
            }

            list.ItemsSource = routes
                .Where(route => route.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        searchBox.TextChanged += (_, _) => ApplyRouteFilter();

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Übernehmen", MinWidth = 110, IsDefault = true };
        cancel.Click += (_, _) => picker.DialogResult = false;
        ok.Click += (_, _) => picker.DialogResult = true;
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        Grid.SetRow(btns, 3);
        grid.Children.Add(btns);
        picker.Content = grid;
        picker.Loaded += (_, _) => searchBox.Focus();

        if (picker.ShowDialog() != true || list.SelectedItem is not string selected)
        {
            return;
        }

        CopyStopsFromRouteKey = selected;
        var parsed = RouteDisplayHelper.Parse(selected);
        _routeNameBox.Text = parsed.Name;
        _lineCourseBox.Text = parsed.LineCourse;
        _tripNumberBox.Text = string.Empty;
        _passengerLineBox.Text = parsed.PassengerDisplayLine;
        ApplyOperatingDaysToChecks(
            RouteOperatingDaysEditor.GetDaysForRoute(operatingDaysByRoute, selected));
        ApplyDateRangeToFields(RouteDateRangeEditor.GetRangeForRoute(_dateRangesByRoute, selected));
        ApplyOperatingDatesToField(RouteOperatingDatesEditor.GetDatesForRoute(_operatingDatesByRoute, selected));
        ShowError($"Haltestellen werden von „{selected}“ kopiert – bitte neue Fahrtnummer, Verkehrstage und Gültigkeit setzen.");
    }

    private void ConfirmSave(
        IReadOnlyList<string> existingRoutes,
        IDictionary<string, HashSet<DutyOperatingDay>> operatingDaysByRoute)
    {
        var name = _routeNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Bitte geben Sie einen Routennamen ein.");
            return;
        }

        var selectedDays = GetSelectedOperatingDays();
        if (selectedDays.Count == 0)
        {
            ShowError("Bitte mindestens einen Verkehrstag auswählen.");
            return;
        }

        var lineCourse = RouteDisplayHelper.NormalizeLineCourse(
            RouteDisplayHelper.FormatLineCourseInput(_lineCourseBox.Text));
        var tripNumber = _tripNumberBox.Text.Trim();
        var passengerLine = _passengerLineBox.Text.Trim();
        var definition = new RouteDefinition(name, lineCourse, tripNumber, passengerLine);
        var displayKey = RouteDisplayHelper.ToDisplayStringWithOperatingDays(definition, selectedDays);
        if (!RouteDateRange.TryParse(_dateFromBox.Text, _dateToBox.Text, out var candidateDateRange))
        {
            ShowError("Ungültiges Datum – bitte TT.MM.JJJJ verwenden (von ≤ bis).");
            return;
        }

        if (!RouteOperatingDatesEditor.TryParseDateList(
                _operatingDatesBox.Text,
                out var candidateOperatingDates,
                out var datesError))
        {
            ShowError(datesError ?? "Ungültige Betriebstage.");
            return;
        }

        var routesToCheck = EditingRouteKey is null
            ? existingRoutes
            : existingRoutes
                .Where(route => !RouteDisplayHelper.RouteKeysMatch(route, EditingRouteKey))
                .ToList();

        if (routesToCheck.Contains(displayKey, StringComparer.Ordinal))
        {
            ShowError("Route schon vorhanden.");
            return;
        }

        if (RouteDisplayHelper.HasRouteScheduleConflict(
                routesToCheck,
                operatingDaysByRoute,
                _dateRangesByRoute,
                definition,
                selectedDays,
                candidateDateRange,
                _operatingDatesByRoute,
                candidateOperatingDates))
        {
            ShowError("Route schon vorhanden (Linie/Kurs, Fahrt, Verkehrstag und/oder Datumsbereich überschneiden sich).");
            return;
        }

        ResultDefinition = definition;
        ResultOperatingDays = selectedDays.ToList();
        ResultDateRange = candidateDateRange.IsRestricted ? candidateDateRange : null;
        ResultOperatingDates = candidateOperatingDates;
        ResultItcsRouteListEnabled = _itcsRouteListCheck.IsChecked == true;
        ResultMainDeviceOnly = _mainDeviceOnlyCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private HashSet<DutyOperatingDay> GetSelectedOperatingDays() =>
        _operatingDayChecks
            .Where(check => check.IsChecked == true && check.Tag is DutyOperatingDay)
            .Select(check => (DutyOperatingDay)check.Tag!)
            .ToHashSet();

    private void ApplyOperatingDaysToChecks(HashSet<DutyOperatingDay> days)
    {
        var effective = RouteOperatingDaysEditor.IsConfiguredForAllDays(days)
            ? RouteOperatingDaysEditor.AllDays.ToHashSet()
            : days;
        foreach (var check in _operatingDayChecks)
        {
            if (check.Tag is DutyOperatingDay day)
            {
                check.IsChecked = effective.Contains(day);
            }
        }
    }

    private void ApplyDateRangeToFields(RouteDateRange range)
    {
        _dateFromBox.Text = range.From is { } from ? RouteDateRange.FormatDate(from) : string.Empty;
        _dateToBox.Text = range.To is { } to ? RouteDateRange.FormatDate(to) : string.Empty;
    }

    private void ApplyOperatingDatesToField(IReadOnlyCollection<DateOnly> dates) =>
        _operatingDatesBox.Text = RouteOperatingDatesEditor.FormatDisplay(dates);

    private void FormatLineCourseField()
    {
        if (_formattingLineCourse)
        {
            return;
        }

        _formattingLineCourse = true;
        var formatted = RouteDisplayHelper.FormatLineCourseInput(_lineCourseBox.Text);
        if (!string.Equals(formatted, _lineCourseBox.Text, StringComparison.Ordinal))
        {
            _lineCourseBox.Text = formatted;
            _lineCourseBox.CaretIndex = formatted.Length;
        }

        _formattingLineCourse = false;
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.Visibility = Visibility.Visible;
    }

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static TextBox MakeInput(string hint, string value)
    {
        var box = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Text = value ?? string.Empty,
            Background = InputBackground,
            Foreground = InputForeground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6)
        };
        if (!string.IsNullOrEmpty(hint))
        {
            box.ToolTip = hint;
        }

        return box;
    }
}
