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
    private readonly List<(DataGridColumn Column, Action Toggle)> _toggleColumns = [];
    private DataGrid? _grid;

    public IReadOnlyList<BulkAnnouncementFromAudioRow> ConfirmedRows => _rows.ToList();

    public BulkAnnouncementFromAudioDialog(IEnumerable<BulkAnnouncementFromAudioRow> rows)
    {
        _rows = new ObservableCollection<BulkAnnouncementFromAudioRow>(rows);
        Title = "Mehrere Ansagen anlegen";
        Width = 980;
        Height = 520;
        MinWidth = 760;
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
            Text = "IDs fortlaufend, Bezeichnung und Gong/Next-Stop/Folgende Halte pro Zeile. Spaltenköpfe Gong … Folgende Halte: Klick schaltet alle Zeilen ein/aus. Bereits vorhandene Ansagen erscheinen rot – ✕ entfernt sie aus der Liste – danach „Alle hinzufügen“.",
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
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Sounddatei",
            Binding = new Binding(nameof(BulkAnnouncementFromAudioRow.SoundFileName)),
            IsReadOnly = true,
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
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
        grid.Columns.Add(CreateRemoveDuplicateColumn());

        return grid;
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
        factory.SetValue(Button.ToolTipProperty, "Duplikat aus Liste entfernen");
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
        if (sender is not BulkAnnouncementFromAudioRow row ||
            e.PropertyName is not (nameof(BulkAnnouncementFromAudioRow.AlreadyExists)
                or nameof(BulkAnnouncementFromAudioRow.ExistingMatchLabel)))
        {
            return;
        }

        if (_grid?.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
        {
            ApplyDuplicateRowPresentation(gridRow, row);
        }
    }

    private static void ApplyDuplicateRowPresentation(DataGridRow gridRow, BulkAnnouncementFromAudioRow row)
    {
        gridRow.ToolTip = row.AlreadyExists
            ? string.IsNullOrWhiteSpace(row.ExistingMatchLabel)
                ? "Ansage existiert bereits"
                : $"Bereits vorhanden: {row.ExistingMatchLabel}"
            : null;
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
