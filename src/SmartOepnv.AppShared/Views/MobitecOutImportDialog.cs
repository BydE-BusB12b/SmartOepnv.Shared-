using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Auswahl-Dialog nach Mobitec-OUT-Parse: Nr., Linie/Front/Seite, editierbarer Speichername, Checkbox.
/// </summary>
public sealed class MobitecOutImportDialog : Window
{
    private readonly ObservableCollection<Row> _rows;
    private readonly TextBlock _selectionSummary;

    public IReadOnlyList<(MobitecOutImportDestination Destination, string SaveName)> SelectedImports =>
        _rows.Where(r => r.IsSelected)
            .Select(r => (r.Destination, string.IsNullOrWhiteSpace(r.SaveName) ? r.SuggestedName : r.SaveName.Trim()))
            .ToList();

    public MobitecOutImportDialog(string fileName, IReadOnlyList<MobitecOutImportDestination> destinations)
    {
        Title = $"Mobitec OUT importieren – {fileName}";
        Width = 980;
        Height = 640;
        MinWidth = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
        Foreground = Brushes.White;

        var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _rows = new ObservableCollection<Row>(
            destinations.Select((d, i) =>
            {
                var suggested = MobitecTransOutImporter.SuggestDisplayName(d, nameCount);
                var row = new Row(i + 1, d, suggested);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(Row.IsSelected))
                    {
                        UpdateSelectionSummary();
                    }
                };
                return row;
            }));

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Ziele aus OUT auswählen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(title, 0);

        var hint = new TextBlock
        {
            Text = "Linie zeigt „Grafik“, wenn in der OUT ein Linien-Bitmap steckt (z. B. Tasse, McD-Logo). " +
                   "Unter „Speichername“ legst du den Namen in der Zielliste fest. Nur markierte Zeilen werden importiert.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hint, 1);

        var grid = CreateGrid();
        Grid.SetRow(grid, 2);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(MakeButton("Alle", (_, _) => SetAll(true)));
        left.Children.Add(MakeButton("Keine", (_, _) => SetAll(false), marginLeft: 8));
        _selectionSummary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Opacity = 0.8,
            FontSize = 12
        };
        left.Children.Add(_selectionSummary);
        DockPanel.SetDock(left, Dock.Left);
        buttons.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = MakeButton("Abbrechen", (_, _) =>
        {
            DialogResult = false;
            Close();
        }, outlined: true);
        cancel.IsCancel = true;
        var ok = MakeButton("Auswahl importieren", (_, _) =>
        {
            if (_rows.All(r => !r.IsSelected))
            {
                MessageBox.Show(
                    this,
                    "Bitte mindestens ein Ziel auswählen.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        });
        ok.IsDefault = true;
        ok.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(cancel);
        right.Children.Add(ok);
        buttons.Children.Add(right);
        Grid.SetRow(buttons, 3);

        root.Children.Add(title);
        root.Children.Add(hint);
        root.Children.Add(grid);
        root.Children.Add(buttons);
        Content = root;

        UpdateSelectionSummary();
    }

    public static IReadOnlyList<(MobitecOutImportDestination Destination, string SaveName)>? Show(
        Window? owner,
        string fileName,
        IReadOnlyList<MobitecOutImportDestination> destinations)
    {
        var dialog = new MobitecOutImportDialog(fileName, destinations);
        var ok = DialogOwnerHelper.ShowOwnedDialog(dialog, owner) == true;
        return ok ? dialog.SelectedImports : null;
    }

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            // 0 clippt die erste Spalte (Checkbox/Nr.); 1px reicht, Row-Header bleibt unsichtbar.
            RowHeaderWidth = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x36)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x0F, 0x1C, 0x2E)),
            RowBackground = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x36)),
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var checkStyle = new Style(typeof(CheckBox));
        checkStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        checkStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        checkStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 2, 0)));

        var nrStyle = new Style(typeof(TextBlock));
        nrStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
        nrStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        nrStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 8, 0)));

        var checkCol = new DataGridCheckBoxColumn
        {
            Header = "Imp.",
            Binding = new Binding(nameof(Row.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 64,
            MinWidth = 64,
            CanUserResize = false,
            ElementStyle = checkStyle,
            EditingElementStyle = checkStyle
        };
        grid.Columns.Add(checkCol);

        var nrCol = new DataGridTextColumn
        {
            Header = "Nr.",
            Binding = new Binding(nameof(Row.Number)),
            IsReadOnly = true,
            Width = 56,
            MinWidth = 56,
            CanUserResize = false,
            ElementStyle = nrStyle
        };
        grid.Columns.Add(nrCol);

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Linie",
            Binding = new Binding(nameof(Row.LinePreview)),
            IsReadOnly = true,
            Width = 80,
            MinWidth = 64
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Front",
            Binding = new Binding(nameof(Row.FrontPreview)),
            IsReadOnly = true,
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
            MinWidth = 120
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Seite",
            Binding = new Binding(nameof(Row.SidePreview)),
            IsReadOnly = true,
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            MinWidth = 100
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Speichername",
            Binding = new Binding(nameof(Row.SaveName))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            },
            Width = new DataGridLength(1.3, DataGridLengthUnitType.Star),
            MinWidth = 120
        });

        return grid;
    }

    private void SetAll(bool selected)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = selected;
        }

        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var n = _rows.Count(r => r.IsSelected);
        _selectionSummary.Text = $"{n} von {_rows.Count} ausgewählt";
    }

    private static Button MakeButton(string text, RoutedEventHandler onClick, bool outlined = false, double marginLeft = 0)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(marginLeft, 0, 0, 0),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += onClick;
        try
        {
            button.Style = (Style)Application.Current.FindResource(
                outlined ? "MaterialDesignOutlinedButton" : "MaterialDesignRaisedButton");
        }
        catch
        {
            // Fallback ohne MaterialDesign-Theme
        }

        return button;
    }

    public sealed class Row : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _saveName;

        public Row(int number, MobitecOutImportDestination destination, string suggestedName)
        {
            Number = number;
            Destination = destination;
            SuggestedName = suggestedName;
            _saveName = suggestedName;
            LinePreview = destination.LinePreview;
            FrontPreview = destination.FrontPreview;
            SidePreview = destination.SidePreview;
        }

        public int Number { get; }
        public MobitecOutImportDestination Destination { get; }
        public string SuggestedName { get; }
        public string LinePreview { get; }
        public string FrontPreview { get; }
        public string SidePreview { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string SaveName
        {
            get => _saveName;
            set
            {
                var next = value ?? string.Empty;
                if (_saveName == next)
                {
                    return;
                }

                _saveName = next;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
