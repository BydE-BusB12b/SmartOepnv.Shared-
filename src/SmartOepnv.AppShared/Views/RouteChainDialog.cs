using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Pdf;
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
    /// <summary>Optional: Anzuzeigende Route nach Änderung (z. B. erste kopierte Fahrt).</summary>
    private readonly Action<string?>? _onPackageChanged;
    private readonly TextBox _lineCourseBox;
    private readonly TextBox _dateFromBox;
    private readonly TextBox _dateToBox;
    private readonly TextBlock _resultCountText;
    private readonly TextBlock _validityText;
    private readonly Button _changeValidityButton;
    private readonly Button _copyChainButton;
    private readonly Button _exportPdfButton;
    private readonly ListBox _tripList;
    private readonly TextBlock _errorText;
    private readonly StackPanel _chainPanel;

    private IReadOnlyList<RouteChainPlanner.TripCandidate> _trips = [];
    private RouteChainPlanner.ChainCheckFilter _activeFilter = RouteChainPlanner.ChainCheckFilter.None;
    private bool _formattingLineCourse;

    public RouteChainDialog(
        EditableRoutePackage editor,
        string? initialLineCourse = null,
        Action<string?>? onPackageChanged = null)
    {
        _editor = editor;
        _onPackageChanged = onPackageChanged;

        Title = "Routenschnur & Fahrplan";
        Width = 900;
        MinWidth = 720;
        MinHeight = 480;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height * 0.92);
        Height = Math.Min(780, MaxHeight);
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PanelBackground;

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        var outer = new Grid { Margin = new Thickness(16, 12, 16, 10) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Titel
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Suche + Aktionen
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ergebnistext
        outer.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(0.45, GridUnitType.Star),
            MinHeight = 90
        }); // Fahrten (kompakt)
        outer.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1.55, GridUnitType.Star),
            MinHeight = 200
        }); // Schnur (Hauptfläche)
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Schließen

        var title = new TextBlock
        {
            Text = "Routenschnur & Fahrplan",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        outer.Children.Add(title);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayText = RouteDateRange.FormatDate(today);
        _lineCourseBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Linie/Kurs (z. B. 128/01)"
        };
        _lineCourseBox.TextChanged += (_, _) => FormatLineCourseField();
        if (!string.IsNullOrWhiteSpace(initialLineCourse))
        {
            _lineCourseBox.Text = initialLineCourse;
        }

        _dateFromBox = MakeDateInput("von", todayText);
        _dateToBox = MakeDateInput("bis", todayText);
        _dateFromBox.VerticalAlignment = VerticalAlignment.Center;
        _dateToBox.VerticalAlignment = VerticalAlignment.Center;
        _dateFromBox.Padding = new Thickness(8, 6, 8, 6);
        _dateToBox.Padding = new Thickness(8, 6, 8, 6);

        var searchButton = new Button
        {
            Content = "Suchen",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Fahrten für Linie/Kurs und Prüfzeitraum suchen"
        };
        searchButton.Click += (_, _) => SearchTrips();

        _changeValidityButton = new Button
        {
            Content = "Gültigkeit ändern",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = "Verkehrstage und Gültigkeit von/bis für alle Fahrten der ausgewählten Routenschnur setzen"
        };
        _changeValidityButton.Click += (_, _) => ChangeChainValidity();

        _copyChainButton = new Button
        {
            Content = "Routenschnur kopieren",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = "Komplette Routenschnur mit neuer Linie/Kurs, Verkehrstagen und Datum kopieren"
        };
        _copyChainButton.Click += (_, _) => CopySelectedChain();

        _exportPdfButton = new Button
        {
            Content = "PDF",
            Padding = new Thickness(10, 6, 10, 6),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = "Routenschnur als PDF speichern (Darstellung wie in diesem Dialog)"
        };
        _exportPdfButton.Click += (_, _) => ExportSelectedChainPdf();

        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lineLabel = MakeLabel("Linie/Kurs:");
        lineLabel.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetRow(lineLabel, 0);
        Grid.SetColumn(lineLabel, 0);
        searchGrid.Children.Add(lineLabel);

        var fromLabel = MakeLabel("Datum von:");
        fromLabel.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetRow(fromLabel, 0);
        Grid.SetColumn(fromLabel, 2);
        searchGrid.Children.Add(fromLabel);

        var toLabel = MakeLabel("Datum bis:");
        toLabel.Margin = new Thickness(0, 0, 0, 2);
        Grid.SetRow(toLabel, 0);
        Grid.SetColumn(toLabel, 4);
        searchGrid.Children.Add(toLabel);

        Grid.SetRow(_lineCourseBox, 1);
        Grid.SetColumn(_lineCourseBox, 0);
        searchGrid.Children.Add(_lineCourseBox);
        Grid.SetRow(_dateFromBox, 1);
        Grid.SetColumn(_dateFromBox, 2);
        searchGrid.Children.Add(_dateFromBox);
        Grid.SetRow(_dateToBox, 1);
        Grid.SetColumn(_dateToBox, 4);
        searchGrid.Children.Add(_dateToBox);

        var actionColumn = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 0)
        };
        actionColumn.Children.Add(searchButton);
        actionColumn.Children.Add(_changeValidityButton);
        actionColumn.Children.Add(_copyChainButton);
        actionColumn.Children.Add(_exportPdfButton);
        Grid.SetRow(actionColumn, 1);
        Grid.SetColumn(actionColumn, 5);
        searchGrid.Children.Add(actionColumn);

        Grid.SetRow(searchGrid, 1);
        outer.Children.Add(searchGrid);

        _errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed
        };
        _resultCountText = new TextBlock
        {
            Foreground = MutedForeground,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        _validityText = new TextBlock
        {
            Foreground = AccentBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var resultPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        resultPanel.Children.Add(_errorText);
        resultPanel.Children.Add(_resultCountText);
        resultPanel.Children.Add(_validityText);
        Grid.SetRow(resultPanel, 2);
        outer.Children.Add(resultPanel);

        var tripsBlock = new DockPanel { LastChildFill = true };
        var tripsLabel = MakeLabel("Fahrten (zeitlich sortiert):");
        tripsLabel.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(tripsLabel, Dock.Top);
        tripsBlock.Children.Add(tripsLabel);
        _tripList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x24, 0x3A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x77)),
            BorderThickness = new Thickness(1)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_tripList, ScrollBarVisibility.Auto);
        _tripList.SelectionChanged += (_, _) =>
        {
            UpdateChainPreview();
            UpdateValiditySummary();
        };
        tripsBlock.Children.Add(_tripList);
        Grid.SetRow(tripsBlock, 3);
        outer.Children.Add(tripsBlock);

        var chainBlock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        var chainLabel = MakeLabel("Verbundene Routenschnur:");
        chainLabel.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(chainLabel, Dock.Top);
        chainBlock.Children.Add(chainLabel);
        _chainPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var chainScroll = new ScrollViewer
        {
            Content = _chainPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x1C, 0x30)),
            Padding = new Thickness(8, 6, 4, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        chainBlock.Children.Add(chainScroll);
        Grid.SetRow(chainBlock, 4);
        outer.Children.Add(chainBlock);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 2)
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
        Grid.SetRow(buttonBar, 5);
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
            ClearValidityUi();
            _chainPanel.Children.Clear();
            return;
        }

        if (!TryReadCheckFilter(out var filter))
        {
            _trips = [];
            _activeFilter = RouteChainPlanner.ChainCheckFilter.None;
            _tripList.ItemsSource = null;
            _resultCountText.Text = string.Empty;
            ClearValidityUi();
            _chainPanel.Children.Clear();
            return;
        }

        _activeFilter = filter;
        RouteChainPlanner.RemapRouteChangeLinksOntoLineCourse(_editor, lineCourse);
        _trips = RouteChainPlanner.FindTripsByLineCourse(_editor, lineCourse, filter);
        if (_trips.Count == 0)
        {
            var period = FormatFilterSummary(filter);
            ShowError($"Keine Fahrten für Linie/Kurs {lineCourse} im Zeitraum {period}.");
            _tripList.ItemsSource = null;
            _resultCountText.Text = "0 Fahrten";
            ClearValidityUi();
            _chainPanel.Children.Clear();
            return;
        }

        _resultCountText.Text =
            $"{_trips.Count} Fahrt(en) für Linie/Kurs {lineCourse} · Prüfzeitraum {FormatFilterSummary(filter)}";
        _tripList.ItemsSource = _trips.Select(FormatTripListItem).ToList();
        _tripList.SelectedIndex = 0;
        UpdateChainPreview();
        UpdateValiditySummary();
    }

    private void ClearValidityUi()
    {
        _validityText.Text = string.Empty;
        _changeValidityButton.Visibility = Visibility.Collapsed;
        _copyChainButton.Visibility = Visibility.Collapsed;
        _exportPdfButton.Visibility = Visibility.Collapsed;
    }

    private IReadOnlyList<string> GetSelectedChainRouteKeys()
    {
        if (_tripList.SelectedIndex < 0 || _tripList.SelectedIndex >= _trips.Count)
        {
            return [];
        }

        var selected = _trips[_tripList.SelectedIndex];
        return RouteChainPlanner.BuildConnectedRouteChain(_editor, selected.RouteKey, _activeFilter);
    }

    private void UpdateValiditySummary()
    {
        var chainKeys = GetSelectedChainRouteKeys();
        if (chainKeys.Count == 0)
        {
            ClearValidityUi();
            return;
        }

        _changeValidityButton.Visibility = Visibility.Visible;
        _changeValidityButton.IsEnabled = true;
        _copyChainButton.Visibility = Visibility.Visible;
        _copyChainButton.IsEnabled = true;
        _exportPdfButton.Visibility = Visibility.Visible;
        _exportPdfButton.IsEnabled = true;

        var dateLabels = chainKeys
            .Select(FormatRouteValidityDate)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var dayLabels = chainKeys
            .Select(key =>
            {
                var days = _editor.GetRouteOperatingDays(key);
                return RouteOperatingDaysEditor.IsConfiguredForAllDays(days)
                    ? "alle Tage"
                    : DutyOperatingDayHelper.FormatDisplay(days);
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var datePart = dateLabels.Count == 1
            ? dateLabels[0]
            : $"unterschiedlich (Startfahrt: {FormatRouteValidityDate(chainKeys[0])})";
        var daysPart = dayLabels.Count == 1
            ? dayLabels[0]
            : $"unterschiedlich (Startfahrt: {FormatRouteValidityDays(chainKeys[0])})";

        _validityText.Text =
            $"Gültigkeit der Schnur ({chainKeys.Count} Fahrt/en): {datePart} · Verkehrstage: {daysPart}";
    }

    private string FormatRouteValidityDate(string routeKey)
    {
        var rangeText = RouteDateRange.FormatDisplay(_editor.GetRouteDateRange(routeKey));
        var operatingSummary = RouteOperatingDatesEditor.FormatSummary(
            _editor.GetRouteOperatingDates(routeKey));

        if (!string.IsNullOrWhiteSpace(rangeText) && !string.IsNullOrWhiteSpace(operatingSummary))
        {
            return $"{rangeText} · Betriebstage: {operatingSummary}";
        }

        if (!string.IsNullOrWhiteSpace(rangeText))
        {
            return rangeText;
        }

        if (!string.IsNullOrWhiteSpace(operatingSummary))
        {
            return $"Betriebstage: {operatingSummary}";
        }

        return "unbegrenzt";
    }

    private string FormatRouteValidityDays(string routeKey)
    {
        var days = _editor.GetRouteOperatingDays(routeKey);
        return RouteOperatingDaysEditor.IsConfiguredForAllDays(days)
            ? "alle Tage"
            : DutyOperatingDayHelper.FormatDisplay(days);
    }

    private void ChangeChainValidity()
    {
        _errorText.Visibility = Visibility.Collapsed;
        if (_tripList.SelectedIndex < 0 || _tripList.SelectedIndex >= _trips.Count)
        {
            ShowError("Bitte zuerst eine Fahrt mit Routenschnur auswählen.");
            return;
        }

        var seedFromList = _trips[_tripList.SelectedIndex].RouteKey;
        var seedLineCourse = RouteDisplayHelper.NormalizeLineCourse(
            RouteDisplayHelper.Parse(seedFromList).LineCourse);
        if (!string.IsNullOrEmpty(seedLineCourse))
        {
            RouteChainPlanner.RemapRouteChangeLinksOntoLineCourse(_editor, seedLineCourse);
        }

        var chainKeys = GetSelectedChainRouteKeys();
        if (chainKeys.Count == 0)
        {
            ShowError("Bitte zuerst eine Fahrt mit Routenschnur auswählen.");
            return;
        }

        var seedKey = chainKeys[0];
        seedLineCourse = RouteDisplayHelper.NormalizeLineCourse(
            RouteDisplayHelper.Parse(seedKey).LineCourse);
        // Nur Fahrten desselben Linie/Kurs – verhindert Überschreiben der Quell-Schnur.
        var targetKeys = chainKeys
            .Where(key =>
                string.IsNullOrEmpty(seedLineCourse) ||
                string.Equals(
                    RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(key).LineCourse),
                    seedLineCourse,
                    StringComparison.Ordinal))
            .GroupBy(RouteDisplayHelper.ToCanonicalRouteKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (targetKeys.Count == 0)
        {
            ShowError("Keine Fahrten des aktuellen Linie/Kurs in der Routenschnur.");
            return;
        }

        var seedDays = _editor.GetRouteOperatingDays(seedKey);
        var seedRange = _editor.GetRouteDateRange(seedKey);
        var seedOperatingDates = _editor.GetRouteOperatingDates(seedKey);

        var dialog = new Window
        {
            Title = "Gültigkeit der Routenschnur ändern",
            Width = 520,
            MinWidth = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = PanelBackground,
            ResizeMode = ResizeMode.NoResize
        };
        WindowTitleBarHelper.ApplyDarkWindowBackground(dialog);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(dialog);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(seedLineCourse)
                ? $"Änderung gilt für {targetKeys.Count} verknüpfte Fahrt/en der Routenschnur."
                : $"Änderung gilt für {targetKeys.Count} Fahrt/en auf Linie/Kurs {seedLineCourse} " +
                  "(nicht für andere Kurse mit gleicher Fahrtnummer).",
            Foreground = MutedForeground,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(MakeLabel("Verkehrstage"));
        var dayWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var dayChecks = new List<CheckBox>();
        var effectiveSeedDays = RouteOperatingDaysEditor.IsConfiguredForAllDays(seedDays)
            ? RouteOperatingDaysEditor.AllDays.ToHashSet()
            : seedDays;
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = effectiveSeedDays.Contains(day),
                Tag = day,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 12, 4)
            };
            dayChecks.Add(check);
            dayWrap.Children.Add(check);
        }

        root.Children.Add(dayWrap);

        root.Children.Add(MakeLabel("Gültigkeit von / bis (optional)"));
        root.Children.Add(new TextBlock
        {
            Text = "TT.MM.JJJJ – leer lassen = unbegrenzt",
            Foreground = MutedForeground,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var editDateGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fromBox = MakeDateInput(
            "von",
            seedRange.From is { } fromDate ? RouteDateRange.FormatDate(fromDate) : string.Empty);
        var toBox = MakeDateInput(
            "bis",
            seedRange.To is { } toDate ? RouteDateRange.FormatDate(toDate) : string.Empty);
        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toBox, 2);
        editDateGrid.Children.Add(fromBox);
        editDateGrid.Children.Add(toBox);
        root.Children.Add(editDateGrid);

        root.Children.Add(MakeLabel("Einzelne Betriebstage (optional)"));
        root.Children.Add(new TextBlock
        {
            Text = "Kommagetrennt oder Bereiche (z. B. 10.08-14.08) – leer = alle Tage im Zeitraum",
            Foreground = MutedForeground,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var operatingDatesBox = new TextBox
        {
            Text = RouteOperatingDatesEditor.FormatDisplay(seedOperatingDates),
            MinHeight = 64,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(operatingDatesBox);

        var localError = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(localError);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Cursor = Cursors.Hand
        };
        cancel.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };
        var save = new Button
        {
            Content = "Übernehmen",
            Padding = new Thickness(16, 8, 16, 8),
            IsDefault = true,
            Cursor = Cursors.Hand
        };
        save.Click += (_, _) =>
        {
            var selectedDays = dayChecks
                .Where(c => c.IsChecked == true && c.Tag is DutyOperatingDay)
                .Select(c => (DutyOperatingDay)c.Tag!)
                .ToHashSet();
            if (selectedDays.Count == 0)
            {
                localError.Text = "Bitte mindestens einen Verkehrstag auswählen.";
                localError.Visibility = Visibility.Visible;
                return;
            }

            if (!RouteDateRange.TryParse(fromBox.Text, toBox.Text, out var range))
            {
                localError.Text = "Ungültige Gültigkeit – Format TT.MM.JJJJ, von ≤ bis (oder beide leer).";
                localError.Visibility = Visibility.Visible;
                return;
            }

            if (!RouteOperatingDatesEditor.TryParseDateList(
                    operatingDatesBox.Text,
                    out var operatingDates,
                    out var datesError))
            {
                localError.Text = datesError ?? "Ungültige Betriebstage.";
                localError.Visibility = Visibility.Visible;
                return;
            }

            foreach (var routeKey in targetKeys.ToList())
            {
                var updatedKey = _editor.ApplyOperatingDaysChange(routeKey, selectedDays);
                _editor.SetRouteDateRange(updatedKey, range.IsRestricted ? range : null);
                _editor.SetRouteOperatingDates(updatedKey, operatingDates);
            }

            _onPackageChanged?.Invoke(null);
            dialog.Tag = range;
            dialog.DialogResult = true;
            dialog.Close();
        };
        bar.Children.Add(cancel);
        bar.Children.Add(save);
        root.Children.Add(bar);
        dialog.Content = root;

        if (dialog.ShowDialog() == true)
        {
            // Prüfzeitraum an neue Gültigkeit anpassen, sonst verschwinden die Fahrten aus der Liste
            // (z. B. Suche Do–Fr, neu nur Fr–Sa ab Freitag).
            if (dialog.Tag is RouteDateRange applied && applied.IsRestricted)
            {
                _dateFromBox.Text = applied.From is { } f
                    ? RouteDateRange.FormatDate(f)
                    : string.Empty;
                _dateToBox.Text = applied.To is { } t
                    ? RouteDateRange.FormatDate(t)
                    : string.Empty;
            }

            var selectedCanonical = RouteDisplayHelper.ToCanonicalRouteKey(
                _trips[_tripList.SelectedIndex].RouteKey);
            SearchTrips();
            var newIndex = _trips.ToList().FindIndex(t =>
                string.Equals(
                    RouteDisplayHelper.ToCanonicalRouteKey(t.RouteKey),
                    selectedCanonical,
                    StringComparison.OrdinalIgnoreCase));
            if (newIndex < 0 && _trips.Count > 0)
            {
                newIndex = 0;
            }

            if (newIndex >= 0)
            {
                _tripList.SelectedIndex = newIndex;
            }

            UpdateValiditySummary();
            UpdateChainPreview();
        }
    }

    private void ExportSelectedChainPdf()
    {
        _errorText.Visibility = Visibility.Collapsed;
        if (_tripList.SelectedIndex < 0 || _tripList.SelectedIndex >= _trips.Count)
        {
            ShowError("Bitte zuerst eine Fahrt mit Routenschnur auswählen.");
            return;
        }

        var selected = _trips[_tripList.SelectedIndex];
        var segments = RouteChainPlanner.BuildChainSchedule(_editor, selected.RouteKey, _activeFilter);
        if (segments.Count == 0)
        {
            ShowError("Keine Fahrplandaten für diese Fahrt.");
            return;
        }

        var lineCourse = RouteDisplayHelper.NormalizeLineCourse(selected.Definition.LineCourse);
        var pdfSegments = segments.Select(BuildPdfSegment).ToList();
        var model = new RouteChainPdfGenerator.Model(
            LineCourse: lineCourse,
            FilterSummary: _activeFilter.HasDates ? FormatFilterSummary(_activeFilter) : null,
            ValiditySummary: string.IsNullOrWhiteSpace(_validityText.Text) ? null : _validityText.Text,
            Segments: pdfSegments);

        var dialog = new SaveFileDialog
        {
            Title = "Routenschnur als PDF speichern",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = RouteChainPdfGenerator.BuildDefaultFileName(lineCourse),
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            RouteChainPdfGenerator.Generate(dialog.FileName, model);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Öffnen optional
            }
        }
        catch (Exception ex)
        {
            ShowError($"PDF fehlgeschlagen: {ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "Routenschnur PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private RouteChainPdfGenerator.Segment BuildPdfSegment(RouteChainPlanner.ChainSegment segment)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(segment.StartTimeDisplay))
        {
            meta.Add($"Start {segment.StartTimeDisplay}");
        }

        meta.Add($"Gültig: {FormatRouteValidityDate(segment.RouteKey)}");

        if (!string.IsNullOrWhiteSpace(segment.OperatingDaysDisplay))
        {
            meta.Add($"Verkehr: {segment.OperatingDaysDisplay}");
        }
        else
        {
            meta.Add($"Verkehr: {FormatRouteValidityDays(segment.RouteKey)}");
        }

        if (!string.IsNullOrWhiteSpace(segment.RouteChangeTo))
        {
            meta.Add($"Routenwechsel → {segment.RouteChangeTo}");
        }

        return new RouteChainPdfGenerator.Segment(
            Title: $"{segment.Index}. {segment.RouteLabel}",
            MetaLine: string.Join("  ·  ", meta),
            Stops: segment.Stops
                .Select(s => new RouteChainPdfGenerator.StopRow(s.Name, s.TimeDisplay, s.IsRouteChangeStop))
                .ToList());
    }

    private void CopySelectedChain()
    {
        _errorText.Visibility = Visibility.Collapsed;
        var chainKeys = GetSelectedChainRouteKeys();
        if (chainKeys.Count == 0)
        {
            ShowError("Bitte zuerst eine Fahrt mit Routenschnur auswählen.");
            return;
        }

        var seedKey = chainKeys[0];
        var seedDef = RouteDisplayHelper.Parse(seedKey);
        var seedDays = _editor.GetRouteOperatingDays(seedKey);
        var seedRange = _editor.GetRouteDateRange(seedKey);
        var seedOperatingDates = _editor.GetRouteOperatingDates(seedKey);

        var dialog = new Window
        {
            Title = "Routenschnur kopieren",
            Width = 520,
            MinWidth = 460,
            MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height * 0.88),
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = PanelBackground,
            ResizeMode = ResizeMode.NoResize
        };
        WindowTitleBarHelper.ApplyDarkWindowBackground(dialog);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(dialog);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Kopiert alle {chainKeys.Count} verknüpften Fahrten inkl. Haltestellen, " +
                   "Fahrweg und Routenwechsel. Fahrtnummern bleiben gleich – Ziel-Linie/Kurs und Gültigkeit neu setzen.",
            Foreground = MutedForeground,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(MakeLabel("Ziel-Linie/Kurs:"));
        var targetLineBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 16,
            Text = seedDef.LineCourse,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var formattingTargetLine = false;
        targetLineBox.TextChanged += (_, _) =>
        {
            if (formattingTargetLine)
            {
                return;
            }

            formattingTargetLine = true;
            var formatted = RouteDisplayHelper.FormatLineCourseInput(targetLineBox.Text);
            if (!string.Equals(targetLineBox.Text, formatted, StringComparison.Ordinal))
            {
                targetLineBox.Text = formatted;
                targetLineBox.CaretIndex = targetLineBox.Text.Length;
            }

            formattingTargetLine = false;
        };
        root.Children.Add(targetLineBox);

        root.Children.Add(MakeLabel("Verkehrstage:"));
        var dayWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var dayChecks = new List<CheckBox>();
        var effectiveSeedDays = RouteOperatingDaysEditor.IsConfiguredForAllDays(seedDays)
            ? RouteOperatingDaysEditor.AllDays.ToHashSet()
            : seedDays;
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = effectiveSeedDays.Contains(day),
                Tag = day,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 12, 4)
            };
            dayChecks.Add(check);
            dayWrap.Children.Add(check);
        }

        root.Children.Add(dayWrap);

        root.Children.Add(MakeLabel("Gültigkeit von / bis (optional):"));
        root.Children.Add(new TextBlock
        {
            Text = "TT.MM.JJJJ – leer = unbegrenzt",
            Foreground = MutedForeground,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var editDateGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        editDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fromBox = MakeDateInput(
            "von",
            seedRange.From is { } fromDate ? RouteDateRange.FormatDate(fromDate) : string.Empty);
        var toBox = MakeDateInput(
            "bis",
            seedRange.To is { } toDate ? RouteDateRange.FormatDate(toDate) : string.Empty);
        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toBox, 2);
        editDateGrid.Children.Add(fromBox);
        editDateGrid.Children.Add(toBox);
        root.Children.Add(editDateGrid);

        root.Children.Add(MakeLabel("Einzelne Betriebstage (optional):"));
        root.Children.Add(new TextBlock
        {
            Text = "Kommagetrennt, z. B. 10.08-14.08, 17.08, 20.08 – leer = alle Tage im Zeitraum.",
            Foreground = MutedForeground,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var operatingDatesBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Text = RouteOperatingDatesEditor.FormatDisplay(seedOperatingDates),
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "TT.MM oder Bereiche TT.MM-TT.MM, kommagetrennt"
        };
        root.Children.Add(operatingDatesBox);

        var localError = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(localError);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Cursor = Cursors.Hand
        };
        cancel.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };
        var copyButton = new Button
        {
            Content = "Kopieren",
            Padding = new Thickness(16, 8, 16, 8),
            IsDefault = true,
            Cursor = Cursors.Hand
        };
        copyButton.Click += (_, _) =>
        {
            var selectedDays = dayChecks
                .Where(c => c.IsChecked == true && c.Tag is DutyOperatingDay)
                .Select(c => (DutyOperatingDay)c.Tag!)
                .ToHashSet();
            if (selectedDays.Count == 0)
            {
                localError.Text = "Bitte mindestens einen Verkehrstag auswählen.";
                localError.Visibility = Visibility.Visible;
                return;
            }

            if (!RouteDateRange.TryParse(fromBox.Text, toBox.Text, out var range))
            {
                localError.Text = "Ungültige Gültigkeit – Format TT.MM.JJJJ, von ≤ bis (oder beide leer).";
                localError.Visibility = Visibility.Visible;
                return;
            }

            if (!RouteOperatingDatesEditor.TryParseDateList(
                    operatingDatesBox.Text,
                    out var operatingDates,
                    out var datesError))
            {
                localError.Text = datesError ?? "Ungültige Betriebstage.";
                localError.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var result = RouteChainCopyPlanner.CopyChain(
                    _editor,
                    new RouteChainCopyPlanner.Request(
                        chainKeys,
                        targetLineBox.Text,
                        selectedDays,
                        range.IsRestricted ? range : null,
                        operatingDates));
                var firstCreated = result.CreatedRouteKeys.Count > 0
                    ? result.CreatedRouteKeys[0]
                    : null;
                _onPackageChanged?.Invoke(firstCreated);
                dialog.Tag = (
                    result,
                    targetLineBox.Text.Trim(),
                    fromBox.Text?.Trim() ?? string.Empty,
                    toBox.Text?.Trim() ?? string.Empty);
                dialog.DialogResult = true;
                dialog.Close();
            }
            catch (Exception ex)
            {
                localError.Text = ex.Message;
                localError.Visibility = Visibility.Visible;
            }
        };
        bar.Children.Add(cancel);
        bar.Children.Add(copyButton);
        root.Children.Add(bar);
        dialog.Content = root;

        if (dialog.ShowDialog() == true &&
            dialog.Tag is ValueTuple<RouteChainCopyPlanner.Result, string, string, string> tag)
        {
            var (copyResult, targetLineRaw, fromRaw, toRaw) = tag;
            if (RouteDisplayHelper.TryParseLineCourseUserInput(targetLineRaw, out var copiedLine))
            {
                _lineCourseBox.Text = copiedLine;
            }

            if (RouteDateRange.TryParse(fromRaw, toRaw, out var copiedRange) && copiedRange.IsRestricted)
            {
                if (copiedRange.From is { } f)
                {
                    _dateFromBox.Text = RouteDateRange.FormatDate(f);
                }

                if (copiedRange.To is { } t)
                {
                    _dateToBox.Text = RouteDateRange.FormatDate(t);
                }
            }

            SearchTrips();
            if (copyResult.CreatedRouteKeys.Count > 0)
            {
                var firstCreated = copyResult.CreatedRouteKeys[0];
                var newIndex = _trips.ToList().FindIndex(t =>
                    RouteDisplayHelper.RouteKeysMatch(t.RouteKey, firstCreated));
                if (newIndex >= 0)
                {
                    _tripList.SelectedIndex = newIndex;
                }
            }

            MessageBox.Show(
                this,
                $"{copyResult.CopiedCount} Fahrt(en) der Routenschnur wurden kopiert.",
                "Routenschnur kopiert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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

    private Border BuildSegmentCard(RouteChainPlanner.ChainSegment segment)
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

        var validity = FormatRouteValidityDate(segment.RouteKey);
        meta.Add($"Gültig: {validity}");

        if (!string.IsNullOrWhiteSpace(segment.OperatingDaysDisplay))
        {
            meta.Add($"Verkehr: {segment.OperatingDaysDisplay}");
        }
        else
        {
            meta.Add($"Verkehr: {FormatRouteValidityDays(segment.RouteKey)}");
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
