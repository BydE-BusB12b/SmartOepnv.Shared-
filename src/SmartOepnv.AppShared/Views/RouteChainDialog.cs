using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class RouteChainDialog : Window
{
    private static readonly Brush PanelBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush MutedForeground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));

    private readonly EditableRoutePackage _editor;
    private readonly TextBox _lineCourseBox;
    private readonly TextBox _dateFromBox;
    private readonly TextBox _dateToBox;
    private readonly TextBlock _resultCountText;
    private readonly ListBox _tripList;
    private readonly TextBlock _errorText;
    private readonly StackPanel _chainPanel;

    private IReadOnlyList<RouteChainPlanner.TripCandidate> _trips = [];
    private RouteChainPlanner.ChainCheckFilter _activeFilter = RouteChainPlanner.ChainCheckFilter.None;
    private bool _formattingLineCourse;

    public RouteChainDialog(EditableRoutePackage editor, string? initialLineCourse = null)
    {
        _editor = editor;

        Title = "Routenschnur & Fahrplan";
        Width = 720;
        MinWidth = 640;
        MinHeight = 520;
        Height = 800;
        MaxHeight = 940;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PanelBackground;

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

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
            Text = "Routenschnur & Fahrplan",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Linie/Kurs und Prüfzeitraum (Datum von/bis) eingeben. " +
                   "Fahrten werden nach Verkehrstag und Gültigkeit gefiltert; " +
                   "die Routenschnur löst Folgefahrten für denselben Zeitraum auf.",
            Foreground = MutedForeground,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
            Opacity = 0.95
        });

        root.Children.Add(MakeLabel("Linie/Kurs:"));
        _lineCourseBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 18,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _lineCourseBox.TextChanged += (_, _) => FormatLineCourseField();
        if (!string.IsNullOrWhiteSpace(initialLineCourse))
        {
            _lineCourseBox.Text = initialLineCourse;
        }

        root.Children.Add(_lineCourseBox);

        root.Children.Add(MakeLabel("Prüfzeitraum (Datum von / bis):"));
        root.Children.Add(new TextBlock
        {
            Text = "TT.MM.JJJJ – Verkehrstag aus „Datum von“. Beide Felder erforderlich für die Routenfolge.",
            Foreground = MutedForeground,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayText = RouteDateRange.FormatDate(today);
        var dateGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _dateFromBox = MakeDateInput("von", todayText);
        _dateToBox = MakeDateInput("bis", todayText);
        Grid.SetColumn(_dateFromBox, 0);
        Grid.SetColumn(_dateToBox, 2);
        dateGrid.Children.Add(_dateFromBox);
        dateGrid.Children.Add(_dateToBox);
        root.Children.Add(dateGrid);

        var searchRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var searchButton = new Button
        {
            Content = "Fahrten suchen",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        searchButton.Click += (_, _) => SearchTrips();
        searchRow.Children.Add(searchButton);
        root.Children.Add(searchRow);

        _errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        _resultCountText = new TextBlock
        {
            Foreground = MutedForeground,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(_resultCountText);

        root.Children.Add(MakeLabel("Fahrten (zeitlich sortiert):"));
        _tripList = new ListBox
        {
            MinHeight = 120,
            MaxHeight = 180,
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x24, 0x3A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x77)),
            BorderThickness = new Thickness(1)
        };
        _tripList.SelectionChanged += (_, _) => UpdateChainPreview();
        root.Children.Add(_tripList);

        root.Children.Add(MakeLabel("Verbundene Routenschnur:"));
        _chainPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_chainPanel);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 12, 24, 20)
        };
        var closeButton = new Button
        {
            Content = "Schließen",
            Padding = new Thickness(20, 8, 20, 8),
            IsCancel = true,
            Cursor = Cursors.Hand
        };
        closeButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttonBar.Children.Add(closeButton);
        Grid.SetRow(buttonBar, 1);
        outer.Children.Add(buttonBar);

        Content = outer;

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_lineCourseBox.Text))
            {
                SearchTrips();
            }
        };
    }

    private static TextBox MakeDateInput(string watermarkHint, string initialText) =>
        new()
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Text = initialText,
            ToolTip = $"Datum {watermarkHint} (TT.MM.JJJJ)"
        };

    private void FormatLineCourseField()
    {
        if (_formattingLineCourse)
        {
            return;
        }

        _formattingLineCourse = true;
        var formatted = RouteDisplayHelper.FormatLineCourseInput(_lineCourseBox.Text);
        if (!string.Equals(_lineCourseBox.Text, formatted, StringComparison.Ordinal))
        {
            _lineCourseBox.Text = formatted;
            _lineCourseBox.CaretIndex = _lineCourseBox.Text.Length;
        }

        _formattingLineCourse = false;
    }

    private bool TryReadCheckFilter(out RouteChainPlanner.ChainCheckFilter filter)
    {
        filter = RouteChainPlanner.ChainCheckFilter.None;
        var fromRaw = _dateFromBox.Text?.Trim() ?? string.Empty;
        var toRaw = _dateToBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fromRaw) || string.IsNullOrWhiteSpace(toRaw))
        {
            ShowError("Bitte Datum von und Datum bis eingeben (TT.MM.JJJJ).");
            return false;
        }

        if (!RouteDateRange.TryParse(fromRaw, toRaw, out var range) || !range.IsRestricted)
        {
            ShowError("Ungültiger Prüfzeitraum – Format TT.MM.JJJJ, von ≤ bis.");
            return false;
        }

        filter = new RouteChainPlanner.ChainCheckFilter(range.From, range.To);
        return true;
    }

    private void SearchTrips()
    {
        _errorText.Visibility = Visibility.Collapsed;
        if (!RouteDisplayHelper.TryParseLineCourseUserInput(_lineCourseBox.Text, out var lineCourse))
        {
            ShowError("Bitte Linie/Kurs eingeben (z. B. 12801 oder 128/01).");
            _trips = [];
            _activeFilter = RouteChainPlanner.ChainCheckFilter.None;
            _tripList.ItemsSource = null;
            _resultCountText.Text = string.Empty;
            _chainPanel.Children.Clear();
            return;
        }

        if (!TryReadCheckFilter(out var filter))
        {
            _trips = [];
            _activeFilter = RouteChainPlanner.ChainCheckFilter.None;
            _tripList.ItemsSource = null;
            _resultCountText.Text = string.Empty;
            _chainPanel.Children.Clear();
            return;
        }

        _activeFilter = filter;
        _trips = RouteChainPlanner.FindTripsByLineCourse(_editor, lineCourse, filter);
        if (_trips.Count == 0)
        {
            var period = FormatFilterSummary(filter);
            ShowError($"Keine Fahrten für Linie/Kurs {lineCourse} im Zeitraum {period}.");
            _tripList.ItemsSource = null;
            _resultCountText.Text = "0 Fahrten";
            _chainPanel.Children.Clear();
            return;
        }

        _resultCountText.Text =
            $"{_trips.Count} Fahrt(en) für Linie/Kurs {lineCourse} · {FormatFilterSummary(filter)}";
        _tripList.ItemsSource = _trips.Select(FormatTripListItem).ToList();
        _tripList.SelectedIndex = 0;
        UpdateChainPreview();
    }

    private static string FormatFilterSummary(RouteChainPlanner.ChainCheckFilter filter)
    {
        var rangeText = RouteDateRange.FormatDisplay(filter.AsQueryRange);
        if (filter.ReferenceDate is not { } reference)
        {
            return rangeText;
        }

        var dayName = DutyOperatingDayHelper.GetName(DutyOperatingDayHelper.FromDate(reference));
        return $"{rangeText} ({dayName})";
    }

    private static string FormatTripListItem(RouteChainPlanner.TripCandidate trip)
    {
        var tripNo = RouteDisplayHelper.NormalizeTripNumber(trip.Definition.TripNumber);
        var time = trip.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
        var tripLabel = string.IsNullOrWhiteSpace(tripNo) ? "ohne Fahrtnr." : $"Fahrt {tripNo}";
        return $"{time}  ·  {tripLabel}  ·  {trip.Definition.Name}  ({trip.StopCount} Hst.)";
    }

    private void UpdateChainPreview()
    {
        _chainPanel.Children.Clear();
        if (_tripList.SelectedIndex < 0 || _tripList.SelectedIndex >= _trips.Count)
        {
            _chainPanel.Children.Add(new TextBlock
            {
                Text = "Fahrt auswählen, um die Routenschnur anzuzeigen.",
                Foreground = MutedForeground,
                Opacity = 0.85
            });
            return;
        }

        var selected = _trips[_tripList.SelectedIndex];
        var segments = RouteChainPlanner.BuildChainSchedule(_editor, selected.RouteKey, _activeFilter);
        if (segments.Count == 0)
        {
            _chainPanel.Children.Add(new TextBlock
            {
                Text = "Keine Fahrplandaten für diese Fahrt.",
                Foreground = MutedForeground
            });
            return;
        }

        if (_activeFilter.HasDates)
        {
            _chainPanel.Children.Add(new TextBlock
            {
                Text = $"Auflösung für Prüfzeitraum: {FormatFilterSummary(_activeFilter)}",
                Foreground = AccentBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            _chainPanel.Children.Add(BuildSegmentCard(segment));
            if (i < segments.Count - 1)
            {
                _chainPanel.Children.Add(new TextBlock
                {
                    Text = "↓ Routenwechsel",
                    Foreground = AccentBrush,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 8),
                    FontSize = 12
                });
            }
        }
    }

    private static Border BuildSegmentCard(RouteChainPlanner.ChainSegment segment)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x24, 0x3A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x77)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = $"{segment.Index}. {segment.RouteLabel}",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(segment.StartTimeDisplay))
        {
            meta.Add($"Start {segment.StartTimeDisplay}");
        }

        if (!string.IsNullOrWhiteSpace(segment.OperatingDaysDisplay))
        {
            meta.Add($"Verkehr: {segment.OperatingDaysDisplay}");
        }

        if (!string.IsNullOrWhiteSpace(segment.RouteChangeTo))
        {
            meta.Add($"Routenwechsel → {segment.RouteChangeTo}");
        }

        if (meta.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = string.Join("  ·  ", meta),
                Foreground = MutedForeground,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            });
        }

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerStop = new TextBlock
        {
            Text = "Haltestelle",
            Foreground = MutedForeground,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11
        };
        var headerTime = new TextBlock
        {
            Text = "Zeit",
            Foreground = MutedForeground,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11
        };
        Grid.SetColumn(headerTime, 1);
        grid.Children.Add(headerStop);
        grid.Children.Add(headerTime);

        for (var rowIndex = 0; rowIndex < segment.Stops.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rowIndex + 1;
            var stop = segment.Stops[rowIndex];
            var nameBlock = new TextBlock
            {
                Text = stop.IsRouteChangeStop ? $"{stop.Name}  (Routenwechsel)" : stop.Name,
                Foreground = stop.IsRouteChangeStop ? AccentBrush : Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 8, 0)
            };
            var timeBlock = new TextBlock
            {
                Text = stop.TimeDisplay,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(nameBlock, row);
            Grid.SetRow(timeBlock, row);
            Grid.SetColumn(timeBlock, 1);
            grid.Children.Add(nameBlock);
            grid.Children.Add(timeBlock);
        }

        content.Children.Add(grid);
        card.Child = content;
        return card;
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
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
}
