using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Models;

namespace SmartOepnv.AppShared.Views;

public sealed class BulkAnnouncementFromAudioDialog : Window
{
    private readonly ObservableCollection<BulkAnnouncementFromAudioRow> _rows;
    private readonly Func<string, IReadOnlyList<string>>? _suggestStopNames;
    private readonly List<(DataGridColumn Column, Action Toggle)> _toggleColumns = [];
    private DataGrid? _grid;
    private Popup? _suggestionPopup;
    private ListBox? _suggestionList;
    private BulkAnnouncementFromAudioRow? _suggestionTargetRow;

    public IReadOnlyList<BulkAnnouncementFromAudioRow> ConfirmedRows => _rows.ToList();

    public BulkAnnouncementFromAudioDialog(
        IEnumerable<BulkAnnouncementFromAudioRow> rows,
        Func<string, IReadOnlyList<string>>? suggestStopNames = null)
    {
        _rows = new ObservableCollection<BulkAnnouncementFromAudioRow>(rows);
        _suggestStopNames = suggestStopNames;
        Title = "Mehrere Ansagen anlegen";
        Width = 1080;
        Height = 540;
        MinWidth = 800;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var addAll = new Button
        {
            Content = "Alle hinzufügen",
            MinWidth = 140,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        addAll.Click += (_, _) =>
        {
            CloseSuggestionPopup();
            var missing = _rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.DisplayName));
            if (missing is not null)
            {
                MessageBox.Show(
                    this,
                    $"Bitte Bezeichnung für ID {missing.AnnouncementCodeDisplay} ergänzen.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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
            Padding = new Thickness(12, 4, 12, 4)
        };
        cancel.Click += (_, _) =>
        {
            CloseSuggestionPopup();
            DialogResult = false;
            Close();
        };

        buttons.Children.Add(addAll);
        buttons.Children.Add(cancel);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock
        {
            Text = "Ansagen aus mehreren Tondateien",
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4)
        });
        header.Children.Add(new TextBlock
        {
            Text =
                "IDs fortlaufend. Bezeichnung/Speichername = WAV-Name ohne .wav. " +
                "Bereits vorhandene Ansagen rot – „aktualisieren“ legt sie auf die identische ID. " +
                "Unbekannte Haltestellen weiß: Klick auf Sounddatei → ähnliche Namen aus der Haltestellenliste. " +
                "✕ entfernt die Zeile – dann „Alle hinzufügen“.",
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        _grid = CreateGrid();
        foreach (var row in _rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        root.Children.Add(buttons);
        root.Children.Add(header);
        root.Children.Add(_grid);
        Content = root;

        Closed += (_, _) => CloseSuggestionPopup();
        Deactivated += (_, _) => CloseSuggestionPopup();
    }

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            EnableRowVirtualization = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            RowBackground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x15, 0x23, 0x42)),
            Foreground = Brushes.White,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            RowStyle = CreateDuplicateRowStyle()
        };

        grid.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is BulkAnnouncementFromAudioRow row)
            {
                ApplyDuplicateRowPresentation(e.Row, row);
            }
        };

        grid.PreviewMouseLeftButtonDown += OnToggleColumnHeaderClick;

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "ID",
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.AnnouncementCodeDisplay)),
            IsReadOnly = true,
            Width = 72
        });
        grid.Columns.Add(CreateSoundFileColumn());
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Bezeichnung",
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.DisplayName))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Speichername",
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.SaveFileName)),
            IsReadOnly = true,
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(MakeToggleCheckColumn("Gong", nameof(BulkAnnouncementFromAudioRow.IncludeGong), row => row.IncludeGong, (row, v) => row.IncludeGong = v));
        grid.Columns.Add(MakeToggleCheckColumn("Sondergong", nameof(BulkAnnouncementFromAudioRow.IncludeSondergong), row => row.IncludeSondergong, (row, v) => row.IncludeSondergong = v));
        grid.Columns.Add(MakeToggleCheckColumn("Nächste Hst.", nameof(BulkAnnouncementFromAudioRow.IncludeNextStopGerman), row => row.IncludeNextStopGerman, (row, v) => row.IncludeNextStopGerman = v));
        grid.Columns.Add(MakeToggleCheckColumn("Next Stop", nameof(BulkAnnouncementFromAudioRow.IncludeNextStopMp3), row => row.IncludeNextStopMp3, (row, v) => row.IncludeNextStopMp3 = v));
        grid.Columns.Add(MakeToggleCheckColumn("Folgende Halte", nameof(BulkAnnouncementFromAudioRow.IncludeFollowingStops), row => row.IncludeFollowingStops, (row, v) => row.IncludeFollowingStops = v));
        grid.Columns.Add(CreateUpdateExistingColumn());
        grid.Columns.Add(CreateRemoveDuplicateColumn());

        return grid;
    }

    private DataGridTemplateColumn CreateSoundFileColumn()
    {
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(BulkAnnouncementFromAudioRow.SoundFileName)));
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
        textFactory.SetValue(FrameworkElement.ToolTipProperty, "Unbekannte Haltestelle: Klick für Namensvorschläge aus der Haltestellenliste");
        textFactory.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
        textFactory.AddHandler(
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnSoundFileClick));

        // Nur bei unbekannten (weißen) Zeilen Hand-Cursor / Unterstreichung andeuten
        var underlineTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.IsUnknownStop)),
            Value = true
        };
        underlineTrigger.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline));
        underlineTrigger.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
        textStyle.Triggers.Add(underlineTrigger);
        textFactory.SetValue(FrameworkElement.StyleProperty, textStyle);

        return new DataGridTemplateColumn
        {
            Header = "Sounddatei",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            CanUserSort = false,
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = textFactory }
        };
    }

    private void OnSoundFileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BulkAnnouncementFromAudioRow row })
        {
            return;
        }

        if (!row.IsUnknownStop || _suggestStopNames is null)
        {
            return;
        }

        e.Handled = true;
        ShowStopNameSuggestions(row, sender as UIElement);
    }

    private void ShowStopNameSuggestions(BulkAnnouncementFromAudioRow row, UIElement? placementTarget)
    {
        CloseSuggestionPopup();

        var query = !string.IsNullOrWhiteSpace(row.DisplayName)
            ? row.DisplayName
            : System.IO.Path.GetFileNameWithoutExtension(row.SoundFileName);
        var suggestions = _suggestStopNames!(query);
        if (suggestions.Count == 0)
        {
            MessageBox.Show(
                this,
                $"Keine ähnlichen Haltestellen zu „{query}“ gefunden.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _suggestionTargetRow = row;
        _suggestionList = new ListBox
        {
            ItemsSource = suggestions,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            BorderThickness = new Thickness(1),
            MaxHeight = 260,
            MinWidth = 220,
            Padding = new Thickness(2)
        };
        _suggestionList.MouseDoubleClick += OnSuggestionChosen;
        _suggestionList.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                ApplySelectedSuggestion();
                ke.Handled = true;
            }
            else if (ke.Key == Key.Escape)
            {
                CloseSuggestionPopup();
                ke.Handled = true;
            }
        };

        var title = new TextBlock
        {
            Text = $"Ähnliche Haltestellen zu „{query}“",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        };
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
            Cursor = Cursors.Hand,
            ToolTip = "Vorschläge schließen",
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => CloseSuggestionPopup();

        var titleRow = new DockPanel { Margin = new Thickness(8, 8, 8, 4) };
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleRow.Children.Add(closeBtn);
        titleRow.Children.Add(title);

        var hint = new TextBlock
        {
            Text = "Doppelklick/Enter übernimmt · ✕, Esc oder Klick außerhalb schließt.",
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(8, 0, 8, 6),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        };

        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Height = 30,
            Margin = new Thickness(8, 6, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 100,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 2, 12, 2),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => CloseSuggestionPopup();

        var panel = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Width = 340
        };
        DockPanel.SetDock(titleRow, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(cancelBtn, Dock.Bottom);
        panel.Children.Add(titleRow);
        panel.Children.Add(hint);
        panel.Children.Add(cancelBtn);
        panel.Children.Add(_suggestionList);

        // StaysOpen=true: in modalen Dialogen schließt StaysOpen=false oft nicht zuverlässig.
        // Schließen per ✕ / Abbrechen / Esc / Klick außerhalb (PreviewMouseDown).
        _suggestionPopup = new Popup
        {
            Child = panel,
            Placement = PlacementMode.Bottom,
            PlacementTarget = placementTarget ?? _grid,
            StaysOpen = true,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            IsOpen = true
        };
        _suggestionPopup.Closed += (_, _) => DetachSuggestionDismissHandlers();

        PreviewMouseDown -= OnSuggestionOutsideMouseDown;
        PreviewKeyDown -= OnSuggestionEscapeKeyDown;
        PreviewMouseDown += OnSuggestionOutsideMouseDown;
        PreviewKeyDown += OnSuggestionEscapeKeyDown;

        _suggestionList.SelectedIndex = 0;
        _suggestionList.Focus();
    }

    private void OnSuggestionEscapeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _suggestionPopup is not { IsOpen: true })
        {
            return;
        }

        CloseSuggestionPopup();
        e.Handled = true;
    }

    private void OnSuggestionOutsideMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_suggestionPopup is not { IsOpen: true })
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            (IsUnderVisual(_suggestionPopup.Child, source) || IsUnderVisual(_suggestionPopup, source)))
        {
            return;
        }

        CloseSuggestionPopup();
    }

    private static bool IsUnderVisual(DependencyObject? root, DependencyObject? node)
    {
        if (root is null || node is null)
        {
            return false;
        }

        var current = node;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void DetachSuggestionDismissHandlers()
    {
        PreviewMouseDown -= OnSuggestionOutsideMouseDown;
        PreviewKeyDown -= OnSuggestionEscapeKeyDown;
        _suggestionPopup = null;
        _suggestionList = null;
        _suggestionTargetRow = null;
    }

    private void OnSuggestionChosen(object sender, MouseButtonEventArgs e) => ApplySelectedSuggestion();

    private void ApplySelectedSuggestion()
    {
        if (_suggestionTargetRow is null ||
            _suggestionList?.SelectedItem is not string chosen ||
            string.IsNullOrWhiteSpace(chosen))
        {
            CloseSuggestionPopup();
            return;
        }

        _suggestionTargetRow.DisplayName = chosen.Trim();
        CloseSuggestionPopup();
    }

    private void CloseSuggestionPopup()
    {
        if (_suggestionPopup is null)
        {
            DetachSuggestionDismissHandlers();
            return;
        }

        var popup = _suggestionPopup;
        DetachSuggestionDismissHandlers();
        popup.IsOpen = false;
    }

    private DataGridTemplateColumn CreateUpdateExistingColumn()
    {
        var checkFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkFactory.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(BulkAnnouncementFromAudioRow.UpdateExisting))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Mode = BindingMode.TwoWay
            });
        checkFactory.SetBinding(
            UIElement.IsEnabledProperty,
            new Binding(nameof(BulkAnnouncementFromAudioRow.CanUpdateExisting)));
        checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkFactory.SetValue(
            FrameworkElement.ToolTipProperty,
            "Aktiv: vorhandene Ansage unter der identischen ID aktualisieren (Ton/Bezeichnung)");

        var visibilityStyle = new Style(typeof(CheckBox));
        visibilityStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.CanUpdateExisting)),
            Value = true
        };
        showTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        visibilityStyle.Triggers.Add(showTrigger);
        checkFactory.SetValue(FrameworkElement.StyleProperty, visibilityStyle);

        var column = new DataGridTemplateColumn
        {
            Header = new TextBlock
            {
                Text = "aktualisieren",
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline,
                ToolTip = "Klick: alle „aktualisieren“-Felder ein- oder ausschalten"
            },
            Width = 88,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = checkFactory }
        };
        _toggleColumns.Add((column, ToggleUpdateExistingColumn));
        return column;
    }

    private void ToggleUpdateExistingColumn()
    {
        var eligible = _rows.Where(r => r.CanUpdateExisting).ToList();
        if (eligible.Count == 0)
        {
            return;
        }

        var allOn = eligible.All(r => r.UpdateExisting);
        var next = !allOn;
        foreach (var row in eligible)
        {
            row.UpdateExisting = next;
        }
    }

    private DataGridTemplateColumn CreateRemoveDuplicateColumn()
    {
        var visibilityStyle = new Style(typeof(Button));
        visibilityStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.AlreadyExists)),
            Value = true
        };
        showTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        visibilityStyle.Triggers.Add(showTrigger);

        var factory = new FrameworkElementFactory(typeof(Button));
        factory.SetValue(Button.ContentProperty, "✕");
        factory.SetValue(Button.StyleProperty, visibilityStyle);
        factory.SetValue(Button.WidthProperty, 28.0);
        factory.SetValue(Button.HeightProperty, 28.0);
        factory.SetValue(Button.PaddingProperty, new Thickness(0));
        factory.SetValue(Button.BackgroundProperty, Brushes.Transparent);
        factory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
        factory.SetValue(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)));
        factory.SetValue(Button.CursorProperty, Cursors.Hand);
        factory.SetValue(Button.ToolTipProperty, "Zeile aus Liste entfernen");
        factory.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnRemoveDuplicateRowClick));

        return new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = 40,
            CanUserSort = false,
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = factory }
        };
    }

    private void OnRemoveDuplicateRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BulkAnnouncementFromAudioRow row } || !row.AlreadyExists)
        {
            return;
        }

        e.Handled = true;
        RemoveRow(row);
    }

    private void RemoveRow(BulkAnnouncementFromAudioRow row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        _rows.Remove(row);
        CloseSuggestionPopup();
    }

    private DataGridCheckBoxColumn MakeToggleCheckColumn(
        string headerText,
        string propertyName,
        Func<BulkAnnouncementFromAudioRow, bool> readFlag,
        Action<BulkAnnouncementFromAudioRow, bool> writeFlag)
    {
        var column = new DataGridCheckBoxColumn
        {
            Header = new TextBlock
            {
                Text = headerText,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                ToolTip = $"{headerText}: Klick schaltet die ganze Spalte ein oder aus",
                TextDecorations = TextDecorations.Underline
            },
            Binding = new Binding(propertyName) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 72,
            CanUserSort = false
        };
        _toggleColumns.Add((column, () => ToggleColumn(readFlag, writeFlag)));
        return column;
    }

    private void OnToggleColumnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject)?.Column is not { } column)
        {
            return;
        }

        foreach (var (toggleColumn, toggle) in _toggleColumns)
        {
            if (!ReferenceEquals(toggleColumn, column))
            {
                continue;
            }

            toggle();
            e.Handled = true;
            return;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void ToggleColumn(
        Func<BulkAnnouncementFromAudioRow, bool> readFlag,
        Action<BulkAnnouncementFromAudioRow, bool> writeFlag)
    {
        var allOn = _rows.Count > 0 && _rows.All(readFlag);
        var newValue = !allOn;
        foreach (var row in _rows)
        {
            writeFlag(row, newValue);
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BulkAnnouncementFromAudioRow row)
        {
            return;
        }

        if (e.PropertyName is nameof(BulkAnnouncementFromAudioRow.AlreadyExists)
            or nameof(BulkAnnouncementFromAudioRow.ExistingMatchLabel)
            or nameof(BulkAnnouncementFromAudioRow.UpdateExisting)
            or nameof(BulkAnnouncementFromAudioRow.AnnouncementCodeDisplay)
            or nameof(BulkAnnouncementFromAudioRow.CanUpdateExisting))
        {
            if (_grid?.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
            {
                ApplyDuplicateRowPresentation(gridRow, row);
            }
        }
    }

    private static void ApplyDuplicateRowPresentation(DataGridRow gridRow, BulkAnnouncementFromAudioRow row)
    {
        if (row.AlreadyExists)
        {
            gridRow.ToolTip = string.IsNullOrWhiteSpace(row.ExistingMatchLabel)
                ? "Ansage existiert bereits"
                : row.UpdateExisting
                    ? $"Aktualisieren auf: {row.ExistingMatchLabel}"
                    : $"Bereits vorhanden: {row.ExistingMatchLabel}";
            return;
        }

        gridRow.ToolTip = "Unbekannte Haltestelle – Sounddatei anklicken für Namensvorschläge";
    }

    private static Style CreateDuplicateRowStyle()
    {
        var duplicateBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        duplicateBrush.Freeze();

        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));

        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.AlreadyExists)),
            Value = true
        };
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, duplicateBrush));
        style.Triggers.Add(trigger);

        return style;
    }
}
