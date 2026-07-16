using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.Views;

public sealed class RoutePathNavReuseDialog : Window
{
    private readonly ObservableCollection<Row> _rows = [];

    public IReadOnlyList<RoutePathNavReuseCandidate> SelectedMatches =>
        _rows.Where(r => r.IsSelected).Select(r => r.Candidate).ToList();

    public RoutePathNavReuseDialog(IReadOnlyList<RoutePathNavReuseCandidate> candidates)
    {
        Title = "Navidaten übernehmen";
        Width = 720;
        Height = 460;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        foreach (var c in candidates)
        {
            _rows.Add(new Row(c));
        }

        var root = new Grid { Margin = new Thickness(20) };
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
                   "Ausgewählte Verbindungen werden übernommen – kein erneutes Snappen nötig.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(hint, 1);

        var list = new ListBox
        {
            ItemsSource = _rows,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4A)),
            Foreground = Brushes.White
        };
        list.ItemTemplate = BuildItemTemplate();
        Grid.SetRow(list, 2);

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
        Grid.SetRow(buttons, 3);

        root.Children.Add(title);
        root.Children.Add(hint);
        root.Children.Add(list);
        root.Children.Add(buttons);
        Content = root;
    }

    private static DataTemplate BuildItemTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        factory.SetValue(StackPanel.MarginProperty, new Thickness(4, 6, 4, 6));

        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(Row.IsSelected)));
        check.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 10, 0));
        check.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(check);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.DisplayText)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        factory.AppendChild(text);

        return new DataTemplate { VisualTree = factory };
    }

    private sealed class Row
    {
        public Row(RoutePathNavReuseCandidate candidate)
        {
            Candidate = candidate;
            IsSelected = true;
        }

        public RoutePathNavReuseCandidate Candidate { get; }
        public bool IsSelected { get; set; }

        public string DisplayText =>
            $"{Candidate.FromLabel} → {Candidate.ToLabel} ({Candidate.StopLabels.Count} Haltestellen) · " +
            $"Quelle: {Candidate.SourceRouteKey} · {Candidate.SnappedEdgeCount} Verbindung(en)";
    }
}
