using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.AppShared.Views;

public sealed class VrrStopFinderDialog : Window
{
    private static readonly Brush ItemTextBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
    private static readonly Brush ItemSubtextBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly ObservableCollection<VrrStopEntry> _hits = [];
    private readonly TextBlock _status;
    private readonly ListBox _list;
    private string _pendingQuery = string.Empty;
    private int _searchGeneration;

    public VrrStopEntry? SelectedEntry { get; private set; }

    public VrrStopFinderDialog(string? initialQuery = null)
    {
        Title = "VRR Haltestelle suchen";
        Width = 640;
        Height = 520;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        Foreground = ItemTextBrush;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "VRR Haltestelle suchen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ItemTextBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var queryBox = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Text = initialQuery?.Trim() ?? string.Empty,
            Foreground = ItemTextBrush,
            Background = Brushes.White
        };
        Grid.SetRow(queryBox, 1);
        root.Children.Add(queryBox);

        _status = new TextBlock
        {
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ItemSubtextBrush,
            Text = "Katalog wird geladen…"
        };
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        _list = new ListBox
        {
            ItemsSource = _hits,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Background = Brushes.White,
            Foreground = ItemTextBrush,
            Margin = new Thickness(0, 0, 0, 12),
            ItemTemplate = CreateItemTemplate(),
            ItemContainerStyle = CreateItemContainerStyle()
        };
        _list.MouseDoubleClick += (_, _) => ConfirmSelection();
        Grid.SetRow(_list, 3);
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Übernehmen", MinWidth = 110, IsDefault = true };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        ok.Click += (_, _) => ConfirmSelection();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ScheduleSearch(_pendingQuery);
        };

        queryBox.TextChanged += (_, _) =>
        {
            _pendingQuery = queryBox.Text.Trim();
            _debounce.Stop();
            _debounce.Start();
        };

        Loaded += async (_, _) =>
        {
            try
            {
                await Task.Run(VrrStopCatalog.EnsureLoaded).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(queryBox.Text))
                {
                    ScheduleSearch(queryBox.Text.Trim());
                }
                else
                {
                    SetStatus($"Katalog: {VrrStopCatalog.Size:N0} Haltestellen – Text eingeben zum Suchen.");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Katalog konnte nicht geladen werden: {ex.Message}");
            }

            queryBox.Focus();
            queryBox.SelectAll();
        };
    }

    private void ConfirmSelection()
    {
        if (_list.SelectedItem is VrrStopEntry entry)
        {
            SelectedEntry = entry;
            DialogResult = true;
            Close();
            return;
        }

        if (_hits.Count == 1)
        {
            SelectedEntry = _hits[0];
            DialogResult = true;
            Close();
        }
    }

    private void ScheduleSearch(string query)
    {
        var generation = ++_searchGeneration;
        if (string.IsNullOrWhiteSpace(query))
        {
            ApplyHits(generation, [], query);
            return;
        }

        SetStatus("Suche…");
        Task.Run(() =>
        {
            try
            {
                VrrStopCatalog.EnsureLoaded();
                return VrrStopCatalog.Suggest(query, limit: 80).ToList();
            }
            catch
            {
                return [];
            }
        }).ContinueWith(
            t => Dispatcher.BeginInvoke(() => ApplyHits(generation, t.Result, query)),
            TaskScheduler.Default);
    }

    private void ApplyHits(int generation, IReadOnlyList<VrrStopEntry> hits, string query)
    {
        if (generation != _searchGeneration)
        {
            return;
        }

        _hits.Clear();
        foreach (var entry in hits)
        {
            _hits.Add(entry);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus($"Katalog: {VrrStopCatalog.Size:N0} Haltestellen – Text eingeben zum Suchen.");
            _list.SelectedIndex = -1;
            return;
        }

        SetStatus(hits.Count == 0
            ? $"Keine Treffer für „{query}“"
            : $"{hits.Count} Treffer (von {VrrStopCatalog.Size:N0})");

        if (hits.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(_hits[0]);
        }
        else
        {
            _list.SelectedIndex = -1;
        }
    }

    private void SetStatus(string text) => _status.Text = text;

    private static Style CreateItemContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 40.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ItemTextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        return style;
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate(typeof(VrrStopEntry));
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(StackPanel.MarginProperty, new Thickness(8, 6, 8, 6));

        var line1 = new FrameworkElementFactory(typeof(TextBlock));
        line1.SetBinding(TextBlock.TextProperty, new Binding(nameof(VrrStopEntry.DisplayLine)));
        line1.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        line1.SetValue(TextBlock.ForegroundProperty, ItemTextBrush);
        line1.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

        var line2 = new FrameworkElementFactory(typeof(TextBlock));
        line2.SetBinding(TextBlock.TextProperty, new Binding(nameof(VrrStopEntry.Subtitle)));
        line2.SetValue(TextBlock.ForegroundProperty, ItemSubtextBrush);
        line2.SetValue(TextBlock.FontSizeProperty, 12.0);
        line2.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        line2.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

        stack.AppendChild(line1);
        stack.AppendChild(line2);
        template.VisualTree = stack;
        return template;
    }
}
