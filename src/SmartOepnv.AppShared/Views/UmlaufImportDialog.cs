using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Umlaufkarte aus PDF laden, Fahrten als editierbare Karten im Ablauf anzeigen, dann importieren.
/// </summary>
public sealed class UmlaufImportDialog : Window
{
    private static Brush PanelBackground => UiTheme.BackgroundBrush;
    private static Brush CardBackground => UiTheme.SurfaceBrush;
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
    private static readonly Brush SuggestionForeground = new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static Brush LabelForeground => UiTheme.ForegroundBrush;

    private readonly EditableRoutePackage _editor;
    private readonly RoutesViewModel _routesViewModel;
    private readonly IReadOnlyList<string> _routes;
    private readonly ObservableCollection<TripCardModel> _trips = [];
    private readonly ItemsControl _tripHost;
    private readonly TextBlock _pdfPathText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _errorText;
    private readonly TextBox _ausfahrtTimeBox;
    private readonly TextBox _einfahrtTimeBox;
    private readonly TextBox _defaultLineCourseBox;
    private readonly CheckBox _linkRouteChangesBox;
    private readonly List<CheckBox> _operatingDayChecks = [];
    private readonly Dictionary<TripCardModel, List<CheckBox>> _tripDayChecks = new();
    private readonly Dictionary<string, string> _templateGuessCache = new(StringComparer.Ordinal);
    private readonly string? _initialRouteKey;
    private readonly Border _loadingOverlay;
    private readonly RotateTransform _loadingRotate;
    private readonly TextBlock _loadingStatusText;
    private readonly Button _pickPdfButton;
    private DispatcherTimer? _spinnerTimer;
    private IReadOnlyDictionary<(string FromCode, string ToCode), int>? _typicalTravelMinutes;
    private bool _openingStopDialog;
    private bool _isRebuildingCards;
    private bool _isLoadingPdf;
    private bool _suppressTripCollectionRebuild;

