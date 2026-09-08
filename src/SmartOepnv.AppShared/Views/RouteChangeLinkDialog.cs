using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Schnelldialog: Routenwechsel an der Endhaltestelle (Standard + datumsabhängige Abweichungen).
/// </summary>
public sealed class RouteChangeLinkDialog : Window
{
    private static readonly Brush PanelBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush CardBackground = new SolidColorBrush(Color.FromRgb(0x14, 0x23, 0x3A));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
    private static readonly Brush ButtonBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));
    private static readonly Brush MutedForeground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB));
    private static readonly Brush InputFg = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

    private readonly EditableRoutePackage _editor;
    private readonly string _routeKey;
    private readonly RouteStopItem _endStop;
    private readonly ObservableCollection<string> _tripOptions = [];
    private readonly ObservableCollection<RouteChangeDatedTargetRow> _datedRows = [];

    private readonly CheckBox _enabledBox;
    private readonly StackPanel _fieldsPanel;
    private readonly TextBox _quickEntryBox;
    private readonly ComboBox _standardTripCombo;
    private readonly TextBlock _standardTripLabel;
    private readonly StackPanel _datedHost;
    private readonly TextBlock _statusText;

    public RouteChangeLinkDialog(EditableRoutePackage editor, string routeKey)
    {
        _editor = editor;
        _routeKey = routeKey.Trim();
        _endStop = ResolveEndStop(editor, _routeKey)
                   ?? throw new InvalidOperationException(
                       "Keine Endhaltestelle für diese Route gefunden.");

        Title = "Routenwechsel verknüpfen";
        Width = 560;
        MinWidth = 480;
        MinHeight = 420;
        Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.88);
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = PanelBackground;

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        LoadTripOptions();
        LoadDatedRowsFromStop();
        _datedRows.CollectionChanged += OnDatedRowsChanged;

        var root = new DockPanel { Margin = new Thickness(16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var ok = new Button
        {
            Content = "Übernehmen",
            MinWidth = 120,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        ok.Click += (_, _) =>
        {
            ApplyToEndStop();
            DialogResult = true;
            Close();
        };

        var cancel = new Button
        {
            Content = "Abbrechen",
            MinWidth = 100,
            Height = 34,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Routenwechsel verknüpfen",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var routeLabel = RouteDisplayHelper.ToDisplayString(RouteDisplayHelper.Parse(_routeKey));
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(routeLabel) ? _routeKey : routeLabel,
            Foreground = AccentBrush,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        content.Children.Add(new TextBlock
        {
            Text = $"Endhaltestelle: {_endStop.Name}",
            Foreground = MutedForeground,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _enabledBox = new CheckBox
        {
            Content = "Automatischer Routenwechsel",
            IsChecked = _endStop.RouteChangeEnabled,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _enabledBox.Checked += (_, _) => UpdateFieldsVisibility();
        _enabledBox.Unchecked += (_, _) => UpdateFieldsVisibility();
        content.Children.Add(_enabledBox);

        _fieldsPanel = new StackPanel();

        _fieldsPanel.Children.Add(new TextBlock
        {
            Text = "Standard-Folgefahrt (Tage ohne Ausnahme):",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
            Opacity = 0.9
        });

        var quickGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        quickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _quickEntryBox = MakeInputBox();
        _quickEntryBox.MaxLength = 4;
        _quickEntryBox.ToolTip = "Fahrtnummer (z. B. 3112)";
        Grid.SetColumn(_quickEntryBox, 0);
        quickGrid.Children.Add(_quickEntryBox);

        var applyQuick = MakeAccentButton("Übernehmen");
        applyQuick.Margin = new Thickness(8, 0, 0, 0);
        applyQuick.ToolTip = "Fahrt per Nummer wählen (gleiche Linie/Kurs wie aktuelle Route)";
        applyQuick.Click += (_, _) => ApplyQuickTripNumber();
        Grid.SetColumn(applyQuick, 1);
        quickGrid.Children.Add(applyQuick);
        _fieldsPanel.Children.Add(quickGrid);

        var standardGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        standardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        standardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var initialStandard = RouteStopEditorCatalog.ToComboLabel(
            _endStop.SelectedLineCourseTrip,
            RouteStopEditorCatalog.NoLineCourseTripLabel);
        EnsureTripInOptions(initialStandard);

        _standardTripLabel = new TextBlock
        {
            Text = initialStandard,
            Foreground = MutedForeground,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 0, 0, 0)
        };

        _standardTripCombo = new ComboBox
        {
            ItemsSource = _tripOptions,
            SelectedItem = initialStandard,
            MinWidth = 220,
            MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center
        };
        _standardTripCombo.SelectionChanged += (_, _) =>
        {
            _standardTripLabel.Text = _standardTripCombo.SelectedItem as string ?? string.Empty;
        };
        Grid.SetColumn(_standardTripCombo, 0);
        standardGrid.Children.Add(_standardTripCombo);

        Grid.SetColumn(_standardTripLabel, 1);
        standardGrid.Children.Add(_standardTripLabel);
        _fieldsPanel.Children.Add(standardGrid);

        _fieldsPanel.Children.Add(new TextBlock
        {
            Text = "Abweichende Folgefahrt an einzelnen Betriebstagen:",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _fieldsPanel.Children.Add(new TextBlock
        {
            Text = "Pro Zeile: Datum(e) → Folgefahrt. Mehrere Tage kommagetrennt " +
                    "(z. B. 10.08-14.08, 17.08 oder 20.08). Ohne Eintrag gilt die Standard-Folgefahrt. " +
                    "Mit + weitere Abweichungen anlegen.",
            FontSize = 11,
            Opacity = 0.75,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _datedHost = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        _fieldsPanel.Children.Add(_datedHost);
        RebuildDatedHost();

        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var addButton = MakeAccentButton("+");
        addButton.Width = 40;
        addButton.MinHeight = 34;
        addButton.Padding = new Thickness(0);
        addButton.FontSize = 18;
        addButton.FontWeight = FontWeights.Bold;
        addButton.ToolTip = "Weitere Abweichung hinzufügen";
        addButton.Click += (_, _) => AddDatedRow();
        addRow.Children.Add(addButton);
        addRow.Children.Add(new TextBlock
        {
            Text = "Weitere Abweichung hinzufügen",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = Brushes.White,
            Opacity = 0.85
        });
        _fieldsPanel.Children.Add(addRow);

        _statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _fieldsPanel.Children.Add(_statusText);

        content.Children.Add(_fieldsPanel);
        scroll.Content = content;
        root.Children.Add(scroll);
        Content = root;

        UpdateFieldsVisibility();
    }

    public static bool TryShow(
        Window? owner,
        EditableRoutePackage editor,
        string routeKey,
        out string? error)
    {
        error = null;
        var key = routeKey?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(key))
        {
            error = "Keine Route ausgewählt.";
            return false;
        }

        if (ResolveEndStop(editor, key) is null)
        {
            error = "Diese Route hat keine Haltestelle (Endhaltestelle fehlt).";
            return false;
        }

        try
        {
            var dialog = new RouteChangeLinkDialog(editor, key) { Owner = owner };
            return dialog.ShowDialog() == true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static RouteStopItem? ResolveEndStop(EditableRoutePackage editor, string routeKey)
    {
        var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
        if (stops.Count == 0)
        {
            return null;
        }

        return stops.LastOrDefault(s => s.IsEndStop) ?? stops[^1];
    }

    private void LoadTripOptions()
    {
        _tripOptions.Clear();
        _tripOptions.Add(RouteStopEditorCatalog.NoLineCourseTripLabel);
        foreach (var trip in RouteStopEditorCatalog.LoadLineCourseTripRoutes(_editor))
        {
            _tripOptions.Add(trip);
        }
    }

    private void EnsureTripInOptions(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) ||
            _tripOptions.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _tripOptions.Add(label);
    }

    private void LoadDatedRowsFromStop()
    {
        _datedRows.Clear();
        for (var i = 0; i < _endStop.RouteChangeTargetsByDate.Count; i++)
        {
            var entry = _endStop.RouteChangeTargetsByDate[i];
            var label = RouteStopEditorCatalog.ToComboLabel(
                entry.SelectedLineCourseTrip,
                RouteStopEditorCatalog.NoLineCourseTripLabel);
            EnsureTripInOptions(label);
            _datedRows.Add(new RouteChangeDatedTargetRow(
                i,
                RouteOperatingDatesEditor.FormatDisplay(entry.OperatingDates),
                label,
                entry.SelectedLineCourseTrip,
                () => { }));
        }
    }

    private void AddDatedRow()
    {
        _datedRows.Add(new RouteChangeDatedTargetRow(
            _datedRows.Count,
            string.Empty,
            RouteStopEditorCatalog.NoLineCourseTripLabel,
            string.Empty,
            () => { }));
    }

    private void OnDatedRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildDatedHost();

    private void RebuildDatedHost()
    {
        _datedHost.Children.Clear();
        foreach (var row in _datedRows)
        {
            _datedHost.Children.Add(BuildDatedCard(row));
        }
    }

    private Border BuildDatedCard(RouteChangeDatedTargetRow row)
    {
        var card = new Border
        {
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Background = CardBackground
        };

        var stack = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = row.SummaryText,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Opacity = 0.95,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(RouteChangeDatedTargetRow.SummaryText) or null)
            {
                summary.Text = row.SummaryText;
            }
        };
        Grid.SetColumn(summary, 0);
        header.Children.Add(summary);

        var remove = MakeAccentButton("\u2212");
        remove.Width = 36;
        remove.MinHeight = 32;
        remove.Padding = new Thickness(0);
        remove.ToolTip = "Diese Abweichung entfernen";
        remove.Click += (_, _) => _datedRows.Remove(row);
        Grid.SetColumn(remove, 1);
        header.Children.Add(remove);
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = "Datum (kommagetrennt):",
            Foreground = Brushes.White,
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var datesBox = MakeInputBox();
        datesBox.Text = row.DatesText;
        datesBox.Margin = new Thickness(0, 0, 0, 10);
        datesBox.ToolTip = "z. B. 20.08 oder 10.08-14.08, 17.08-19.08";
        datesBox.LostFocus += (_, _) => row.DatesText = datesBox.Text;
        stack.Children.Add(datesBox);

        stack.Children.Add(new TextBlock
        {
            Text = "Wechsel nach Folgefahrt:",
            Foreground = Brushes.White,
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var tripGrid = new Grid();
        tripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tripCombo = new ComboBox
        {
            ItemsSource = _tripOptions,
            SelectedItem = row.SelectedTrip,
            MinWidth = 220,
            MinHeight = 32
        };
        var tripText = new TextBlock
        {
            Text = row.SelectedTrip ?? string.Empty,
            Foreground = MutedForeground,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        tripCombo.SelectionChanged += (_, _) =>
        {
            row.SelectedTrip = tripCombo.SelectedItem as string;
            tripText.Text = row.SelectedTrip ?? string.Empty;
        };
        Grid.SetColumn(tripCombo, 0);
        Grid.SetColumn(tripText, 1);
        tripGrid.Children.Add(tripCombo);
        tripGrid.Children.Add(tripText);
        stack.Children.Add(tripGrid);

        card.Child = stack;
        return card;
    }

    private void UpdateFieldsVisibility()
    {
        _fieldsPanel.Visibility = _enabledBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyQuickTripNumber()
    {
        ClearStatus();
        if (!RouteStopEditorCatalog.TryResolveLineCourseTripByTripNumber(
                _tripOptions,
                _quickEntryBox.Text,
                _routeKey,
                out var matched,
                out var error) ||
            matched is null)
        {
            ShowStatus(error ?? "Fahrt nicht gefunden.");
            return;
        }

        EnsureTripInOptions(matched);
        _standardTripCombo.SelectedItem = matched;
        _standardTripLabel.Text = matched;
        ClearStatus();
    }

    private void ApplyToEndStop()
    {
        _endStop.IsEndStop = true;
        var enabled = _enabledBox.IsChecked == true;
        _endStop.RouteChangeEnabled = enabled;

        if (!enabled)
        {
            return;
        }

        _endStop.SelectedLineCourseTrip = RouteStopEditorCatalog.FromComboLabel(
            _standardTripCombo.SelectedItem as string,
            RouteStopEditorCatalog.NoLineCourseTripLabel);

        var rebuilt = new List<RouteChangeTargetEntry>();
        foreach (var row in _datedRows)
        {
            if (!RouteOperatingDatesEditor.TryParseDateList(row.DatesText, out var dates, out _) ||
                dates.Count == 0)
            {
                continue;
            }

            var trip = RouteStopEditorCatalog.FromComboLabel(
                row.SelectedTrip,
                RouteStopEditorCatalog.NoLineCourseTripLabel);
            if (string.IsNullOrWhiteSpace(trip))
            {
                continue;
            }

            rebuilt.Add(new RouteChangeTargetEntry
            {
                SelectedLineCourseTrip = trip,
                OperatingDates = dates
            });
        }

        _endStop.RouteChangeTargetsByDate = rebuilt;
    }

    private static TextBox MakeInputBox() =>
        new()
        {
            Style = null,
            MinHeight = 34,
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.White,
            Foreground = InputFg
        };

    private static Button MakeAccentButton(object content) =>
        new()
        {
            Content = content,
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 32,
            Background = ButtonBg,
            Foreground = Brushes.White,
            BorderBrush = AccentBrush,
            Cursor = Cursors.Hand
        };

    private void ShowStatus(string message)
    {
        _statusText.Text = message;
        _statusText.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        _statusText.Text = string.Empty;
        _statusText.Visibility = Visibility.Collapsed;
    }
}
