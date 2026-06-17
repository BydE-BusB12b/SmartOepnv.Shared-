using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class PickStopFromLibraryDialog : Window
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ObservableCollection<ManagedStopTemplateItem> _filtered = [];
    private readonly List<ManagedStopTemplateItem> _allTemplates;
    private readonly TextBlock _status;
    private string _pendingQuery = string.Empty;

    public ManagedStopTemplateItem? SelectedTemplate { get; private set; }

    public PickStopFromLibraryDialog(IReadOnlyList<ManagedStopTemplateItem> templates, string? initialSearch = null)
    {
        _allTemplates = templates
            .Where(t => t.HasPersistableContent())
            .OrderBy(t => t.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = "Haltestelle aus Kartei";
        Width = 620;
        Height = 520;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"Haltestelle aus Kartei wählen ({_allTemplates.Count})",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var queryBox = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Text = initialSearch?.Trim() ?? string.Empty,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28))
        };
        Grid.SetRow(queryBox, 1);
        root.Children.Add(queryBox);

        _status = new TextBlock
        {
            Opacity = 0.75,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        var list = new ListBox
        {
            ItemsSource = _filtered,
            DisplayMemberPath = nameof(ManagedStopTemplateItem.DisplayLabel),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12)
        };
        list.MouseDoubleClick += (_, _) => ConfirmSelection(list);
        Grid.SetRow(list, 3);
        root.Children.Add(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), MinHeight = 36 };
        var ok = new Button { Content = "Einfügen", MinWidth = 110, IsDefault = true, MinHeight = 36 };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        ok.Click += (_, _) => ConfirmSelection(list);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplyFilter(_pendingQuery);
        };

        queryBox.TextChanged += (_, _) =>
        {
            _pendingQuery = queryBox.Text.Trim();
            _debounce.Stop();
            _debounce.Start();
        };

        Loaded += (_, _) =>
        {
            ApplyFilter(queryBox.Text.Trim());
            queryBox.Focus();
            if (!string.IsNullOrEmpty(queryBox.Text))
            {
                queryBox.SelectAll();
            }
        };
    }

    private void ConfirmSelection(ListBox list)
    {
        var item = list.SelectedItem as ManagedStopTemplateItem
                   ?? (_filtered.Count == 1 ? _filtered[0] : null);
        if (item is null)
        {
            _status.Text = "Bitte eine Haltestelle auswählen.";
            return;
        }

        SelectedTemplate = item;
        DialogResult = true;
        Close();
    }

    private void ApplyFilter(string query)
    {
        _filtered.Clear();
        if (_allTemplates.Count == 0)
        {
            _status.Text = "Keine Haltestellen in der Kartei – bitte unter „Haltestellen“ anlegen.";
            return;
        }

        IEnumerable<ManagedStopTemplateItem> matches = _allTemplates;
        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = _allTemplates.Where(t =>
                t.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.StopCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.StopNameItcs.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.VrrStopId.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in matches)
        {
            _filtered.Add(item);
        }

        _status.Text = _filtered.Count == _allTemplates.Count
            ? $"{_filtered.Count} Haltestelle(n)"
            : $"{_filtered.Count} von {_allTemplates.Count} Treffer";
    }
}