    public string? CreatedFirstRouteKey { get; private set; }
    public int CreatedCount { get; private set; }
    public int LinkedRouteChangeCount { get; private set; }
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public UmlaufImportDialog(
        EditableRoutePackage editor,
        RoutesViewModel routesViewModel,
        string? initialRouteKey = null)
    {
        _editor = editor;
        _routesViewModel = routesViewModel;
        _initialRouteKey = initialRouteKey;
        _routes = UmlaufImportPlanner.GetSortedTemplateRoutes(editor);
        if (_routes.Count == 0)
        {
            throw new InvalidOperationException("Keine Routen als Vorlage verfügbar");
        }

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "Umlauf importieren";
        Width = 920;
        MinWidth = 780;
        Height = 860;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PanelBackground;

        var outer = new Grid { Margin = new Thickness(0) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // —— Kopf ——
        var header = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        header.Children.Add(new TextBlock
        {
            Text = "Umlauf importieren",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.ForegroundBrush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        header.Children.Add(new TextBlock
        {
            Text = "PDF der Umlaufkarte wählen → erkannte Fahrten prüfen/korrigieren → Importieren. " +
                   "Jede Karte = eine Route im Ablauf.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var pdfRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pdfPathText = new TextBlock
        {
            Text = "Keine PDF gewählt",
            Foreground = UiTheme.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_pdfPathText, 0);
        pdfRow.Children.Add(_pdfPathText);
        _pickPdfButton = MakeAccentButton("PDF auswählen…");
        _pickPdfButton.Margin = new Thickness(12, 0, 0, 0);
        _pickPdfButton.Click += (_, _) => PickAndLoadPdf();
        Grid.SetColumn(_pickPdfButton, 1);
        pdfRow.Children.Add(_pickPdfButton);
        var addTrip = MakeAccentButton("+ Fahrt");
        addTrip.Margin = new Thickness(8, 0, 0, 0);
        addTrip.Click += (_, _) =>
        {
            _trips.Add(CreateEmptyTrip(_trips.Count % 2 == 0
                ? UmlaufImportPlanner.Direction.Hin
                : UmlaufImportPlanner.Direction.Rueck));
            RefreshTripNumbers();
        };
        Grid.SetColumn(addTrip, 2);
        pdfRow.Children.Add(addTrip);
        header.Children.Add(pdfRow);

        _statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        header.Children.Add(_statusText);

        // Globale Felder
        var globalGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        globalGrid.ColumnDefinitions.Add(new ColumnDefinition());
        globalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        globalGrid.ColumnDefinitions.Add(new ColumnDefinition());
        globalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        globalGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var linePanel = new StackPanel();
        linePanel.Children.Add(MakeLabel("Standard Linie/Kurs (z. B. 697/30)"));
        _defaultLineCourseBox = MakeInput(string.Empty);
        linePanel.Children.Add(_defaultLineCourseBox);
        Grid.SetColumn(linePanel, 0);
        globalGrid.Children.Add(linePanel);

        var ausPanel = new StackPanel();
        ausPanel.Children.Add(MakeLabel("Ausfahrtzeit → SWS Ausfahrt"));
        _ausfahrtTimeBox = MakeInput(string.Empty);
        _ausfahrtTimeBox.LostFocus += (_, _) =>
        {
            if (!_isRebuildingCards)
            {
                RebuildTripCards();
            }
        };
        ausPanel.Children.Add(_ausfahrtTimeBox);
        Grid.SetColumn(ausPanel, 2);
        globalGrid.Children.Add(ausPanel);

        var einPanel = new StackPanel();
        einPanel.Children.Add(MakeLabel("Einfahrtzeit → SWS Einfahrt"));
        _einfahrtTimeBox = MakeInput(string.Empty);
        _einfahrtTimeBox.LostFocus += (_, _) =>
        {
            if (!_isRebuildingCards)
            {
                RebuildTripCards();
            }
        };
        einPanel.Children.Add(_einfahrtTimeBox);
        Grid.SetColumn(einPanel, 4);
        globalGrid.Children.Add(einPanel);
        header.Children.Add(globalGrid);

        header.Children.Add(MakeLabel("Standard-Verkehrstage (für neue Fahrten / PDF)"));
        var daysRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = true };
        var applyDays = MakeAccentButton("Auf alle");
        applyDays.Margin = new Thickness(12, 0, 0, 0);
        applyDays.VerticalAlignment = VerticalAlignment.Center;
        applyDays.ToolTip = "Verkehrstage auf alle Fahrten übernehmen";
        DockPanel.SetDock(applyDays, Dock.Right);
        applyDays.Click += (_, _) =>
        {
            var days = GetDefaultOperatingDays();
            var daySet = days.ToHashSet();
            foreach (var trip in _trips)
            {
                trip.SetOperatingDays(days);
                if (!_tripDayChecks.TryGetValue(trip, out var checks))
                {
                    continue;
                }

                foreach (var check in checks)
                {
                    if (check.Tag is not DutyOperatingDay day || check.IsChecked == daySet.Contains(day))
                    {
                        continue;
                    }

                    check.IsChecked = daySet.Contains(day);
                }
            }
        };
        daysRow.Children.Add(applyDays);
        var dayWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var isWeekday = day is DutyOperatingDay.Monday or DutyOperatingDay.Tuesday
                or DutyOperatingDay.Wednesday or DutyOperatingDay.Thursday or DutyOperatingDay.Friday;
            var check = new CheckBox
            {
                Content = name,
                IsChecked = isWeekday,
                Tag = day,
                Foreground = LabelForeground,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _operatingDayChecks.Add(check);
            dayWrap.Children.Add(check);
        }

        daysRow.Children.Add(dayWrap);
        header.Children.Add(daysRow);

        _linkRouteChangesBox = new CheckBox
        {
            Content = "Routenwechsel verknüpfen",
            IsChecked = true,
            Foreground = LabelForeground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Routenwechsel automatisch verknüpfen (Reihenfolge der Fahrten)"
        };
        _linkRouteChangesBox.Checked += (_, _) => RebuildTripCards();
        _linkRouteChangesBox.Unchecked += (_, _) => RebuildTripCards();

        header.Children.Add(new TextBlock
        {
            Text = "Fahrten / Umlaufablauf",
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.ForegroundBrush,
            Margin = new Thickness(0, 12, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        // —— Scrollbare Fahrten ——
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20, 0, 16, 8)
        };
        _tripHost = new ItemsControl();
        _trips.CollectionChanged += (_, _) =>
        {
            if (!_suppressTripCollectionRebuild)
            {
                RebuildTripCards();
            }
        };
        scroll.Content = _tripHost;
        Grid.SetRow(scroll, 1);
        outer.Children.Add(scroll);

        // —— Fuß ——
        var footer = new DockPanel { Margin = new Thickness(20, 8, 20, 16) };
        _errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x9A, 0x9A)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        footer.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(_linkRouteChangesBox);
        var cancel = new Button
        {
            Content = "Abbrechen",
            MinWidth = 110,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
            BorderThickness = new Thickness(0)
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(cancel);
        var import = MakeAccentButton("Importieren");
        import.MinWidth = 130;
        import.FontWeight = FontWeights.SemiBold;
        import.IsDefault = true;
        import.Click += (_, _) => RunImport();
        buttons.Children.Add(import);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        outer.Children.Add(footer);

        var root = new Grid();
        root.Children.Add(outer);

        _loadingRotate = new RotateTransform();
        var spinner = new Ellipse
        {
            Width = 56,
            Height = 56,
            Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
            StrokeThickness = 6,
            StrokeDashArray = new DoubleCollection { 12, 18 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = _loadingRotate,
            RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        loadingPanel.Children.Add(spinner);
        _loadingStatusText = new TextBlock
        {
            Text = "PDF wird geladen…",
            Foreground = UiTheme.ForegroundBrush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };
        loadingPanel.Children.Add(_loadingStatusText);
        _loadingOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0A, 0x16, 0x28)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = true,
            Child = loadingPanel
        };
        root.Children.Add(_loadingOverlay);

        Content = root;

        // Beim Öffnen noch keine Fahrt – erst PDF oder „+ Fahrt“
        RebuildTripCards();
    }

    private void SetPdfLoading(bool loading, string? status = null)
    {
        _isLoadingPdf = loading;
        _pickPdfButton.IsEnabled = !loading;
        _loadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(status))
        {
            _loadingStatusText.Text = status;
        }

        if (loading)
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _spinnerTimer.Tick += (_, _) => _loadingRotate.Angle = (_loadingRotate.Angle + 8) % 360;
            _spinnerTimer.Start();
        }
        else
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;
            _loadingRotate.Angle = 0;
            _loadingStatusText.Text = "PDF wird geladen…";
        }
    }

    private IReadOnlyDictionary<(string FromCode, string ToCode), int> GetTypicalTravelMinutes() =>
        _typicalTravelMinutes ??= RouteTravelTimeLearner.BuildTypicalMinutes(
            _editor.RouteNames.Select(_editor.GetStops));

    /// <param name="resyncTemplateStops">
    /// false = nur UI neu zeichnen (z. B. nach Haltestellen-Dialog), ohne Vorlage neu zu berechnen.
    /// </param>
    private void RebuildTripCards(bool resyncTemplateStops = true)
    {
        if (_isRebuildingCards)
        {
            return;
        }

        _isRebuildingCards = true;
        _tripDayChecks.Clear();
        try
        {
            if (resyncTemplateStops)
            {
                foreach (var trip in _trips)
                {
                    EnsureStops(trip);
                }
            }

            SyncDepotStopsAcrossTrips();

            var panel = new StackPanel();
            for (var i = 0; i < _trips.Count; i++)
            {
                panel.Children.Add(BuildTripCard(_trips[i], i));
            }

            if (_trips.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Noch keine Fahrten. PDF laden oder „+ Fahrt“.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            _tripHost.Items.Clear();
            _tripHost.Items.Add(panel);
        }
        finally
        {
            _isRebuildingCards = false;
        }
    }

    /// <summary>Nur diese Fahrt neu zeichnen. Verkehrstage und Ziele brauchen keinen Neuaufbau aller Karten.</summary>
    private void ReplaceTripCard(TripCardModel trip)
    {
        if (_isRebuildingCards)
        {
            return;
        }

        var index = _trips.IndexOf(trip);
        if (index < 0 ||
            _tripHost.Items.Count == 0 ||
            _tripHost.Items[0] is not StackPanel panel ||
            index >= panel.Children.Count)
        {
            RebuildTripCards(resyncTemplateStops: false);
            return;
        }

        _isRebuildingCards = true;
        try
        {
            panel.Children.RemoveAt(index);
            panel.Children.Insert(index, BuildTripCard(trip, index));
        }
        finally
        {
            _isRebuildingCards = false;
        }
    }

    /// <summary>
    /// SWS Ausfahrt vor die 1. Fahrt, SWS Einfahrt hinter die letzte Fahrt
    /// (Zeiten aus den globalen Feldern / PDF-Kopf „Betr.Hof … – …“).
    /// </summary>
    private void SyncDepotStopsAcrossTrips()
    {
        foreach (var trip in _trips)
        {
            trip.Stops.RemoveAll(s => UmlaufImportPlanner.IsSwsDepotStopName(s.Name));
        }

        if (_trips.Count == 0)
        {
            return;
        }

        var ausRaw = _ausfahrtTimeBox.Text?.Trim() ?? string.Empty;
        if (RouteScheduleTimeCalculator.TryParseTime(ausRaw, out _))
        {
            _trips[0].Stops.Insert(
                0,
                UmlaufImportPlanner.CreateSwsDepotStop(
                    _editor,
                    routeKey: string.Empty,
                    ausRaw,
                    ausfahrt: true));
        }

        var einRaw = _einfahrtTimeBox.Text?.Trim() ?? string.Empty;
        if (RouteScheduleTimeCalculator.TryParseTime(einRaw, out _))
        {
            _trips[^1].Stops.Add(
                UmlaufImportPlanner.CreateSwsDepotStop(
                    _editor,
                    routeKey: string.Empty,
                    einRaw,
                    ausfahrt: false));
            UmlaufImportPlanner.EnsureDienstfahrtEndBeforeEinfahrt(_trips[^1].Stops, _editor);
        }
    }

    private Border BuildTripCard(TripCardModel trip, int index)
    {
        var markBrush = trip.MarkColor is Color mc
            ? new SolidColorBrush(mc)
            : AccentBrush;
        var card = new Border
        {
            Background = CardBackground,
            BorderBrush = markBrush,
            BorderThickness = new Thickness(trip.MarkColor is null ? 1 : 3),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var root = new StackPanel();
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var titleLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (trip.MarkColor is Color mark)
        {
            titleLeft.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(mark),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Farbmarkierung aus der Umlaufkarte (Linie)"
            });
        }

        titleLeft.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(trip.LineCourse)
                ? $"Fahrt {index + 1}"
                : $"Fahrt {index + 1} · {trip.LineCourse.Trim()}",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = UiTheme.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(titleLeft);
        var remove = new Button
        {
            Content = "Entfernen",
            MinHeight = 28,
            Padding = new Thickness(10, 2, 10, 2),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(remove, Dock.Right);
        var tripRef = trip;
        remove.Click += (_, _) =>
        {
            _trips.Remove(tripRef);
            RefreshTripNumbers();
        };
        titleRow.Children.Add(remove);
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = "Verkehrstage",
            Foreground = LabelForeground,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var dayWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var dayChecks = new List<CheckBox>();
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = trip.OperatingDays.Contains(day),
                Tag = day,
                Foreground = LabelForeground,
                Margin = new Thickness(0, 0, 12, 4)
            };
            check.Checked += (_, _) => trip.OperatingDays.Add(day);
            check.Unchecked += (_, _) => trip.OperatingDays.Remove(day);
            dayChecks.Add(check);
            dayWrap.Children.Add(check);
        }

        _tripDayChecks[trip] = dayChecks;
        root.Children.Add(dayWrap);

        // Richtung + Vorlage
        var row1 = CreateFieldRow();
        var dirBox = MakeDarkComboBox();
        dirBox.ItemsSource = new[] { "Hin", "Rück" };
        dirBox.SelectedItem = trip.Direction == UmlaufImportPlanner.Direction.Hin ? "Hin" : "Rück";
        dirBox.SelectionChanged += (_, _) =>
        {
            trip.Direction = string.Equals(dirBox.SelectedItem as string, "Rück", StringComparison.Ordinal)
                ? UmlaufImportPlanner.Direction.Rueck
                : UmlaufImportPlanner.Direction.Hin;
        };
        AddField(row1, 0, "Richtung", dirBox);

        var templateBox = MakeDarkComboBox();
        var templateChoices = TemplateChoicesFor(trip);
        templateBox.ItemsSource = templateChoices;
        templateBox.SelectedItem = templateChoices.Contains(trip.TemplateRouteKey)
            ? trip.TemplateRouteKey
            : templateChoices[0];
        templateBox.SelectionChanged += (_, _) =>
        {
            if (_isRebuildingCards || templateBox.SelectedItem is not string key)
            {
                return;
            }

            if (string.Equals(key, trip.TemplateRouteKey, StringComparison.Ordinal))
            {
                return;
            }

            trip.TemplateRouteKey = key;
            trip.StopsLocked = false;
            // Keine alten Ziele übernehmen (sonst bleibt z. B. Burger Bahnhof auf Schleife-Vorlage)
            EnsureStops(trip, forceRebuild: true, keepStopEdits: false);
            trip.RouteName = FormatUmlaufRouteName(
                trip.LineCourse,
                trip.StartStopHint,
                trip.EndStopHint,
                trip.Stops,
                RouteDisplayHelper.Parse(key).Name);
            ReplaceTripCard(trip);
        };
        trip.TemplateRouteKey = templateBox.SelectedItem as string ?? _routes[0];
        AddField(row1, 2, "Vorlage (Haltestellenkette)", templateBox);
        root.Children.Add(row1);

        // Name / Linie / Fahrt / Zeiten
        var row2 = CreateFieldRow(columns: 4);
        // Routenname / Linie / Fahrtnummer: nur speichern, kein Rebuild (sonst UI-Freeze)
        var nameBox = BindInput(trip.RouteName, v => trip.RouteName = v);
        AddField(row2, 0, "Routenname", nameBox);
        var lineBox = BindInput(trip.LineCourse, v => trip.LineCourse = v);
        lineBox.LostFocus += (_, _) =>
        {
            if (_isRebuildingCards)
            {
                return;
            }

            trip.LineCourse = RouteDisplayHelper.NormalizeLineCourse(trip.LineCourse);
            if (!string.Equals(lineBox.Text, trip.LineCourse, StringComparison.Ordinal))
            {
                lineBox.Text = trip.LineCourse;
            }

            var synced = ApplyLineCourseToRouteName(trip.RouteName, trip.LineCourse);
            if (!string.Equals(synced, trip.RouteName, StringComparison.Ordinal))
            {
                trip.RouteName = synced;
                nameBox.Text = synced;
            }
        };
        AddField(row2, 2, "Linie/Kurs", lineBox);
        var tripBox = BindInput(trip.TripNumber, v => trip.TripNumber = v);
        AddField(row2, 4, "Fahrtnummer", tripBox);
        var startBox = BindInput(trip.StartTime, v => trip.StartTime = v);
        startBox.LostFocus += (_, _) =>
        {
            if (_isRebuildingCards || _openingStopDialog)
            {
                return;
            }

            trip.StopsLocked = false;
            EnsureStops(trip, forceRebuild: true);
            ReplaceTripCard(trip);
        };
        AddField(row2, 6, "Startzeit", startBox);
        root.Children.Add(row2);

        var row3 = CreateFieldRow(columns: 2);
        var endBox = BindInput(trip.EndTime, v => trip.EndTime = v);
        endBox.LostFocus += (_, _) =>
        {
            if (_isRebuildingCards || _openingStopDialog)
            {
                return;
            }

            trip.StopsLocked = false;
            EnsureStops(trip, forceRebuild: true);
            ReplaceTripCard(trip);
        };
        AddField(row3, 0, "Endzeit (Info / Anker optional)", endBox);
        var note = new TextBlock
        {
            Text = BuildTripHintText(trip),
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 18, 0, 8)
        };
        Grid.SetColumn(note, 2);
        row3.Children.Add(note);
        root.Children.Add(row3);

        root.Children.Add(BuildStopListPanel(trip, index));

        card.Child = root;
        return card;
    }

