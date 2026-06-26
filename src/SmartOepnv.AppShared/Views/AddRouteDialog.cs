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
    private readonly List<CheckBox> _operatingDayChecks = [];
    private bool _formattingLineCourse;

    public RouteDefinition? ResultDefinition { get; private set; }
    public IReadOnlyList<DutyOperatingDay> ResultOperatingDays { get; private set; } = [];
    public string? CopyStopsFromRouteKey { get; private set; }
    public string? EditingRouteKey { get; }
    public bool ResultItcsRouteListEnabled { get; private set; }

    public AddRouteDialog(EditableRoutePackage package, RouteDefinition? initial = null, string? copyFromRouteKey = null, string? editingRouteKey = null)
        : this(
            package.RouteNames.ToList(),
            package.RouteOperatingDaysByRoute,
            initial,
            copyFromRouteKey,
            editingRouteKey,
            editingRouteKey is not null && package.IsRouteInItcsRouteList(editingRouteKey))
    {
    }

    public AddRouteDialog(
        IReadOnlyList<string> existingRoutes,
        IDictionary<string, HashSet<DutyOperatingDay>> operatingDaysByRoute,
        RouteDefinition? initial = null,
        string? copyFromRouteKey = null,
        string? editingRouteKey = null,
        bool initialItcsRouteListEnabled = false)
    {
        EditingRouteKey = string.IsNullOrWhiteSpace(editingRouteKey) ? null : editingRouteKey.Trim();
        var isEdit = EditingRouteKey is not null;

        Title = isEdit ? "Route bearbeiten" : "Neue Route hinzufügen";
        Width = 460;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        initial ??= isEdit && EditingRouteKey is not null
            ? RouteDisplayHelper.Parse(EditingRouteKey)
            : new RouteDefinition(string.Empty);
        CopyStopsFromRouteKey = copyFromRouteKey;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = isEdit ? "Route bearbeiten" : "Neue Route hinzufügen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Routenname"));
        _routeNameBox = MakeInput("Routenname (z.B. Hamburg/Berlin)", initial.Name);
        root.Children.Add(_routeNameBox);

        root.Children.Add(MakeLabel("Linie/Kurs"));
        _lineCourseBox = MakeInput(string.Empty, initial.LineCourse);
        _lineCourseBox.TextChanged += (_, _) => FormatLineCourseField();
        root.Children.Add(_lineCourseBox);

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
        root.Children.Add(split);

        root.Children.Add(MakeLabel("Verkehrstage"));
        root.Children.Add(new TextBlock
        {
            Text = "Gleiche Linie/Kurs + Fahrt nur einmal pro Tag · Betriebstag 00:01–03:59 Folgetag",
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
        root.Children.Add(dayWrap);
        if (isEdit)
        {
            ApplyOperatingDaysToChecks(
                RouteOperatingDaysEditor.GetDaysForRoute(operatingDaysByRoute, EditingRouteKey!));
        }

        _itcsRouteListCheck = new CheckBox
        {
            Content = "In ITCS-Routenliste (Route wählen)",
            IsChecked = initialItcsRouteListEnabled,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 4, 0, 4)
        };
        root.Children.Add(_itcsRouteListCheck);
        root.Children.Add(new TextBlock
        {
            Text = "Linie/Kurs-Suche in der App zeigt alle Fahrten unabhängig davon.",
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
        root.Children.Add(copyButton);

        _errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var add = new Button { Content = isEdit ? "Speichern" : "Hinzufügen", MinWidth = 110, IsDefault = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        add.Click += (_, _) => ConfirmSave(existingRoutes, operatingDaysByRoute);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        root.Children.Add(buttons);

        Content = root;
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
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = "Quellroute wählen – Haltestellen werden in die neue Route übernommen.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(hint, 0);
        grid.Children.Add(hint);

        var list = new ListBox { ItemsSource = routes };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

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
        Grid.SetRow(btns, 2);
        grid.Children.Add(btns);
        picker.Content = grid;

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
        ShowError($"Haltestellen werden von „{selected}“ kopiert – bitte neue Fahrtnummer und Verkehrstage setzen.");
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

        if (RouteDisplayHelper.HasOperatingDayConflict(
                routesToCheck,
                operatingDaysByRoute,
                definition,
                selectedDays))
        {
            ShowError("Route schon vorhanden (Linie/Kurs, Fahrt und Verkehrstag überschneiden sich).");
            return;
        }

        ResultDefinition = definition;
        ResultOperatingDays = selectedDays.ToList();
        ResultItcsRouteListEnabled = _itcsRouteListCheck.IsChecked == true;
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
