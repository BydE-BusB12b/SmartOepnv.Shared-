using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Fahrbereich aus dem aktuellen Fahrverlauf markieren, optional Haltestellen in Lücken einfügen
/// und Zeiten anpassen – Ergebnis geht in „Route hinzufügen“.
/// </summary>
public sealed class FahrbereichCopyDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush AccentBlue = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Brush SelectedGreen = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush SelectedGreenBorder = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
    private static readonly Brush UnselectedRowBackground = new SolidColorBrush(Color.FromRgb(0x12, 0x2A, 0x48));
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush SuggestionForeground = new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));

    private readonly ObservableCollection<FahrbereichStopRow> _rows = [];
    private readonly List<TextBox> _timeBoxes = [];
    private readonly IReadOnlyList<ManagedStopTemplateItem> _templates;
    private readonly IReadOnlyDictionary<(string FromCode, string ToCode), int> _travelMinutes;
    private readonly StackPanel _listPanel;
    private readonly TextBlock _hint;
    private readonly Button _applyButton;
    private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private FahrbereichStopRow? _pendingClickRow;
    private bool _syncingTimeBox;

    public IReadOnlyList<RouteStopItem> ResultStops { get; private set; } = [];

    public FahrbereichCopyDialog(
        IReadOnlyList<RouteStopItem> sourceStops,
        IReadOnlyList<ManagedStopTemplateItem> templates,
        string routeDisplayName,
        IReadOnlyDictionary<(string FromCode, string ToCode), int>? travelMinutesByStopPair = null)
    {
        _travelMinutes = travelMinutesByStopPair ??
                         new Dictionary<(string FromCode, string ToCode), int>();
        _templates = templates
            .Where(t => t.HasPersistableContent())
            .OrderBy(t => t.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "Fahrbereich kopieren";
        Width = 700;
        Height = 620;
        MinWidth = 540;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = DialogBackground;

        foreach (var stop in sourceStops)
        {
            _rows.Add(FahrbereichStopRow.FromSource(stop));
        }

        var root = new DockPanel { Margin = new Thickness(20) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Fahrbereich kopieren",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 6)
        });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(routeDisplayName)
                ? "Haltestellen anklicken (blau → grün). Bei genau zwei Markierungen: Doppelklick öffnet die Kartei zum Einfügen dazwischen. Mit ↑/↓ Reihenfolge ändern (z. B. neu eingefügte Hst. nach oben).\nZeiten: Enter übernimmt den (grauen) Vorschlag bzw. die Eingabe und springt weiter – Zifferntipp verwirft den Vorschlag."
                : $"Route: {routeDisplayName}\nHaltestellen anklicken (blau → grün). Bei genau zwei Markierungen: Doppelklick öffnet die Kartei zum Einfügen dazwischen. Mit ↑/↓ Reihenfolge ändern (z. B. neu eingefügte Hst. nach oben).\nZeiten: Enter übernimmt den (grauen) Vorschlag bzw. die Eingabe und springt weiter – Zifferntipp verwirft den Vorschlag.",
            Foreground = Brushes.White,
            Opacity = 0.85,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            MinWidth = 110,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        _applyButton = new Button
        {
            Content = "Übernehmen",
            MinWidth = 120,
            MinHeight = 36,
            IsDefault = true,
            Background = AccentBlue,
            Foreground = Brushes.White,
            BorderBrush = AccentBlue,
            FontWeight = FontWeights.SemiBold
        };
        _applyButton.Click += (_, _) => Confirm();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_applyButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _hint = new TextBlock
        {
            Foreground = Brushes.White,
            Opacity = 0.75,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(_hint, Dock.Bottom);
        root.Children.Add(_hint);

        var columnHeader = CreateColumnHeader();
        DockPanel.SetDock(columnHeader, Dock.Top);
        root.Children.Add(columnHeader);

        _listPanel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _listPanel
        };
        root.Children.Add(scroll);

        Content = root;

        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            if (_pendingClickRow is null)
            {
                return;
            }

            var row = _pendingClickRow;
            _pendingClickRow = null;
            ToggleSelection(row);
        };

        RebuildList();
        UpdateHint();
    }

    private static FrameworkElement CreateColumnHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        grid.Children.Add(CreateHeaderLabel("#", 0));
        grid.Children.Add(CreateHeaderLabel("Haltestelle", 1));
        grid.Children.Add(CreateHeaderLabel("Zeit", 2));
        grid.Children.Add(CreateHeaderLabel("", 3));
        return grid;
    }

    private static TextBlock CreateHeaderLabel(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            Opacity = 0.7,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(column == 0 ? 10 : 8, 0, 4, 0)
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private void RebuildList()
    {
        _listPanel.Children.Clear();
        _timeBoxes.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.IndexLabel = (i + 1).ToString(CultureInfo.InvariantCulture);
            _listPanel.Children.Add(CreateRowBorder(row));
        }
    }

    private Border CreateRowBorder(FahrbereichStopRow row)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = row,
            Background = row.IsSelected ? SelectedGreen : UnselectedRowBackground,
            BorderBrush = row.IsSelected ? SelectedGreenBorder : AccentBlue
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        var indexBlock = new TextBlock
        {
            Text = row.IndexLabel,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85
        };
        Grid.SetColumn(indexBlock, 0);
        grid.Children.Add(indexBlock);

        var nameBlock = new TextBlock
        {
            Text = row.DisplayName,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 8, 0)
        };
        Grid.SetColumn(nameBlock, 1);
        grid.Children.Add(nameBlock);

        var timeBox = CreateTimeBox(row);
        _timeBoxes.Add(timeBox);
        Grid.SetColumn(timeBox, 2);
        grid.Children.Add(timeBox);

        var movePanel = CreateMoveButtons(row);
        Grid.SetColumn(movePanel, 3);
        grid.Children.Add(movePanel);

        border.Child = grid;
        border.MouseLeftButtonDown += OnRowMouseLeftButtonDown;
        return border;
    }

    private FrameworkElement CreateMoveButtons(FahrbereichStopRow row)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };

        var index = _rows.IndexOf(row);
        var up = CreateMoveButton("↑", "Nach oben", enabled: index > 0);
        up.Click += (_, e) =>
        {
            MoveRow(row, delta: -1);
            e.Handled = true;
        };
        var down = CreateMoveButton("↓", "Nach unten", enabled: index >= 0 && index < _rows.Count - 1);
        down.Click += (_, e) =>
        {
            MoveRow(row, delta: 1);
            e.Handled = true;
        };
        panel.Children.Add(up);
        panel.Children.Add(down);
        return panel;
    }

    private static Button CreateMoveButton(string content, string toolTip, bool enabled) =>
        new()
        {
            Content = content,
            ToolTip = toolTip,
            Width = 30,
            Height = 28,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5C)),
            BorderBrush = AccentBlue,
            IsEnabled = enabled,
            Cursor = Cursors.Hand
        };

    private void MoveRow(FahrbereichStopRow row, int delta)
    {
        var index = _rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _rows.Count)
        {
            return;
        }

        _rows.Move(index, target);
        RebuildList();
        UpdateHint();
    }

    private TextBox CreateTimeBox(FahrbereichStopRow row)
    {
        var timeBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            MinHeight = 28,
            MaxLength = 4,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = row
        };

        if (RouteScheduleTimeCalculator.TryParseTime(row.TimeText, out var parsed))
        {
            timeBox.Text = parsed.ToString("HHmm", CultureInfo.InvariantCulture);
        }

        timeBox.PreviewTextInput += TimeBox_PreviewTextInput;
        DataObject.AddPastingHandler(timeBox, TimeBox_OnPaste);
        timeBox.PreviewKeyDown += TimeBox_PreviewKeyDown;
        timeBox.TextChanged += (_, _) =>
        {
            if (_syncingTimeBox || row.ShowingSuggestion)
            {
                return;
            }

            row.TimeText = DigitsOnly(timeBox.Text);
        };
        timeBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!timeBox.IsKeyboardFocusWithin)
            {
                timeBox.Focus();
                timeBox.SelectAll();
                e.Handled = true;
            }
        };
        timeBox.GotFocus += (_, _) => OnTimeBoxGotFocus(timeBox, row);
        timeBox.LostFocus += (_, _) => OnTimeBoxLostFocus(timeBox, row);
        return timeBox;
    }

    private void OnTimeBoxGotFocus(TextBox timeBox, FahrbereichStopRow row)
    {
        if (row.ShowingSuggestion && !string.IsNullOrEmpty(row.SuggestedTime))
        {
            ShowSuggestionInBox(timeBox, row, row.SuggestedTime);
            return;
        }

        timeBox.Foreground = InputForeground;
        if (RouteScheduleTimeCalculator.TryParseTime(row.TimeText, out var t))
        {
            SetTimeBoxText(timeBox, t.ToString("HHmm", CultureInfo.InvariantCulture), maxLength: 4);
            timeBox.SelectAll();
        }
        else
        {
            SetTimeBoxText(timeBox, DigitsOnly(timeBox.Text), maxLength: 4);
        }
    }

    private void OnTimeBoxLostFocus(TextBox timeBox, FahrbereichStopRow row)
    {
        // Grauer Vorschlag bleibt Vorschlag – nur Enter übernimmt.
        if (row.ShowingSuggestion)
        {
            ShowSuggestionInBox(timeBox, row, row.SuggestedTime ?? string.Empty);
            return;
        }

        CommitTypedTime(timeBox, row, showNormalized: true);
    }

    private void TimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not FahrbereichStopRow row)
        {
            return;
        }

        if (e.Text.Any(ch => !char.IsDigit(ch)))
        {
            e.Handled = true;
            return;
        }

        if (row.ShowingSuggestion)
        {
            DismissSuggestion(box, row);
        }
    }

    private void TimeBox_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not FahrbereichStopRow row)
        {
            return;
        }

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var raw = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var digits = DigitsOnly(raw);
        if (digits.Length == 0)
        {
            e.CancelCommand();
            return;
        }

        e.CancelCommand();
        if (row.ShowingSuggestion)
        {
            DismissSuggestion(box, row);
        }

        var start = box.SelectionStart;
        var length = box.SelectionLength;
        var current = DigitsOnly(box.Text);
        var prefix = start <= current.Length ? current[..start] : current;
        var suffixStart = Math.Min(current.Length, start + length);
        var suffix = current[suffixStart..];
        var merged = prefix + digits + suffix;
        if (merged.Length > 4)
        {
            merged = merged[..4];
        }

        SetTimeBoxText(box, merged, maxLength: 4);
        box.CaretIndex = merged.Length;
        row.TimeText = merged;
    }

    private void TimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not FahrbereichStopRow row)
        {
            return;
        }

        if (e.Key is Key.Space or Key.OemPeriod or Key.Decimal or Key.OemSemicolon or Key.Oem2)
        {
            e.Handled = true;
            return;
        }

        // Backspace/Delete bei Vorschlag → Vorschlag verwerfen.
        if (row.ShowingSuggestion && e.Key is Key.Back or Key.Delete)
        {
            DismissSuggestion(box, row);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        HandleTimeEnter(box, row);
        e.Handled = true;
    }

    private void HandleTimeEnter(TextBox timeBox, FahrbereichStopRow row)
    {
        if (row.ShowingSuggestion && !string.IsNullOrEmpty(row.SuggestedTime))
        {
            AcceptSuggestion(timeBox, row);
        }
        else
        {
            CommitTypedTime(timeBox, row, showNormalized: true);
        }

        MoveFocusToNextWithSuggestion(timeBox);
    }

    private void AcceptSuggestion(TextBox timeBox, FahrbereichStopRow row)
    {
        var accepted = row.SuggestedTime ?? string.Empty;
        row.SuggestedTime = null;
        row.ShowingSuggestion = false;
        row.TimeText = accepted;
        timeBox.Foreground = InputForeground;
        SetTimeBoxText(timeBox, accepted, maxLength: 5);
        SelectRowForCommittedTime(row);
    }

    private void CommitTypedTime(TextBox timeBox, FahrbereichStopRow row, bool showNormalized)
    {
        if (row.ShowingSuggestion)
        {
            return;
        }

        var digits = DigitsOnly(timeBox.Text);
        var normalized = string.IsNullOrEmpty(digits)
            ? string.Empty
            : RouteScheduleTimeCalculator.NormalizeTimeInput(digits);
        row.TimeText = normalized;
        row.SuggestedTime = null;
        row.ShowingSuggestion = false;
        timeBox.Foreground = InputForeground;
        if (showNormalized)
        {
            SetTimeBoxText(timeBox, normalized, maxLength: 5);
        }

        if (!string.IsNullOrEmpty(normalized))
        {
            SelectRowForCommittedTime(row);
        }
    }

    /// <summary>Zeit übernommen/eingetragen → Haltestelle automatisch markieren (grün).</summary>
    private void SelectRowForCommittedTime(FahrbereichStopRow row)
    {
        if (row.IsSelected)
        {
            return;
        }

        row.IsSelected = true;
        RefreshRowVisuals();
        UpdateHint();
    }

    private void DismissSuggestion(TextBox timeBox, FahrbereichStopRow row)
    {
        row.ShowingSuggestion = false;
        row.SuggestedTime = null;
        row.TimeText = string.Empty;
        timeBox.Foreground = InputForeground;
        SetTimeBoxText(timeBox, string.Empty, maxLength: 4);
    }

    private void MoveFocusToNextWithSuggestion(TextBox currentBox)
    {
        var index = _timeBoxes.IndexOf(currentBox);
        if (index < 0 || index >= _timeBoxes.Count - 1)
        {
            return;
        }

        var nextIndex = index + 1;
        TryOfferSuggestion(fromIndex: index, targetIndex: nextIndex);
        var next = _timeBoxes[nextIndex];
        next.Focus();
        next.SelectAll();
    }

    private void TryOfferSuggestion(int fromIndex, int targetIndex)
    {
        if (_travelMinutes.Count == 0 ||
            fromIndex < 0 || fromIndex >= _rows.Count ||
            targetIndex < 0 || targetIndex >= _rows.Count)
        {
            return;
        }

        var fromRow = _rows[fromIndex];
        var toRow = _rows[targetIndex];
        if (fromRow.Stop.IsWaypoint || toRow.Stop.IsWaypoint)
        {
            return;
        }

        if (!RouteScheduleTimeCalculator.TryParseTime(ResolveCommittedTime(fromRow), out _))
        {
            return;
        }

        if (!RouteTravelTimeLearner.TrySuggestArrival(
                _travelMinutes,
                fromRow.Stop.PlannerStopCode,
                ResolveCommittedTime(fromRow),
                toRow.Stop.PlannerStopCode,
                out var suggested))
        {
            return;
        }

        // Nur vorschlagen, wenn Ziel noch keine übernommene Zeit hat – außer Ziel ist leer/unverändert aus Quelle:
        // Vorschlag immer anbieten; Übernahme erst mit Enter.
        ShowSuggestionInBox(_timeBoxes[targetIndex], toRow, suggested);
    }

    private void ShowSuggestionInBox(TextBox timeBox, FahrbereichStopRow row, string suggestedHhMm)
    {
        if (string.IsNullOrEmpty(suggestedHhMm))
        {
            return;
        }

        row.SuggestedTime = suggestedHhMm;
        row.ShowingSuggestion = true;
        timeBox.Foreground = SuggestionForeground;
        SetTimeBoxText(timeBox, suggestedHhMm, maxLength: 5);
        timeBox.SelectAll();
    }

    private void SetTimeBoxText(TextBox timeBox, string text, int maxLength)
    {
        _syncingTimeBox = true;
        try
        {
            timeBox.MaxLength = Math.Max(maxLength, text.Length);
            timeBox.Text = text;
            timeBox.MaxLength = maxLength;
        }
        finally
        {
            _syncingTimeBox = false;
        }
    }

    private static string ResolveCommittedTime(FahrbereichStopRow row) =>
        row.ShowingSuggestion
            ? string.Empty
            : row.TimeText;

    private static string ResolveEffectiveTime(FahrbereichStopRow row)
    {
        if (row.ShowingSuggestion && !string.IsNullOrEmpty(row.SuggestedTime))
        {
            return row.SuggestedTime;
        }

        return RouteScheduleTimeCalculator.NormalizeTimeInput(row.TimeText);
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    private void OnRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            (FindAncestor<TextBox>(source) is not null || FindAncestor<Button>(source) is not null))
        {
            return;
        }

        if (sender is not Border { Tag: FahrbereichStopRow row })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _clickTimer.Stop();
            _pendingClickRow = null;
            TryInsertOnDoubleClick(row);
            e.Handled = true;
            return;
        }

        _pendingClickRow = row;
        _clickTimer.Stop();
        _clickTimer.Start();
        e.Handled = true;
    }

    private void ToggleSelection(FahrbereichStopRow row)
    {
        row.IsSelected = !row.IsSelected;
        RefreshRowVisuals();
        UpdateHint();
    }

    private void TryInsertOnDoubleClick(FahrbereichStopRow row)
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count != 2 || !row.IsSelected)
        {
            return;
        }

        TryInsertStopBetween(selected[0], selected[1]);
    }

    private void TryInsertStopBetween(FahrbereichStopRow a, FahrbereichStopRow b)
    {
        if (_templates.Count == 0)
        {
            MessageBox.Show(
                this,
                "Keine Haltestellen in der Kartei – bitte unter „Haltestellen“ anlegen.",
                "Fahrbereich kopieren",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var indexA = _rows.IndexOf(a);
        var indexB = _rows.IndexOf(b);
        if (indexA < 0 || indexB < 0)
        {
            return;
        }

        var insertAt = Math.Min(indexA, indexB) + 1;

        var picker = new PickStopFromLibraryDialog(_templates) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedTemplate is null)
        {
            return;
        }

        var stop = picker.SelectedTemplate.ToRouteStop(string.Empty);
        var row = FahrbereichStopRow.FromInserted(stop);
        row.IsSelected = true;
        _rows.Insert(insertAt, row);
        RebuildList();
        UpdateHint();
    }

    private void RefreshRowVisuals()
    {
        foreach (var child in _listPanel.Children)
        {
            if (child is not Border { Tag: FahrbereichStopRow row } border)
            {
                continue;
            }

            border.Background = row.IsSelected ? SelectedGreen : UnselectedRowBackground;
            border.BorderBrush = row.IsSelected ? SelectedGreenBorder : AccentBlue;
            if (border.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock indexBlock)
            {
                var index = _rows.IndexOf(row);
                indexBlock.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
                row.IndexLabel = indexBlock.Text;
            }
        }
    }

    private void Confirm()
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                this,
                "Bitte mindestens eine Haltestelle markieren.",
                "Fahrbereich kopieren",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = new List<RouteStopItem>(selected.Count);
        foreach (var row in selected)
        {
            var time = ResolveEffectiveTime(row);
            row.TimeText = time;
            row.SuggestedTime = null;
            row.ShowingSuggestion = false;
            var stop = row.Stop.Clone();
            stop.Time = time;
            result.Add(stop);
        }

        ResultStops = result;
        DialogResult = true;
        Close();
    }

    private void UpdateHint()
    {
        var selectedCount = _rows.Count(r => r.IsSelected);
        _hint.Text = selectedCount switch
        {
            0 => "Noch keine Haltestelle markiert.",
            1 => "1 Haltestelle markiert. Eine zweite markieren, um dazwischen aus der Kartei einzufügen.",
            2 => "2 Haltestellen markiert – Doppelklick auf eine davon öffnet die Kartei (Einfügen dazwischen). Weitere Markierungen möglich.",
            _ => $"{selectedCount} Haltestellen markiert. Übernehmen öffnet „Route hinzufügen“."
        };
        _applyButton.IsEnabled = selectedCount > 0;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class FahrbereichStopRow
    {
        public RouteStopItem Stop { get; private init; } = new();
        public string DisplayName { get; private init; } = string.Empty;
        public string IndexLabel { get; set; } = string.Empty;
        /// <summary>Übernommene/eingegebene Zeit (noch nicht der graue Vorschlag).</summary>
        public string TimeText { get; set; } = string.Empty;
        /// <summary>Gelernter Vorschlag HH:mm – erst mit Enter übernommen.</summary>
        public string? SuggestedTime { get; set; }
        public bool ShowingSuggestion { get; set; }
        public bool IsSelected { get; set; }

        public static FahrbereichStopRow FromSource(RouteStopItem source)
        {
            var clone = source.Clone();
            return new FahrbereichStopRow
            {
                Stop = clone,
                DisplayName = BuildDisplayName(clone),
                TimeText = clone.Time ?? string.Empty
            };
        }

        public static FahrbereichStopRow FromInserted(RouteStopItem stop) => new()
        {
            Stop = stop,
            DisplayName = BuildDisplayName(stop),
            TimeText = stop.Time ?? string.Empty
        };

        private static string BuildDisplayName(RouteStopItem stop)
        {
            if (stop.IsWaypoint)
            {
                var wp = string.IsNullOrWhiteSpace(stop.WaypointName) ? "Waypoint" : stop.WaypointName.Trim();
                return $"◇ {wp}";
            }

            var name = string.IsNullOrWhiteSpace(stop.Name) ? "(ohne Name)" : stop.Name.Trim();
            if (!string.IsNullOrWhiteSpace(stop.StopDisplay) &&
                !string.Equals(stop.StopDisplay.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return $"{name}  ·  {stop.StopDisplay.Trim()}";
            }

            return name;
        }
    }
}