    private static string BuildTripHintText(TripCardModel trip)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(trip.StartStopHint) || !string.IsNullOrWhiteSpace(trip.EndStopHint))
        {
            parts.Add(
                $"PDF-Abschnitt: {trip.StartStopHint ?? "?"} → {trip.EndStopHint ?? "?"}");
        }
        else if (!string.IsNullOrWhiteSpace(trip.SectionHint))
        {
            parts.Add($"Abschnitt PDF: {trip.SectionHint}");
        }

        parts.Add("Klick = Ziele · + = Haltestelle einfügen · ✕ = entfernen");
        return string.Join(" · ", parts);
    }

    private string? FormatRouteChangeHint(int tripIndex)
    {
        if (_linkRouteChangesBox.IsChecked != true)
        {
            return null;
        }

        if (tripIndex >= _trips.Count - 1)
        {
            return null;
        }

        var next = _trips[tripIndex + 1];
        return $"Routenwechsel → Fahrt {tripIndex + 2}: {FormatTripLinkLabel(next)}";
    }

    private static bool IsPendingRouteChangeStop(TripCardModel trip, RouteStopItem stop)
    {
        for (var s = trip.Stops.Count - 1; s >= 0; s--)
        {
            if (UmlaufImportPlanner.IsSwsDepotStopName(trip.Stops[s].Name))
            {
                continue;
            }

            return ReferenceEquals(trip.Stops[s], stop);
        }

        return false;
    }

    /// <summary>
    /// Die grüne Zeile ist die geplante Folgefahrt. Dieselbe Angabe kommt an den Endhalt,
    /// damit der Halt-Dialog Haken und Fahrt zeigt.
    /// </summary>
    private void StampPendingRouteChange(RouteStopItem stop, TripCardModel next)
    {
        stop.IsEndStop = true;
        stop.RouteChangeEnabled = true;
        stop.SelectedLineCourseTrip = ResolvePendingFollowRoute(next);
    }

    private string ResolvePendingFollowRoute(TripCardModel next)
    {
        var name = (next.RouteName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = RouteDisplayHelper.Parse(next.TemplateRouteKey ?? string.Empty).Name.Trim();
        }

        var lineCourse = RouteDisplayHelper.NormalizeLineCourse(next.LineCourse ?? string.Empty);
        var tripNumber = RouteDisplayHelper.NormalizeTripNumber(next.TripNumber ?? string.Empty);
        var built = name;
        if (!string.IsNullOrEmpty(lineCourse) || !string.IsNullOrEmpty(tripNumber))
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(lineCourse))
            {
                parts.Add($"Linie: {lineCourse}");
            }

            if (!string.IsNullOrEmpty(tripNumber))
            {
                parts.Add($"Fahrt: {tripNumber}");
            }

            built = $"{name} ({string.Join(", ", parts)})";
        }

        foreach (var route in RouteStopEditorCatalog.LoadLineCourseTripRoutes(_editor))
        {
            if (string.Equals(route, built, StringComparison.OrdinalIgnoreCase))
            {
                return route;
            }

            var parsed = RouteDisplayHelper.Parse(route);
            if (string.Equals(
                    RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber),
                    tripNumber,
                    StringComparison.Ordinal) &&
                string.Equals(
                    RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse),
                    lineCourse,
                    StringComparison.Ordinal) &&
                route.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return route;
            }
        }

        return string.IsNullOrWhiteSpace(built) ? next.TemplateRouteKey ?? string.Empty : built;
    }

    private static string FormatTripLinkLabel(TripCardModel trip)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(trip.TripNumber))
        {
            parts.Add(trip.TripNumber.Trim());
        }

        if (!string.IsNullOrWhiteSpace(trip.LineCourse))
        {
            parts.Add(trip.LineCourse.Trim());
        }

        if (!string.IsNullOrWhiteSpace(trip.RouteName))
        {
            parts.Add(trip.RouteName.Trim());
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "(noch ohne Nummer/Name)";
    }

    private UIElement BuildStopListPanel(TripCardModel trip, int tripIndex)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = trip.Stops.Count > 0
                ? $"Haltestellen ({trip.Stops.Count}) – Enter übernimmt die Zeit, springt weiter und zeigt den grauen Vorschlag. Ziffer verwirft ihn."
                : "Haltestellen (Vorlage/Startzeit prüfen)",
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.ForegroundBrush,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (trip.Stops.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Keine Haltestellen – gültige Vorlage und Startzeit wählen oder eine Haltestelle einfügen.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x9A, 0x9A)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(MakeInsertStopButton(trip, null));
            return panel;
        }

        var routeChangeHint = FormatRouteChangeHint(tripIndex);
        var lastPassengerIdx = -1;
        for (var s = trip.Stops.Count - 1; s >= 0; s--)
        {
            if (!UmlaufImportPlanner.IsSwsDepotStopName(trip.Stops[s].Name))
            {
                lastPassengerIdx = s;
                break;
            }
        }

        if (lastPassengerIdx >= 0 && routeChangeHint is not null && tripIndex + 1 < _trips.Count)
        {
            StampPendingRouteChange(trip.Stops[lastPassengerIdx], _trips[tripIndex + 1]);
        }

        var list = new StackPanel();
        var timeBoxes = new List<TextBox>();
        for (var i = 0; i < trip.Stops.Count; i++)
        {
            var stop = trip.Stops[i];
            var isDepot = UmlaufImportPlanner.IsSwsDepotStopName(stop.Name);
            var showRouteChange = i == lastPassengerIdx && routeChangeHint is not null;
            var tripRef = trip;
            var stopRef = stop;
            var rowIndex = i;

            var row = new Border
            {
                Background = isDepot
                    ? new SolidColorBrush(Color.FromRgb(0x4A, 0x14, 0x14))
                    : showRouteChange
                        ? new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))
                        : i % 2 == 0
                            ? new SolidColorBrush(Color.FromRgb(0x0E, 0x1C, 0x30))
                            : new SolidColorBrush(Color.FromRgb(0x14, 0x28, 0x40)),
                BorderBrush = isDepot
                    ? new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73))
                    : showRouteChange
                        ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
                        : new SolidColorBrush(Color.FromRgb(0x2A, 0x3F, 0x5A)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4),
                ToolTip = isDepot
                    ? "Depot-Halt (SWS Ausfahrt/Einfahrt aus Umlaufkarte)"
                    : showRouteChange
                        ? routeChangeHint
                        : null
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timeBox = MakeStopInlineBox(
                string.IsNullOrWhiteSpace(stop.Time) ? string.Empty : stop.Time!,
                AccentBrush,
                FontWeights.SemiBold,
                maxLength: 5);
            timeBox.ToolTip = "Abfahrtszeit. Enter übernimmt und springt zur nächsten Zeit (grauer Vorschlag).";
            var timeState = new StopTimeBoxState(tripRef, stopRef, timeBoxes);
            timeBox.Tag = timeState;
            timeBoxes.Add(timeBox);
            timeBox.PreviewTextInput += StopTimeBox_PreviewTextInput;
            timeBox.PreviewKeyDown += StopTimeBox_PreviewKeyDown;
            timeBox.GotFocus += (_, _) => OnStopTimeBoxGotFocus(timeBox, timeState);
            timeBox.LostFocus += (_, _) => OnStopTimeBoxLostFocus(timeBox, timeState);
            Grid.SetColumn(timeBox, 0);
            grid.Children.Add(timeBox);

            var nameCol = new StackPanel { Margin = new Thickness(4, 0, 8, 0) };
            var nameText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(stop.Name) ? "(ohne Name)" : stop.Name,
                Foreground = UiTheme.ForegroundBrush,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = Cursors.Hand,
                ToolTip = "Klicken → Haltestelle bearbeiten"
            };
            nameText.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenStopEditor(tripRef, stopRef);
            };
            nameCol.Children.Add(nameText);
            if (isDepot)
            {
                nameCol.Children.Add(new TextBlock
                {
                    Text = string.Equals(
                            stop.Name?.Trim(),
                            UmlaufImportPlanner.SwsAusfahrtStopName,
                            StringComparison.OrdinalIgnoreCase)
                        ? "Betr.Hof → erste Fahrt"
                        : "letzte Fahrt → Betr.Hof",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0xD2)),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            else if (showRouteChange)
            {
                nameCol.Children.Add(new TextBlock
                {
                    Text = routeChangeHint,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            Grid.SetColumn(nameCol, 1);
            grid.Children.Add(nameCol);

            var destCol = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var stopHint = FormatStopHintDisplay(stop);
            if (!string.IsNullOrWhiteSpace(stopHint))
            {
                destCol.Children.Add(new TextBlock
                {
                    Text = stopHint,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    MaxWidth = 160,
                    ToolTip = stopHint
                });
            }

            var dest = FormatStopDestinationHint(stop);
            var destText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(dest) ? "Ziele…" : dest,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Klicken → Ziele / Anzeigen bearbeiten"
            };
            destText.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenStopEditor(tripRef, stopRef);
            };
            destCol.Children.Add(destText);
            Grid.SetColumn(destCol, 2);
            grid.Children.Add(destCol);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var addBtn = new Button
            {
                Content = "+",
                Width = 28,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Haltestelle danach aus der Kartei einfügen",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            addBtn.Click += (_, e) =>
            {
                e.Handled = true;
                InsertStopAfter(tripRef, stopRef);
            };
            actions.Children.Add(addBtn);
            var deleteBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 26,
                Padding = new Thickness(0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Haltestelle aus dieser Fahrt entfernen",
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Click += (_, e) =>
            {
                e.Handled = true;
                RemoveStop(tripRef, stopRef);
            };
            actions.Children.Add(deleteBtn);
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);

            row.Child = grid;
            row.MouseEnter += (_, _) =>
                row.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5C));
            row.MouseLeave += (_, _) =>
                row.Background = isDepot
                    ? new SolidColorBrush(Color.FromRgb(0x4A, 0x14, 0x14))
                    : showRouteChange
                        ? new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))
                        : rowIndex % 2 == 0
                            ? new SolidColorBrush(Color.FromRgb(0x0E, 0x1C, 0x30))
                            : new SolidColorBrush(Color.FromRgb(0x14, 0x28, 0x40));
            list.Children.Add(row);
        }

        panel.Children.Add(list);
        return panel;
    }

    private static TextBox MakeStopInlineBox(
        string text,
        Brush foreground,
        FontWeight weight,
        int maxLength) =>
        new()
        {
            Text = text,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
            Foreground = foreground,
            CaretBrush = foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2, 2, 2, 2),
            FontSize = 12,
            FontWeight = weight,
            MaxLength = maxLength,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 24
        };

    private void CommitStopTime(TripCardModel trip, RouteStopItem stop, TextBox box)
    {
        var raw = (box.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            stop.Time = string.Empty;
            trip.StopsLocked = true;
            return;
        }

        if (!RouteScheduleTimeCalculator.TryParseTime(raw, out _))
        {
            box.Text = string.IsNullOrWhiteSpace(stop.Time) ? string.Empty : stop.Time!;
            return;
        }

        var normalized = RouteScheduleTimeCalculator.NormalizeTimeInput(raw);
        stop.Time = normalized;
        box.Text = normalized;
        trip.StopsLocked = true;

        var idx = trip.Stops.IndexOf(stop);
        if (idx == 0)
        {
            trip.StartTime = normalized;
        }

        if (idx == trip.Stops.Count - 1)
        {
            trip.EndTime = normalized;
        }
    }

    private void StopTimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not StopTimeBoxState state)
        {
            return;
        }

        if (e.Text.Any(ch => !char.IsDigit(ch)))
        {
            e.Handled = true;
            return;
        }

        if (!state.ShowingSuggestion)
        {
            return;
        }

        state.ShowingSuggestion = false;
        state.SuggestedTime = null;
        box.Foreground = AccentBrush;
        box.CaretBrush = AccentBrush;
        box.Text = string.Empty;
        box.CaretIndex = 0;
    }

    private void StopTimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not StopTimeBoxState state)
        {
            return;
        }

        if (e.Key is Key.Space or Key.OemPeriod or Key.Decimal or Key.OemSemicolon or Key.Oem2)
        {
            e.Handled = true;
            return;
        }

        if (state.ShowingSuggestion && e.Key is Key.Back or Key.Delete)
        {
            state.ShowingSuggestion = false;
            state.SuggestedTime = null;
            box.Foreground = AccentBrush;
            box.CaretBrush = AccentBrush;
            box.Text = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (state.ShowingSuggestion && !string.IsNullOrEmpty(state.SuggestedTime))
        {
            state.ShowingSuggestion = false;
            box.Foreground = AccentBrush;
            box.CaretBrush = AccentBrush;
            box.Text = state.SuggestedTime;
            state.SuggestedTime = null;
        }

        CommitStopTime(state.Trip, state.Stop, box);
        MoveStopTimeFocusWithSuggestion(box, state);
        e.Handled = true;
    }

    private void OnStopTimeBoxGotFocus(TextBox box, StopTimeBoxState state)
    {
        if (state.ShowingSuggestion && !string.IsNullOrEmpty(state.SuggestedTime))
        {
            ShowStopTimeSuggestion(box, state, state.SuggestedTime);
            return;
        }

        box.SelectAll();
    }

    private void OnStopTimeBoxLostFocus(TextBox box, StopTimeBoxState state)
    {
        if (state.ShowingSuggestion)
        {
            state.ShowingSuggestion = false;
            state.SuggestedTime = null;
            box.Foreground = AccentBrush;
            box.CaretBrush = AccentBrush;
            box.Text = string.IsNullOrWhiteSpace(state.Stop.Time) ? string.Empty : state.Stop.Time!;
            return;
        }

        CommitStopTime(state.Trip, state.Stop, box);
    }

    private void MoveStopTimeFocusWithSuggestion(TextBox current, StopTimeBoxState state)
    {
        var index = state.Boxes.IndexOf(current);
        if (index < 0 || index >= state.Boxes.Count - 1)
        {
            return;
        }

        var next = state.Boxes[index + 1];
        if (next.Tag is StopTimeBoxState nextState &&
            RouteTravelTimeLearner.TrySuggestArrival(
                GetTypicalTravelMinutes(),
                state.Stop.PlannerStopCode,
                state.Stop.Time,
                nextState.Stop.PlannerStopCode,
                out var suggested))
        {
            ShowStopTimeSuggestion(next, nextState, suggested);
        }

        next.Focus();
        next.BringIntoView();
        next.SelectAll();
    }

    private static void ShowStopTimeSuggestion(TextBox box, StopTimeBoxState state, string suggestedHhMm)
    {
        if (string.IsNullOrEmpty(suggestedHhMm))
        {
            return;
        }

        state.SuggestedTime = suggestedHhMm;
        state.ShowingSuggestion = true;
        box.Foreground = SuggestionForeground;
        box.CaretBrush = SuggestionForeground;
        box.MaxLength = Math.Max(5, suggestedHhMm.Length);
        box.Text = suggestedHhMm;
        box.MaxLength = 5;
        box.SelectAll();
    }

    private sealed class StopTimeBoxState
    {
        public StopTimeBoxState(TripCardModel trip, RouteStopItem stop, List<TextBox> boxes)
        {
            Trip = trip;
            Stop = stop;
            Boxes = boxes;
        }

        public TripCardModel Trip { get; }
        public RouteStopItem Stop { get; }
        public List<TextBox> Boxes { get; }
        public bool ShowingSuggestion { get; set; }
        public string? SuggestedTime { get; set; }
    }

    private Button MakeInsertStopButton(TripCardModel trip, RouteStopItem? after)
    {
        var button = new Button
        {
            Content = "Haltestelle einfügen",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => InsertStopAfter(trip, after);
        return button;
    }

    /// <summary>Halt aus der Kartei hinter dieser Zeile einfügen. Nur diese Fahrt wird neu gezeichnet.</summary>
    private void InsertStopAfter(TripCardModel trip, RouteStopItem? after)
    {
        var templates = _editor.StopTemplates
            .Where(t => t.HasPersistableContent())
            .ToList();
        if (templates.Count == 0)
        {
            _errorText.Text = "Keine Haltestellen in der Kartei.";
            return;
        }

        var dialog = new PickStopFromLibraryDialog(templates) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedTemplate is null)
        {
            return;
        }

        var stop = dialog.SelectedTemplate.ToRouteStop(trip.TemplateRouteKey);
        stop.Time = string.Empty;
        stop.IsEndStop = false;

        var index = trip.Stops.Count;
        if (after is not null)
        {
            var at = trip.Stops.IndexOf(after);
            index = at < 0 ? trip.Stops.Count : at + 1;
        }

        var einIdx = trip.Stops.FindIndex(s =>
            string.Equals(s.Name, UmlaufImportPlanner.SwsEinfahrtStopName, StringComparison.OrdinalIgnoreCase));
        if (einIdx >= 0 && index > einIdx)
        {
            index = einIdx;
        }

        trip.Stops.Insert(index, stop);
        trip.StopsLocked = true;
        _errorText.Text = string.Empty;
        ReplaceTripCard(trip);
    }

    private void RemoveStop(TripCardModel trip, RouteStopItem stop)
    {
        if (!trip.Stops.Remove(stop))
        {
            return;
        }

        trip.StopsLocked = true;
        if (trip.Stops.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(trip.Stops[0].Time))
            {
                trip.StartTime = trip.Stops[0].Time!;
            }

            if (!string.IsNullOrWhiteSpace(trip.Stops[^1].Time))
            {
                trip.EndTime = trip.Stops[^1].Time!;
            }

            if (string.Equals(
                    trip.Stops[^1].Name,
                    UmlaufImportPlanner.SwsEinfahrtStopName,
                    StringComparison.OrdinalIgnoreCase))
            {
                UmlaufImportPlanner.EnsureDienstfahrtEndBeforeEinfahrt(trip.Stops, _editor);
            }
            else
            {
                trip.Stops[^1].IsEndStop = true;
            }
        }

        ReplaceTripCard(trip);
    }

    private void OpenStopEditor(TripCardModel trip, RouteStopItem stop)
    {
        if (_openingStopDialog)
        {
            return;
        }

        _openingStopDialog = true;
        try
        {
            var tripIndex = _trips.IndexOf(trip);
            if (tripIndex >= 0 &&
                tripIndex + 1 < _trips.Count &&
                FormatRouteChangeHint(tripIndex) is not null &&
                IsPendingRouteChangeStop(trip, stop))
            {
                StampPendingRouteChange(stop, _trips[tripIndex + 1]);
            }

            var dialog = new RouteStopEditDialog(_routesViewModel, stop, commitPackageOnSave: false)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                // Änderungen liegen auf dem RouteStopItem – Kette nicht aus Vorlage neu bauen.
                trip.StopsLocked = true;
                ReplaceTripCard(trip);
            }
        }
        finally
        {
            _openingStopDialog = false;
        }
    }

    /// <param name="keepStopEdits">
    /// true = Ziele/Felder gleicher Stop-Codes aus der bisherigen Kette behalten (Zeitänderung).
    /// false = alles frisch aus der Vorlage (Vorlagenwechsel / Import).
    /// </param>
    private void EnsureStops(TripCardModel trip, bool forceRebuild = false, bool keepStopEdits = true)
    {
        if (string.IsNullOrWhiteSpace(trip.TemplateRouteKey))
        {
            trip.Stops = [];
            return;
        }

        // Manuell gelöschte/angepasste Kette nicht überschreiben (außer Vorlage/Zeiten ändern).
        if (trip.StopsLocked && !forceRebuild && trip.Stops.Count > 0)
        {
            return;
        }

        IReadOnlyList<RouteStopItem>? preserveFrom =
            keepStopEdits && trip.Stops.Count > 0 ? trip.Stops : null;

        trip.Stops = UmlaufImportPlanner.BuildStopsFromTemplate(
            _editor,
            trip.TemplateRouteKey,
            trip.StartTime,
            string.IsNullOrWhiteSpace(trip.EndTime) ? null : trip.EndTime,
            preserveFrom,
            trip.StartStopHint,
            trip.EndStopHint,
            trip.TimingPoints,
            GetTypicalTravelMinutes());
        trip.StopsLocked = false;
    }

    /// <summary>Haltestellenhinweis nur anzeigen, wenn aktiv und Text hinterlegt.</summary>
    private static string FormatStopHintDisplay(RouteStopItem stop)
    {
        if (!stop.StopHintEnabled || string.IsNullOrWhiteSpace(stop.StopHintText))
        {
            return string.Empty;
        }

        return stop.StopHintText.Trim();
    }

    private static string FormatStopDestinationHint(RouteStopItem stop)
    {
        if (stop.ZielwechselEnabled)
        {
            var z = ResolveDisplayDestination(stop);
            return string.IsNullOrEmpty(z) ? "Zielwechsel…" : $"Ziel: {z}";
        }

        if (RouteStopEditorCatalog.IsStartStop(stop))
        {
            // Nicht „Starthaltestelle“-Platzhalter, sondern das gewählte Anzeigeziel
            var z = ResolveDisplayDestination(stop);
            return string.IsNullOrEmpty(z) ? "Start" : $"Start · Ziel: {z}";
        }

        if (stop.IsEndStop)
        {
            // Endziel nach Zielwechsel / Endziel-Felder
            var z = ResolveEndDisplayDestination(stop);
            if (string.IsNullOrEmpty(z))
            {
                z = ResolveDisplayDestination(stop);
            }

            return string.IsNullOrEmpty(z) ? string.Empty : $"Ziel: {z}";
        }

        return string.Empty;
    }

    /// <summary>Gewähltes Außenanzeige-Ziel; Platzhalter „Starthaltestelle“ überspringen.</summary>
    private static string ResolveDisplayDestination(RouteStopItem stop) =>
        FirstRealDestination(
            stop.Destination,
            stop.Ds021NeuDestination,
            stop.FmaS1Destination,
            stop.Ds003aDestination,
            stop.ZielnummerDestination,
            stop.MobitecDestination);

    private static string ResolveEndDisplayDestination(RouteStopItem stop) =>
        FirstRealDestination(
            stop.EndDestination,
            stop.Ds021NeuEndDestination,
            stop.FmaS1EndDestination,
            stop.Ds003aEndDestination,
            stop.ZielnummerEndDestination,
            stop.MobitecEndDestination);

    private static string FirstRealDestination(params string?[] values)
    {
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v) ||
                RouteStopEditorCatalog.IsStartStopPlaceholder(v))
            {
                continue;
            }

            return v.Trim();
        }

        return string.Empty;
    }

    private async void PickAndLoadPdf()
    {
        if (_isLoadingPdf)
        {
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Umlaufkarte (PDF)",
            Filter = "PDF-Dateien (*.pdf)|*.pdf|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        var path = dlg.FileName;
        _errorText.Text = string.Empty;
        _statusText.Text = "PDF wird gelesen…";
        SetPdfLoading(true, "PDF wird gelesen…");
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            var parsed = await Task.Run(() => UmlaufPdfReader.Read(path)).ConfigureAwait(true);
            _pdfPathText.Text = path;
            ApplyParseHeaderFields(parsed);

            SetPdfLoading(true, "Fahrzeiten werden berechnet…");
            await Dispatcher.Yield(DispatcherPriority.Render);

            // Lernminuten einmalig im Hintergrund (nicht pro Fahrt auf dem UI-Thread)
            var typical = await Task.Run(() =>
                    RouteTravelTimeLearner.BuildTypicalMinutes(_editor.RouteNames.Select(_editor.GetStops)))
                .ConfigureAwait(true);
            _typicalTravelMinutes = typical;

            var defaultDays = GetDefaultOperatingDays();
            var defaultLine = _defaultLineCourseBox.Text?.Trim() ?? string.Empty;
            var prepared = await Task.Run(() =>
                    PrepareTripsFromParse(parsed, defaultDays, defaultLine, typical))
                .ConfigureAwait(true);

            SetPdfLoading(true, "Anzeige wird aufgebaut…");
            await Dispatcher.Yield(DispatcherPriority.Render);

            _suppressTripCollectionRebuild = true;
            try
            {
                _trips.Clear();
                foreach (var card in prepared)
                {
                    _trips.Add(card);
                }
            }
            finally
            {
                _suppressTripCollectionRebuild = false;
            }

            if (prepared.Count == 0)
            {
                _statusText.Text = parsed.Warning ??
                                   "Keine Fahrten erkannt – bitte manuell ergänzen oder „+ Fahrt“.";
            }
            else
            {
                _statusText.Text =
                    $"{prepared.Count} Fahrt(en) aus PDF geladen" +
                    (string.IsNullOrWhiteSpace(parsed.Warning) ? "." : $" – {parsed.Warning}");
            }

            // UI aufbauen, Haltestellen sind bereits berechnet
            await RebuildTripCardsAsync(resyncTemplateStops: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _errorText.Text = $"PDF konnte nicht gelesen werden: {ex.Message}";
            _statusText.Text = string.Empty;
        }
        finally
        {
            SetPdfLoading(false);
        }
    }

    private void ApplyParseHeaderFields(UmlaufPdfReader.ParseResult parsed)
    {
        _errorText.Text = string.Empty;
        if (!string.IsNullOrWhiteSpace(parsed.LineCourse))
        {
            _defaultLineCourseBox.Text = parsed.LineCourse;
        }

        if (!string.IsNullOrWhiteSpace(parsed.AusfahrtTime))
        {
            _ausfahrtTimeBox.Text = parsed.AusfahrtTime;
        }

        if (!string.IsNullOrWhiteSpace(parsed.EinfahrtTime))
        {
            _einfahrtTimeBox.Text = parsed.EinfahrtTime;
        }

        if (parsed.OperatingDays.Count > 0)
        {
            var set = parsed.OperatingDays.ToHashSet();
            foreach (var check in _operatingDayChecks)
            {
                if (check.Tag is DutyOperatingDay day)
                {
                    check.IsChecked = set.Contains(day);
                }
            }
        }
    }

    /// <summary>Fahrten inkl. Haltestellenketten im Hintergrund vorbereiten (ohne UI-Zugriffe).</summary>
    private List<TripCardModel> PrepareTripsFromParse(
        UmlaufPdfReader.ParseResult parsed,
        IReadOnlyList<DutyOperatingDay> defaultDays,
        string defaultLine,
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes)
    {
        var result = new List<TripCardModel>();
        _templateGuessCache.Clear();
        if (parsed.Trips.Count == 0)
        {
            return result;
        }

        foreach (var t in parsed.Trips)
        {
            // 681/682 im selben Umlauf: Hästen↔Hbf bleibt 681, Brockenberg↔Hbf bleibt 682.
            // Andere Datei-Linie (z. B. „Mo 683-01“) gewinnt gegen einen Geographie-Hint.
            var headerLine = parsed.LineCourse ?? defaultLine;
            var hintLine = t.LineCourseHint ?? headerLine;
            var pairHint = IsHbfPairCourse(headerLine) && IsHbfPairCourse(hintLine);
            var stacked696 = Is693StackedBranchHint(headerLine, hintLine);
            if (!pairHint &&
                !stacked696 &&
                !string.IsNullOrWhiteSpace(headerLine) &&
                !string.IsNullOrWhiteSpace(hintLine) &&
                !LinePrefixesEqual(headerLine, hintLine))
            {
                hintLine = headerLine;
            }

            var template = GuessTemplate(
                               hintLine,
                               t.Direction,
                               t.StartStopHint,
                               t.EndStopHint,
                               t.TimingPoints)
                           ?? GuessTemplate(
                               headerLine,
                               t.Direction,
                               t.StartStopHint,
                               t.EndStopHint,
                               t.TimingPoints)
                           ?? GuessTemplate(
                               string.Empty,
                               t.Direction,
                               t.StartStopHint,
                               t.EndStopHint,
                               t.TimingPoints)
                           ?? _routes[0];
            var parsedKey = RouteDisplayHelper.Parse(template);
            // Kurs aus der Datei (/02). Bei 681/682 die Fahrt-Linie behalten, sonst den Dateikopf.
            var lineCourse = RouteDisplayHelper.NormalizeLineCourse(
                (pairHint || stacked696) && !string.IsNullOrWhiteSpace(hintLine)
                    ? WithHeaderCourseSuffix(hintLine, headerLine)
                    : !string.IsNullOrWhiteSpace(headerLine)
                        ? headerLine
                        : !string.IsNullOrWhiteSpace(hintLine)
                            ? hintLine
                            : defaultLine);
            if (string.IsNullOrWhiteSpace(lineCourse))
            {
                lineCourse = RouteDisplayHelper.NormalizeLineCourse(parsedKey.LineCourse);
            }

            var direction = InferDirectionFromTemplate(
                template, t.StartStopHint, t.EndStopHint, t.TimingPoints, t.Direction);
            var card = new TripCardModel
            {
                Direction = direction,
                TemplateRouteKey = template,
                LineCourse = lineCourse,
                TripNumber = t.TripNumber,
                StartTime = t.StartTime,
                EndTime = t.EndTime ?? string.Empty,
                SectionHint = t.SectionHint ?? string.Empty,
                StartStopHint = t.StartStopHint ?? string.Empty,
                EndStopHint = t.EndStopHint ?? string.Empty,
                TimingPoints = t.TimingPoints.ToList(),
                MarkColor = t.MarkColorR is byte mr && t.MarkColorG is byte mg && t.MarkColorB is byte mb
                    ? Color.FromRgb(mr, mg, mb)
                    : null
            };
            card.SetOperatingDays(defaultDays);
            card.Stops = UmlaufImportPlanner.BuildStopsFromTemplate(
                _editor,
                card.TemplateRouteKey,
                card.StartTime,
                string.IsNullOrWhiteSpace(card.EndTime) ? null : card.EndTime,
                preserveFrom: null,
                card.StartStopHint,
                card.EndStopHint,
                card.TimingPoints,
                typicalMinutes);
            card.RouteName = FormatUmlaufRouteName(
                card.LineCourse,
                card.StartStopHint,
                card.EndStopHint,
                card.Stops,
                parsedKey.Name);
            result.Add(card);
        }

        return result;
    }

    private async Task RebuildTripCardsAsync(bool resyncTemplateStops = true)
    {
        if (_isRebuildingCards)
        {
            return;
        }

        _isRebuildingCards = true;
        _tripDayChecks.Clear();
        try
        {
            if (resyncTemplateStops)
            {
                for (var i = 0; i < _trips.Count; i++)
                {
                    EnsureStops(_trips[i]);
                    if (i % 2 == 1)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
            }

            SyncDepotStopsAcrossTrips();

            var panel = new StackPanel();
            for (var i = 0; i < _trips.Count; i++)
            {
                panel.Children.Add(BuildTripCard(_trips[i], i));
                if (i % 2 == 1)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            if (_trips.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Noch keine Fahrten. PDF laden oder „+ Fahrt“.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            _tripHost.Items.Clear();
            _tripHost.Items.Add(panel);
        }
        finally
        {
            _isRebuildingCards = false;
        }
    }

    private static bool IsHbfPairCourse(string? lineCourse)
    {
        var prefix = LinePrefix(lineCourse);
        return prefix is "681" or "682";
    }

    /// <summary>
    /// Gestapelte Linien und Spaltenfahrpläne (L.685 neben L.691).
    /// Der Dateikopf bleibt, die Fahrt behält ihre eigene Linie und damit die passende Haltestellenkette.
    /// </summary>
    private static bool Is693StackedBranchHint(string? headerLine, string? hintLine)
    {
        var header = LinePrefix(headerLine);
        var hint = LinePrefix(hintLine);
        if (string.IsNullOrEmpty(header) ||
            string.IsNullOrEmpty(hint) ||
            string.Equals(header, hint, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsStackedComboLine(header) && IsStackedComboLine(hint);
    }

    private static bool IsStackedComboLine(string? prefix) =>
        prefix is "685" or "686" or "687" or "689" or "691" or "692" or "693" or "694" or "696" or "697" or "698" or "699";

    /// <summary>
    /// Nur Vorlagen dieser Linie. Die volle Routenliste (über tausend Einträge)
    /// hängt das Dropdown auf, weil jeder Eintrag sofort gezeichnet wird.
    /// </summary>
    private List<string> TemplateChoicesFor(TripCardModel trip)
    {
        var prefix = LinePrefix(trip.LineCourse);
        var matches = string.IsNullOrEmpty(prefix)
            ? []
            : _routes.Where(r =>
                {
                    var lc = RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(r).LineCourse);
                    return string.Equals(LinePrefix(lc), prefix, StringComparison.OrdinalIgnoreCase);
                }).ToList();

        if (!string.IsNullOrWhiteSpace(trip.TemplateRouteKey) &&
            !matches.Contains(trip.TemplateRouteKey, StringComparer.Ordinal))
        {
            matches.Insert(0, trip.TemplateRouteKey);
        }

        if (matches.Count == 0 && _routes.Count > 0)
        {
            matches.Add(_routes[0]);
        }

        return matches;
    }

    /// <summary>Kurs aus dem Dateikopf an die Fahrt-Linie hängen, wenn der Hint keinen Kurs trägt.</summary>
    private static string WithHeaderCourseSuffix(string lineCourse, string? headerLine)
    {
        var line = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        if (string.IsNullOrWhiteSpace(line) || line.Contains('/'))
        {
            return line;
        }

        var header = RouteDisplayHelper.NormalizeLineCourse(headerLine);
        if (string.IsNullOrWhiteSpace(header))
        {
            return line;
        }

        var slash = header.IndexOf('/');
        return slash > 0 && slash < header.Length - 1
            ? line + header[slash..]
            : line;
    }

    private static string? LinePrefix(string? lineCourse)
    {
        if (string.IsNullOrWhiteSpace(lineCourse))
        {
            return null;
        }

        var n = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        var slash = n.IndexOf('/');
        return slash > 0 ? n[..slash] : n;
    }

    private static bool LinePrefixesEqual(string? a, string? b)
    {
        var pa = LinePrefix(a);
        var pb = LinePrefix(b);
        return !string.IsNullOrWhiteSpace(pa) &&
               string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
    }

    private string? GuessTemplate(
        string lineCourse,
        UmlaufImportPlanner.Direction direction,
        string? startStopHint = null,
        string? endStopHint = null,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? timingPoints = null)
    {
        var needle = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        var guessKey = string.Concat(
            needle,
            "\u001e",
            startStopHint,
            "\u001e",
            endStopHint,
            "\u001e",
            timingPoints is null
                ? string.Empty
                : string.Join('\u001f', timingPoints.Select(p => p.StopHint)));
        if (_templateGuessCache.TryGetValue(guessKey, out var cachedTemplate))
        {
            return cachedTemplate;
        }

        IEnumerable<string> pool = _routes;
        if (!string.IsNullOrWhiteSpace(needle))
        {
            var linePrefix = needle.Split('/')[0];
            // 681/682 teilen sich oft denselben Umlauf – Vorlage mit bester Halt-Abdeckung wählen
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { linePrefix };
            if (linePrefix.Equals("681", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add("682");
            }
            else if (linePrefix.Equals("682", StringComparison.OrdinalIgnoreCase))
            {
                prefixes.Add("681");
            }

            var filtered = _routes
                .Where(r =>
                {
                    var lc = RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(r).LineCourse);
                    var p = lc.Split('/')[0];
                    return prefixes.Contains(p) ||
                           lc.Equals(needle, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (filtered.Count > 0)
            {
                pool = filtered;
            }
        }

        var list = pool.ToList();
        if (list.Count == 0)
        {
            return _routes[0];
        }

        string? bestKey = null;
        var bestScore = int.MinValue;
        foreach (var key in list)
        {
            var stops = _editor.GetStops(key).Where(s => !s.IsWaypoint).ToList();
            if (stops.Count == 0)
            {
                continue;
            }

            var score = 0;
            var startIdx = UmlaufImportPlanner.FindStopIndexByHint(stops, startStopHint);
            var endIdx = UmlaufImportPlanner.FindLastStopIndexByHint(stops, endStopHint);
            if (startIdx >= 0)
            {
                // Flat-Bonus: früher Index nicht belohnen (sonst gewinnen Skelett-/06-Vorlagen)
                score += 80;
            }

            if (endIdx >= 0)
            {
                score += 50;
                if (startIdx >= 0)
                {
                    // Richtung Start→Ende belohnen; Span nicht mehr pauschal länger = besser
                    // (sonst gewinnt z. B. Vohwinkel→Burg über Hbf gegen die direkte 683)
                    score += endIdx >= startIdx ? 60 : 20;
                    var span = Math.Abs(endIdx - startIdx) + 1;
                    score += Math.Min(span, 40);
                }

                // PDF-Ende = Vorlagen-Endhalt (Schleife/Burger Bf.) → Ziel der Starthaltestelle passt
                if (endIdx == stops.Count - 1 ||
                    (endIdx >= 0 && stops[endIdx].IsEndStop))
                {
                    score += 100;
                }
            }

            // PDF-Planfolge vs. Vorlage derselben Linie (Dichte + Malus ungeplanter Hbf/Termini)
            score += UmlaufImportPlanner.ScorePlanSequenceFit(
                stops, startStopHint, endStopHint, timingPoints);

            // Planzeiten-Abdeckung entscheidet die Vorlage (stärker als PDF-Kopf/Farbband)
            if (timingPoints is { Count: > 0 })
            {
                score += ScoreTimingPointCoverage(stops, timingPoints) * 40;
            }

            // Detaillierte Vorlage leicht bevorzugen (bei gleicher Taktpunkt-Abdeckung)
            score += Math.Min(stops.Count, 20);

            // PDF-Kurs nur leichter Bonus bei gleicher Abdeckung
            var keyLc = RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(key).LineCourse);
            if (!string.IsNullOrWhiteSpace(needle) &&
                keyLc.Equals(needle, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }

            var display = RouteDisplayHelper.Parse(key).Name;
            if (!string.IsNullOrWhiteSpace(endStopHint) &&
                UmlaufImportPlanner.FindStopIndexByHint(
                    [new RouteStopItem { Name = display }],
                    endStopHint) >= 0)
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(startStopHint) &&
                UmlaufImportPlanner.FindStopIndexByHint(
                    [new RouteStopItem { Name = display }],
                    startStopHint) >= 0)
            {
                score += 15;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestKey = key;
            }
        }

        string chosen;
        if (bestKey is not null && bestScore > 0)
        {
            chosen = bestKey;
        }
        else if (!string.IsNullOrWhiteSpace(_initialRouteKey) &&
                 list.Contains(_initialRouteKey, StringComparer.Ordinal))
        {
            chosen = _initialRouteKey;
        }
        else
        {
            chosen = list[direction == UmlaufImportPlanner.Direction.Hin ? 0 : Math.Min(1, list.Count - 1)];
        }

        _templateGuessCache[guessKey] = chosen;
        return chosen;
    }

    /// <summary>
    /// Richtung aus Vorlagen-Index von Start→Ende (bzw. Planzeiten), Fallback PDF.
    /// </summary>
    private UmlaufImportPlanner.Direction InferDirectionFromTemplate(
        string templateKey,
        string? startStopHint,
        string? endStopHint,
        IReadOnlyList<UmlaufPdfReader.TimingPoint>? timingPoints,
        UmlaufImportPlanner.Direction pdfFallback)
    {
        var stops = _editor.GetStops(templateKey).Where(s => !s.IsWaypoint).ToList();
        if (stops.Count == 0)
        {
            return pdfFallback;
        }

        var startIdx = UmlaufImportPlanner.FindStopIndexByHint(stops, startStopHint);
        var endIdx = UmlaufImportPlanner.FindLastStopIndexByHint(stops, endStopHint);
        if (startIdx >= 0 && endIdx >= 0 && startIdx != endIdx)
        {
            return startIdx < endIdx
                ? UmlaufImportPlanner.Direction.Hin
                : UmlaufImportPlanner.Direction.Rueck;
        }

        if (timingPoints is { Count: >= 2 })
        {
            var ordered = timingPoints
                .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _))
                .OrderBy(p =>
                {
                    RouteScheduleTimeCalculator.TryParseTime(p.Time, out var tm);
                    return tm.Hour * 60 + tm.Minute;
                })
                .ToList();
            if (ordered.Count >= 2)
            {
                var a = UmlaufImportPlanner.FindStopIndexByHint(stops, ordered[0].StopHint);
                var b = UmlaufImportPlanner.FindLastStopIndexByHint(stops, ordered[^1].StopHint);
                if (a >= 0 && b >= 0 && a != b)
                {
                    return a < b
                        ? UmlaufImportPlanner.Direction.Hin
                        : UmlaufImportPlanner.Direction.Rueck;
                }
            }
        }

        return pdfFallback;
    }

    /// <summary>Wie viele PDF-Planzeiten in aufsteigender Reihenfolge auf die Vorlage passen.</summary>
    private static int ScoreTimingPointCoverage(
        IReadOnlyList<RouteStopItem> stops,
        IReadOnlyList<UmlaufPdfReader.TimingPoint> timingPoints)
    {
        var matched = 0;
        var prev = -1;
        foreach (var point in timingPoints
                     .Where(p => !string.IsNullOrWhiteSpace(p.StopHint) &&
                                 !string.IsNullOrWhiteSpace(p.Time))
                     .OrderBy(p =>
                     {
                         RouteScheduleTimeCalculator.TryParseTime(p.Time, out var t);
                         return t.Hour * 60 + t.Minute;
                     }))
        {
            // Nur vorwärts ab letztem Treffer – False Friends vor dem echten Halt nicht zählen
            var slice = prev < 0
                ? stops
                : stops.Skip(prev + 1).ToList();
            var localIdx = UmlaufImportPlanner.FindStopIndexByHint(slice, point.StopHint);
            if (localIdx < 0)
            {
                continue;
            }

            var idx = prev < 0 ? localIdx : prev + 1 + localIdx;
            matched++;
            prev = idx;
        }

        return matched;
    }

    private TripCardModel CreateEmptyTrip(UmlaufImportPlanner.Direction direction, bool buildStops = true)
    {
        var template = GuessTemplate(_defaultLineCourseBox?.Text ?? string.Empty, direction)
                       ?? _routes[0];
        var parsedKey = RouteDisplayHelper.Parse(template);
        var lineCourse = string.IsNullOrWhiteSpace(_defaultLineCourseBox?.Text)
            ? RouteDisplayHelper.NormalizeLineCourse(parsedKey.LineCourse)
            : RouteDisplayHelper.NormalizeLineCourse(_defaultLineCourseBox!.Text);
        var card = new TripCardModel
        {
            Direction = direction,
            TemplateRouteKey = template,
            LineCourse = lineCourse,
            TripNumber = string.Empty,
            StartTime = string.Empty,
            EndTime = string.Empty
        };
        card.SetOperatingDays(GetDefaultOperatingDays());
        if (buildStops)
        {
            EnsureStops(card, forceRebuild: true);
        }

        card.RouteName = FormatUmlaufRouteName(
            card.LineCourse,
            card.StartStopHint,
            card.EndStopHint,
            card.Stops,
            parsedKey.Name);
        return card;
    }

    /// <summary>Routenname immer „{Linie/Kurs} {Von} > {Nach}“, z. B. „681/01 Hbf > GWP“.</summary>
    private static string FormatUmlaufRouteName(
        string? lineCourse,
        string? startHint,
        string? endHint,
        IReadOnlyList<RouteStopItem>? stops = null,
        string? fallbackPath = null)
    {
        var lc = RouteDisplayHelper.NormalizeLineCourse(lineCourse ?? string.Empty);
        var from = ShortStopLabelForRouteName(startHint);
        var to = ShortStopLabelForRouteName(endHint);

        if ((string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) &&
            stops is { Count: > 0 })
        {
            var passenger = stops
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) &&
                            !UmlaufImportPlanner.IsSwsDepotStopName(s.Name))
                .Select(s => s.Name)
                .ToList();
            if (passenger.Count > 0)
            {
                if (string.IsNullOrEmpty(from))
                {
                    from = ShortStopLabelForRouteName(passenger[0]);
                }

                if (string.IsNullOrEmpty(to))
                {
                    to = ShortStopLabelForRouteName(passenger[^1]);
                }
            }
        }

        string path;
        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
        {
            path = $"{from} > {to}";
        }
        else
        {
            path = StripLineCoursePrefix(fallbackPath ?? string.Empty);
            path = NormalizeRoutePathSeparator(path);
        }

        if (string.IsNullOrWhiteSpace(lc))
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return lc;
        }

        return $"{lc} {path}";
    }

    private static string ApplyLineCourseToRouteName(string? routeName, string? lineCourse)
    {
        var lc = RouteDisplayHelper.NormalizeLineCourse(lineCourse ?? string.Empty);
        var path = StripLineCoursePrefix(routeName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(lc))
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return lc;
        }

        return $"{lc} {path}";
    }

    private static string StripLineCoursePrefix(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0)
        {
            return string.Empty;
        }

        var m = Regex.Match(n, @"^\d{2,4}(?:/\d{1,2})?\s+(.*)$");
        return m.Success ? m.Groups[1].Value.Trim() : n;
    }

    private static string NormalizeRoutePathSeparator(string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (p.Length == 0)
        {
            return string.Empty;
        }

        p = Regex.Replace(p, @"\s*[–—−]\s*", " > ");
        p = Regex.Replace(p, @"\s+-\s+", " > ");
        p = Regex.Replace(p, @"\s*>\s*", " > ");
        return p.Trim();
    }

    private static string ShortStopLabelForRouteName(string? hintOrName)
    {
        var cleaned = UmlaufPdfReader.CleanStopHint(hintOrName) ?? (hintOrName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var h = cleaned.ToLowerInvariant();
        if (h.Contains("hauptbahnhof", StringComparison.Ordinal) ||
            h.Equals("hbf", StringComparison.Ordinal) ||
            h.EndsWith(" hbf", StringComparison.Ordinal) ||
            h.Contains(" hbf", StringComparison.Ordinal))
        {
            return "Hbf";
        }

        if ((h.Contains("graf", StringComparison.Ordinal) &&
             (h.Contains("wilh", StringComparison.Ordinal) || h.Contains("platz", StringComparison.Ordinal))) ||
            h.Contains("gwp", StringComparison.Ordinal) ||
            h.Equals("graf-wilhelm-platz", StringComparison.Ordinal) ||
            h.Contains("graf-wilh", StringComparison.Ordinal))
        {
            return "GWP";
        }

        if (h.Contains("brockenberg", StringComparison.Ordinal))
        {
            return "Brockenberg";
        }

        if (h.Contains("hästen", StringComparison.Ordinal) ||
            h.Contains("haesten", StringComparison.Ordinal) ||
            h.Contains("hasten", StringComparison.Ordinal))
        {
            return "Hästen";
        }

        if (h.Contains("voh", StringComparison.Ordinal) && h.Contains("bahnhof", StringComparison.Ordinal))
        {
            return "Voh.-Bahnhof";
        }

        if (h.Contains("burger", StringComparison.Ordinal) && h.Contains("bahnhof", StringComparison.Ordinal))
        {
            return "Burger Bahnhof";
        }

        if (h.Contains("krahen", StringComparison.Ordinal) || h.Contains("krähen", StringComparison.Ordinal))
        {
            return h.Contains("schleife", StringComparison.Ordinal) ? "Schleife Krahenhöhe" : "Krahenhöhe";
        }

        if (h.Contains("mangenberg", StringComparison.Ordinal))
        {
            return "Mangenberg";
        }

        // Anzeigename: Komma-Ort abschneiden, Wörter kürzen
        var display = (hintOrName ?? cleaned).Trim();
        var comma = display.IndexOf(',');
        if (comma > 0)
        {
            display = display[..comma].Trim();
        }

        var words = display
            .Split([' ', '/', '-', '–', '—'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 1 && !w.Equals("an", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("ab", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())
            .ToList();
        return words.Count > 0 ? string.Join(" ", words) : display;
    }

    private List<DutyOperatingDay> GetDefaultOperatingDays() =>
        _operatingDayChecks
            .Where(c => c.IsChecked == true && c.Tag is DutyOperatingDay)
            .Select(c => (DutyOperatingDay)c.Tag!)
            .Distinct()
            .ToList();

    private void RefreshTripNumbers()
    {
        // Nur UI neu zeichnen – Haltestellenketten nicht neu berechnen
        RebuildTripCards(resyncTemplateStops: false);
    }

    private void RunImport()
    {
        _errorText.Text = string.Empty;
        if (!TryBuildRequest(out var request, out var error))
        {
            _errorText.Text = error ?? "Eingabe ungültig.";
            return;
        }

        try
        {
            var result = UmlaufImportPlanner.Import(_editor, request);
            CreatedFirstRouteKey = result.FirstRouteKey;
            CreatedCount = result.CreatedCount;
            LinkedRouteChangeCount = result.LinkedRouteChangeCount;
            Warnings = result.Warnings;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
        }
    }

    private bool TryBuildRequest(out UmlaufImportPlanner.Request request, out string? error)
    {
        request = null!;
        error = null;

        if (_trips.Count == 0)
        {
            error = "Keine Fahrten vorhanden.";
            return false;
        }

        var defaultDays = GetDefaultOperatingDays();
        var defaultLine = string.IsNullOrWhiteSpace(_defaultLineCourseBox.Text)
            ? null
            : _defaultLineCourseBox.Text.Trim();

        var specs = new List<UmlaufImportPlanner.TripSpec>();
        for (var i = 0; i < _trips.Count; i++)
        {
            var t = _trips[i];
            if (string.IsNullOrWhiteSpace(t.TemplateRouteKey))
            {
                error = $"Fahrt {i + 1}: Vorlage wählen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(t.TripNumber))
            {
                error = $"Fahrt {i + 1}: Fahrtnummer fehlt.";
                return false;
            }

            if (!RouteScheduleTimeCalculator.TryParseTime(t.StartTime, out _))
            {
                error = $"Fahrt {i + 1}: ungültige Startzeit.";
                return false;
            }

            var tripDays = t.OperatingDays.Count > 0
                ? t.OperatingDays.ToList()
                : defaultDays;
            if (tripDays.Count == 0)
            {
                error = $"Fahrt {i + 1}: bitte mindestens einen Verkehrstag wählen.";
                return false;
            }

            Dictionary<string, string>? anchors = null;
            if (!string.IsNullOrWhiteSpace(t.EndTime) &&
                RouteScheduleTimeCalculator.TryParseTime(t.EndTime, out _))
            {
                // Endzeit als Anker auf letzte Vorlage-Hst legen (Name erst beim Import unbekannt → nur Info)
            }

            // Vor Import: Ziele aus gewählter Vorlage, nicht aus früher falsch geratenen Ketten
            if (!t.StopsLocked)
            {
                EnsureStops(t, forceRebuild: true, keepStopEdits: false);
            }

            var planAnchors = t.TimingPoints.Count > 0
                ? t.TimingPoints
                    .GroupBy(p => p.StopHint, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => RouteScheduleTimeCalculator.NormalizeTimeInput(g.Last().Time),
                        StringComparer.OrdinalIgnoreCase)
                : anchors;
            specs.Add(new UmlaufImportPlanner.TripSpec(
                t.Direction,
                t.TripNumber.Trim(),
                RouteScheduleTimeCalculator.NormalizeTimeInput(t.StartTime),
                planAnchors,
                t.TemplateRouteKey,
                string.IsNullOrWhiteSpace(t.RouteName) ? null : t.RouteName.Trim(),
                string.IsNullOrWhiteSpace(t.LineCourse) ? defaultLine : t.LineCourse.Trim(),
                string.IsNullOrWhiteSpace(t.EndTime)
                    ? null
                    : RouteScheduleTimeCalculator.NormalizeTimeInput(t.EndTime),
                PreparedStops: t.Stops.Count > 0
                    ? t.Stops.Select(s => s.Clone()).ToList()
                    : null,
                OperatingDays: tripDays));
        }

        var firstTemplate = specs[0].TemplateRouteKey ?? _routes[0];
        var rueck = specs.FirstOrDefault(s => s.Direction == UmlaufImportPlanner.Direction.Rueck)
            ?.TemplateRouteKey;

        request = new UmlaufImportPlanner.Request(
            firstTemplate,
            rueck,
            RouteNameOverride: null,
            LineCourseOverride: defaultLine,
            defaultDays,
            specs,
            LinkRouteChanges: _linkRouteChangesBox.IsChecked == true,
            AusfahrtTime: string.IsNullOrWhiteSpace(_ausfahrtTimeBox.Text) ? null : _ausfahrtTimeBox.Text.Trim(),
            EinfahrtTime: string.IsNullOrWhiteSpace(_einfahrtTimeBox.Text) ? null : _einfahrtTimeBox.Text.Trim(),
            AddAusfahrt: true);

        return UmlaufImportPlanner.TryValidateRequest(_editor, request, out error);
    }

    private static Grid CreateFieldRow(int columns = 2)
    {
        var grid = new Grid();
        for (var i = 0; i < columns; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        return grid;
    }

    private static void AddField(Grid row, int column, string label, UIElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = LabelForeground,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        row.Children.Add(panel);
    }

    private static TextBox BindInput(string initial, Action<string> onChanged)
    {
        var box = MakeInput(initial);
        box.TextChanged += (_, _) => onChanged(box.Text);
        return box;
    }

    private static ComboBox MakeDarkComboBox()
    {
        var combo = new ComboBox
        {
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 34
        };
        if (Application.Current?.TryFindResource("SmartDarkComboBox") is Style darkStyle)
        {
            combo.Style = darkStyle;
        }
        else
        {
            combo.Foreground = Brushes.White;
            combo.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));
            combo.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
            combo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28))),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))
                }
            };
        }

        return combo;
    }

    private static TextBox MakeInput(string text) =>
        new()
        {
            Text = text,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };

    private static Button MakeAccentButton(string text) =>
        new()
        {
            Content = text,
            MinHeight = 34,
            Padding = new Thickness(14, 6, 14, 6),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

    private sealed class TripCardModel : INotifyPropertyChanged
    {
        private UmlaufImportPlanner.Direction _direction;
        private string _templateRouteKey = string.Empty;
        private string _routeName = string.Empty;
        private string _lineCourse = string.Empty;
        private string _tripNumber = string.Empty;
        private string _startTime = string.Empty;
        private string _endTime = string.Empty;
        private string _sectionHint = string.Empty;
        private string _startStopHint = string.Empty;
        private string _endStopHint = string.Empty;

        /// <summary>Planzeiten aus der PDF für diese Fahrt.</summary>
        public List<UmlaufPdfReader.TimingPoint> TimingPoints { get; set; } = [];

        public UmlaufImportPlanner.Direction Direction
        {
            get => _direction;
            set => SetField(ref _direction, value);
        }

        public string TemplateRouteKey
        {
            get => _templateRouteKey;
            set => SetField(ref _templateRouteKey, value);
        }

        public string RouteName
        {
            get => _routeName;
            set => SetField(ref _routeName, value);
        }

        public string LineCourse
        {
            get => _lineCourse;
            set => SetField(ref _lineCourse, value);
        }

        public string TripNumber
        {
            get => _tripNumber;
            set => SetField(ref _tripNumber, value);
        }

        public string StartTime
        {
            get => _startTime;
            set => SetField(ref _startTime, value);
        }

        public string EndTime
        {
            get => _endTime;
            set => SetField(ref _endTime, value);
        }

        public string SectionHint
        {
            get => _sectionHint;
            set => SetField(ref _sectionHint, value);
        }

        public string StartStopHint
        {
            get => _startStopHint;
            set => SetField(ref _startStopHint, value);
        }

        public string EndStopHint
        {
            get => _endStopHint;
            set => SetField(ref _endStopHint, value);
        }

        /// <summary>Haltestellen mit Zeiten/Zielen für Anzeige und Import.</summary>
        public List<RouteStopItem> Stops { get; set; } = [];

        /// <summary>PDF-Highlight-Farbe (gelb/grün) zur Linien-Erkennung.</summary>
        public Color? MarkColor { get; set; }

        /// <summary>true = Nutzer hat Halte gelöscht/angepasst – nicht aus Vorlage neu bauen.</summary>
        public bool StopsLocked { get; set; }

        public HashSet<DutyOperatingDay> OperatingDays { get; } = [];

        public void SetOperatingDays(IEnumerable<DutyOperatingDay> days)
        {
            OperatingDays.Clear();
            foreach (var day in days)
            {
                OperatingDays.Add(day);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
