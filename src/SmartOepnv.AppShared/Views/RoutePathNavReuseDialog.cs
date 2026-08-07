using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.Views;

public sealed class RoutePathNavReuseDialog : Window
{
    private readonly ObservableCollection<Row> _rows = [];
    private readonly ICollectionView _view;
    private string _filter = string.Empty;

    public IReadOnlyList<RoutePathNavReuseCandidate> SelectedMatches =>
        _rows.Where(r => r.IsSelected).Select(r => r.Candidate).ToList();

    public RoutePathNavReuseDialog(IReadOnlyList<RoutePathNavReuseCandidate> candidates)
    {
        Title = "Navidaten übernehmen";
        Width = 760;
        Height = 520;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        foreach (var c in candidates)
        {
            var row = new Row(c);
            row.PropertyChanged += OnRowSelectionChanged;
            _rows.Add(row);
        }

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Passende Navidaten gefunden",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);

        var hint = new TextBlock
        {
            Text = "Gleiche Haltestellenfolgen existieren bereits in anderen Routen (auch als Teilabschnitt). " +
                   "Pro Abschnitt können mehrere Quellen zur Auswahl stehen – standardmäßig ist die mit den meisten " +
                   "gesnappten Verbindungen markiert. Die Routen-Suche oben filtert diesen Dialog nicht.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hint, 1);

        var filterPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var filterLabel = new TextBlock
        {
            Text = "Quellen filtern:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5)),
            FontSize = 12
        };
        DockPanel.SetDock(filterLabel, Dock.Left);
        filterPanel.Children.Add(filterLabel);
        var filterBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x36)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        filterBox.TextChanged += (_, _) =>
        {
            _filter = (filterBox.Text ?? string.Empty).Trim();
            _view.Refresh();
        };
        filterPanel.Children.Add(filterBox);
        Grid.SetRow(filterPanel, 2);

        var list = new ListBox
        {
            ItemsSource = _view,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)),
            Foreground = Brushes.White
        };
        list.ItemTemplate = BuildItemTemplate();
        Grid.SetRow(list, 3);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Überspringen", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = new Button { Content = "Übernehmen", MinWidth = 120, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (SelectedMatches.Count == 0)
            {
                MessageBox.Show(this, "Bitte mindestens einen Abschnitt auswählen.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 4);

        root.Children.Add(title);
        root.Children.Add(hint);
        root.Children.Add(filterPanel);
        root.Children.Add(list);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            filterBox.Focus();
            Keyboard.Focus(filterBox);
        };
    }

    private void OnRowSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Row.IsSelected) || sender is not Row changed || !changed.IsSelected)
        {
            return;
        }

        // Pro Ziel-Abschnitt nur eine Quelle (wie Radio-Buttons).
        foreach (var row in _rows)
        {
            if (ReferenceEquals(row, changed))
            {
                continue;
            }

            if (row.Candidate.TargetFirstListIndex == changed.Candidate.TargetFirstListIndex &&
                row.Candidate.TargetLastListIndex == changed.Candidate.TargetLastListIndex)
            {
                row.IsSelected = false;
            }
        }
    }

    private bool FilterRow(object obj)
    {
        if (obj is not Row row)
        {
            return false;
        }

        if (string.IsNullOrEmpty(_filter))
        {
            return true;
        }

        return row.DisplayText.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private static DataTemplate BuildItemTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        factory.SetValue(StackPanel.MarginProperty, new Thickness(4, 6, 4, 6));

        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(Row.IsSelected)));
        check.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 10, 0));
        check.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(check);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.DisplayText)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        factory.AppendChild(text);

        return new DataTemplate { VisualTree = factory };
    }

    private sealed class Row : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Row(RoutePathNavReuseCandidate candidate)
        {
            Candidate = candidate;
            _isSelected = candidate.IsPreferredDefault;
        }

        public RoutePathNavReuseCandidate Candidate { get; }

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string DisplayText =>
            $"{Candidate.FromLabel} → {Candidate.ToLabel} ({Candidate.StopLabels.Count} Haltestellen) · " +
            $"Quelle: {Candidate.SourceRouteKey} · {Candidate.SnappedEdgeCount} Verbindung(en)";

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
