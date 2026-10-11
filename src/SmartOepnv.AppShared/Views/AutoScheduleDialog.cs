using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class AutoScheduleDialog : Window
{
    private static readonly Brush PanelBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

    private readonly EditableRoutePackage _editor;
    private readonly IReadOnlyList<string> _routes;
    private readonly TextBlock _routeButtonText;
    private readonly ListBox _routeList;
    private readonly TextBox _startTimeBox;
    private readonly TextBox _tripCountBox;
    private readonly TextBox _intervalBox;
    private readonly CheckBox _directionSwitch;
    private readonly TextBlock _previewText;
    private readonly TextBlock _tripCounterText;
    private readonly TextBox _tripNumberBox;
    private readonly Button _prevTripButton;
    private readonly Button _nextTripButton;
    private readonly TextBlock _errorText;

    private readonly List<string> _tripNumbers = [];

    private string? _selectedRoute;
    private int _currentTripIndex;
    private bool _routeDropdownOpen;

    public AutoScheduleDialog(EditableRoutePackage editor, string? initialRouteKey = null)
    {
        _editor = editor;
        _routes = AutoSchedulePlanner.GetSortedTemplateRoutes(editor);
        if (_routes.Count == 0)
        {
            throw new InvalidOperationException("Keine Routen als Vorlage verfügbar");
        }

        _selectedRoute = string.IsNullOrWhiteSpace(initialRouteKey) ||
                         !_routes.Contains(initialRouteKey, StringComparer.Ordinal)
            ? _routes[0]
            : initialRouteKey;

        Title = "Automatische Fahrplanerstellung";
        Width = 560;
        MinWidth = 520;
        MinHeight = 480;
        Height = 680;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PanelBackground;

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0)
        };
        var root = new StackPanel { Margin = new Thickness(24, 24, 20, 12) };
        scroll.Content = root;
        Grid.SetRow(scroll, 0);
        outer.Children.Add(scroll);

        root.Children.Add(new TextBlock
        {
            Text = "Automatische Fahrplanerstellung",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        });

        root.Children.Add(MakeLabel("Vorlagen-Route:"));
        root.Children.Add(new TextBlock
        {
            Text = "Die Vorlagen-Fahrtnummer wird nicht erneut angelegt – es werden nur neue Fahrten erstellt.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.9
        });
        var routePicker = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        routePicker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        routePicker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var routeButton = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var routeButtonGrid = new Grid();
        routeButtonGrid.ColumnDefinitions.Add(new ColumnDefinition());
        routeButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _routeButtonText = new TextBlock
        {
            Text = FormatRouteButtonText(_selectedRoute),
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_routeButtonText, 0);
        routeButtonGrid.Children.Add(_routeButtonText);
        routeButtonGrid.Children.Add(new TextBlock
        {
            Text = "▼",
            Foreground = Brushes.White,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        routeButton.Child = routeButtonGrid;
        routeButton.MouseLeftButtonUp += (_, _) => ToggleRouteDropdown();
        Grid.SetRow(routeButton, 0);
        routePicker.Children.Add(routeButton);

        _routeList = new ListBox
        {
            MaxHeight = 220,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x21, 0x71)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 0)
        };
        foreach (var route in _routes)
        {
            var stopCount = AutoSchedulePlanner.CountStopsForRoute(_editor, route);
            _routeList.Items.Add(new RouteListItem(route, stopCount));
        }

        _routeList.SelectionChanged += (_, _) =>
        {
            if (_routeList.SelectedItem is RouteListItem item)
            {
                _selectedRoute = item.RouteKey;
                _routeButtonText.Text = FormatRouteButtonText(_selectedRoute);
                _routeList.Visibility = Visibility.Collapsed;
                _routeDropdownOpen = false;
                _currentTripIndex = 0;
                if (TryReadTripCount(out var count))
                {
                    EnsureTripNumberSlots(count);
                    ApplyTripNumberSuggestions(overwriteAll: true);
                }

                UpdatePreview();
            }
        };
        Grid.SetRow(_routeList, 1);
        routePicker.Children.Add(_routeList);
        root.Children.Add(routePicker);

        var timeRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition());

        var startPanel = new StackPanel();
        startPanel.Children.Add(MakeLabel("Startzeit"));
        _startTimeBox = MakeInput("HH:mm");
        _startTimeBox.IsReadOnly = true;
        _startTimeBox.Cursor = System.Windows.Input.Cursors.Hand;
        _startTimeBox.MouseLeftButtonUp += (_, _) => PickStartTime();
        startPanel.Children.Add(_startTimeBox);
        Grid.SetColumn(startPanel, 0);
        timeRow.Children.Add(startPanel);

        var tripPanel = new StackPanel();
        tripPanel.Children.Add(MakeLabel("Fahrten"));
        _tripCountBox = MakeInput("3");
        _tripCountBox.TextChanged += (_, _) => OnScheduleInputChanged();
        tripPanel.Children.Add(_tripCountBox);
        Grid.SetColumn(tripPanel, 2);
        timeRow.Children.Add(tripPanel);

        var intervalPanel = new StackPanel();
        intervalPanel.Children.Add(MakeLabel("Intervall"));
        _intervalBox = MakeInput("60");
        _intervalBox.TextChanged += (_, _) => OnScheduleInputChanged();
        intervalPanel.Children.Add(_intervalBox);
        Grid.SetColumn(intervalPanel, 4);
        timeRow.Children.Add(intervalPanel);
        root.Children.Add(timeRow);

        root.Children.Add(MakeLabel("Richtung:"));
        var directionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        directionRow.Children.Add(new TextBlock
        {
            Text = "Richtung A (gerade)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        _directionSwitch = new CheckBox
        {
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        _directionSwitch.Checked += (_, _) => OnScheduleInputChanged();
        _directionSwitch.Unchecked += (_, _) => OnScheduleInputChanged();
        directionRow.Children.Add(_directionSwitch);
        directionRow.Children.Add(new TextBlock
        {
            Text = "Richtung B (ungerade)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        });
        root.Children.Add(directionRow);

        var tripNumberRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        tripNumberRow.ColumnDefinitions.Add(new ColumnDefinition());
        tripNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tripNumberPanel = new StackPanel();
        tripNumberPanel.Children.Add(MakeLabel("Fahrtnummer (4-stellig, pro Fahrt):"));
        _tripNumberBox = MakeInput(string.Empty);
        _tripNumberBox.MaxLength = AutoScheduleTripNumber.DigitCount;
        _tripNumberBox.PreviewTextInput += TripNumberBox_PreviewTextInput;
        _tripNumberBox.TextChanged += (_, _) =>
        {
            SaveCurrentTripNumber();
            UpdatePreview();
        };
        tripNumberPanel.Children.Add(_tripNumberBox);
        Grid.SetColumn(tripNumberPanel, 0);
        tripNumberRow.Children.Add(tripNumberPanel);

        var suggestButton = new Button
        {
            Content = "Vorschlag",
            MinWidth = 96,
            MinHeight = 34,
            Margin = new Thickness(8, 22, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            ToolTip = "Fahrtnummern automatisch vorschlagen (Richtung A/B)"
        };
        suggestButton.Click += (_, _) =>
        {
            ApplyTripNumberSuggestions(overwriteAll: true);
            UpdatePreview();
        };
        Grid.SetColumn(suggestButton, 1);
        tripNumberRow.Children.Add(suggestButton);
        root.Children.Add(tripNumberRow);

        root.Children.Add(new TextBlock
        {
            Text = "Fahrplan-Vorschau:",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var navRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _prevTripButton = new Button
        {
            Content = "◀",
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false
        };
        _prevTripButton.Click += (_, _) =>
        {
            if (_currentTripIndex > 0)
            {
                SaveCurrentTripNumber();
                _currentTripIndex--;
                LoadCurrentTripNumber();
                UpdatePreview();
            }
        };
        _tripCounterText = new TextBlock
        {
            Text = "Fahrt 1 von 3",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        _nextTripButton = new Button
        {
            Content = "▶",
            Width = 40,
            Height = 40,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        _nextTripButton.Click += (_, _) =>
        {
            if (TryReadTripCount(out var tripCount) && _currentTripIndex < tripCount - 1)
            {
                SaveCurrentTripNumber();
                _currentTripIndex++;
                LoadCurrentTripNumber();
                UpdatePreview();
            }
        };
        navRow.Children.Add(_prevTripButton);
        navRow.Children.Add(_tripCounterText);
        navRow.Children.Add(_nextTripButton);
        root.Children.Add(navRow);

        _previewText = new TextBlock
        {
            Text = "Vorschau wird angezeigt, wenn alle Felder ausgefüllt sind...",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x24, 0x3A)),
            Padding = new Thickness(12)
        };
        root.Children.Add(_previewText);

        var footer = new StackPanel
        {
            Margin = new Thickness(24, 0, 24, 24)
        };
        _errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed
        };
        footer.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var create = new Button
        {
            Content = "Fahrplan erstellen",
            MinWidth = 140,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
            Foreground = Brushes.White
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        create.Click += (_, _) => CreateSchedule();
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 1);
        outer.Children.Add(footer);

        Content = outer;
        Loaded += (_, _) =>
        {
            WindowTitleBarHelper.ApplyDarkWindowBackground(this);
            WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
            OnScheduleInputChanged();
        };
    }

    private static void TripNumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void SaveCurrentTripNumber()
    {
        if (_currentTripIndex < 0 || _currentTripIndex >= _tripNumbers.Count)
        {
            return;
        }

        _tripNumbers[_currentTripIndex] = _tripNumberBox.Text.Trim();
    }

    private void LoadCurrentTripNumber()
    {
        if (_currentTripIndex < 0 || _currentTripIndex >= _tripNumbers.Count)
        {
            _tripNumberBox.Text = string.Empty;
            return;
        }

        _tripNumberBox.Text = _tripNumbers[_currentTripIndex];
    }

    private void EnsureTripNumberSlots(int count)
    {
        while (_tripNumbers.Count < count)
        {
            _tripNumbers.Add(string.Empty);
        }

        while (_tripNumbers.Count > count)
        {
            _tripNumbers.RemoveAt(_tripNumbers.Count - 1);
        }
    }

    private void ApplyTripNumberSuggestions(bool overwriteAll)
    {
        if (string.IsNullOrWhiteSpace(_selectedRoute) || !TryReadTripCount(out var count))
        {
            return;
        }

        EnsureTripNumberSlots(count);
        var suggested = AutoSchedulePlanner.SuggestTripNumbers(
            _editor,
            _selectedRoute,
            count,
            _directionSwitch.IsChecked == true);
        for (var i = 0; i < count; i++)
        {
            if (overwriteAll || string.IsNullOrWhiteSpace(_tripNumbers[i]))
            {
                _tripNumbers[i] = suggested[i];
            }
        }

        LoadCurrentTripNumber();
    }

    public string? CreatedFirstRouteKey { get; private set; }

    private void ToggleRouteDropdown()
    {
        _routeDropdownOpen = !_routeDropdownOpen;
        _routeList.Visibility = _routeDropdownOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PickStartTime()
    {
        var dialog = new ScheduleTimePickerDialog(_startTimeBox.Text) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedTime))
        {
            _startTimeBox.Text = dialog.SelectedTime;
            _currentTripIndex = 0;
            UpdatePreview();
        }
    }

    private void OnScheduleInputChanged()
    {
        SaveCurrentTripNumber();
        var previousCount = _tripNumbers.Count;
        if (TryReadTripCount(out var count))
        {
            var countChanged = count != previousCount;
            EnsureTripNumberSlots(count);
            ApplyTripNumberSuggestions(overwriteAll: false);
            if (countChanged)
            {
                _currentTripIndex = 0;
            }
            else if (_currentTripIndex >= count)
            {
                _currentTripIndex = Math.Max(0, count - 1);
            }
        }

        LoadCurrentTripNumber();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _errorText.Visibility = Visibility.Collapsed;
        try
        {
            SaveCurrentTripNumber();
            if (!TryBuildRequest(out var request, out _))
            {
                _previewText.Text = "Vorschau wird angezeigt, wenn alle Felder ausgefüllt sind...";
                _tripCounterText.Text = TryReadTripCount(out var count)
                    ? $"Fahrt {Math.Min(_currentTripIndex + 1, count)} von {count}"
                    : "Fahrt 1 von ?";
                _prevTripButton.IsEnabled = _currentTripIndex > 0;
                _nextTripButton.IsEnabled = TryReadTripCount(out count) && _currentTripIndex < count - 1;
                return;
            }

            _tripCounterText.Text = $"Fahrt {_currentTripIndex + 1} von {request.TripCount}";
            _prevTripButton.IsEnabled = _currentTripIndex > 0;
            _nextTripButton.IsEnabled = _currentTripIndex < request.TripCount - 1;
            _previewText.Text = AutoSchedulePlanner.TryBuildPreview(_editor, request, _currentTripIndex)
                                ?? "Vorschau nicht verfügbar.";
        }
        catch (Exception ex)
        {
            _previewText.Text = "Vorschau nicht verfügbar.";
            _errorText.Text = ex.Message;
            _errorText.Visibility = Visibility.Visible;
        }
    }

    private void CreateSchedule()
    {
        SaveCurrentTripNumber();
        if (!TryBuildRequest(out var request, out var error))
        {
            _errorText.Text = error ?? "Bitte alle Felder ausfüllen.";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        if (!AutoSchedulePlanner.TryValidateRequest(_editor, request, out error))
        {
            _errorText.Text = error ?? "Eingaben ungültig.";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            CreatedFirstRouteKey = AutoSchedulePlanner.CreateSchedule(_editor, request);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
            _errorText.Visibility = Visibility.Visible;
        }
    }

    private bool TryBuildRequest(out AutoSchedulePlanner.Request request, out string? error)
    {
        request = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(_selectedRoute))
        {
            error = "Bitte eine Vorlagen-Route wählen.";
            return false;
        }

        var startTime = _startTimeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(startTime))
        {
            error = "Bitte Startzeit wählen.";
            return false;
        }

        if (!TryReadTripCount(out var tripCount))
        {
            error = "Ungültige Anzahl Fahrten.";
            return false;
        }

        if (!int.TryParse(_intervalBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) ||
            interval <= 0)
        {
            error = "Ungültiges Intervall.";
            return false;
        }

        var tripNumbers = _tripNumbers
            .Take(tripCount)
            .Select(n => n.Trim())
            .ToList();
        while (tripNumbers.Count < tripCount)
        {
            tripNumbers.Add(string.Empty);
        }

        request = new AutoSchedulePlanner.Request(
            _selectedRoute,
            startTime,
            tripCount,
            interval,
            tripNumbers);
        return true;
    }

    private bool TryReadTripCount(out int tripCount)
    {
        tripCount = 0;
        return int.TryParse(_tripCountBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tripCount) &&
               tripCount > 0;
    }

    private static string FormatRouteButtonText(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return "Route auswählen...";
        }

        return routeKey.Length > 40 ? routeKey[..37] + "..." : routeKey;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBox MakeInput(string text) => new()
    {
        Text = text,
        MinHeight = 34,
        Margin = new Thickness(0, 0, 0, 0),
        Background = InputBackground,
        Foreground = InputForeground
    };

    private sealed record RouteListItem(string RouteKey, int StopCount)
    {
        public override string ToString() =>
            StopCount > 0 ? $"{RouteKey}\n{StopCount} Haltestellen" : $"{RouteKey}\n0 Haltestellen";
    }
}

internal sealed class ScheduleTimePickerDialog : Window
{
    private readonly ComboBox _hourBox;
    private readonly ComboBox _minuteBox;

    public string? SelectedTime { get; private set; }

    public ScheduleTimePickerDialog(string? initialTime)
    {
        Title = "Startzeit wählen";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        var initialHour = 6;
        var initialMinute = 0;
        if (RouteScheduleTimeCalculator.TryParseTime(initialTime, out var parsed))
        {
            initialHour = parsed.Hour;
            initialMinute = parsed.Minute;
        }

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Startzeit",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        _hourBox = CreateTimeComboBox();
        for (var h = 0; h < 24; h++)
        {
            _hourBox.Items.Add(h.ToString("00", CultureInfo.InvariantCulture));
        }

        _hourBox.SelectedItem = initialHour.ToString("00", CultureInfo.InvariantCulture);

        _minuteBox = CreateTimeComboBox();
        for (var m = 0; m < 60; m++)
        {
            _minuteBox.Items.Add(m.ToString("00", CultureInfo.InvariantCulture));
        }

        _minuteBox.SelectedItem = initialMinute.ToString("00", CultureInfo.InvariantCulture);

        Grid.SetColumn(_hourBox, 0);
        row.Children.Add(_hourBox);
        row.Children.Add(new TextBlock
        {
            Text = ":",
            Foreground = Brushes.White,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        });
        Grid.SetColumn(_minuteBox, 2);
        row.Children.Add(_minuteBox);
        root.Children.Add(row);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5))
        };
        var ok = new Button
        {
            Content = "OK",
            MinWidth = 80,
            IsDefault = true,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5))
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        ok.Click += (_, _) =>
        {
            var hour = _hourBox.SelectedItem?.ToString() ?? "00";
            var minute = _minuteBox.SelectedItem?.ToString() ?? "00";
            SelectedTime = $"{hour}:{minute}";
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }

    private static ComboBox CreateTimeComboBox()
    {
        var combo = new ComboBox { MinHeight = 34 };
        if (Application.Current?.TryFindResource("SmartDarkComboBox") is Style darkStyle)
        {
            combo.Style = darkStyle;
        }
        else
        {
            combo.Foreground = Brushes.White;
            combo.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));
            combo.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        }

        return combo;
    }
}
