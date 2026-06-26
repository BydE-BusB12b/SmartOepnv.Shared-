using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public sealed class EmbeddedSoundPickerDialog : Window
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ObservableCollection<string> _filtered = [];
    private readonly List<string> _allNames;
    private readonly IReadOnlyDictionary<string, string> _searchHintsByFileName;
    private readonly TextBlock _status;
    private string _pendingQuery = string.Empty;

    public string? SelectedFileName { get; private set; }

    public EmbeddedSoundPickerDialog(
        IReadOnlyList<string> soundFileNames,
        string? initialSearch = null,
        IReadOnlyDictionary<string, string>? searchHintsByFileName = null)
    {
        _allNames = soundFileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _searchHintsByFileName = searchHintsByFileName
                                 ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Title = "Ansage wählen";
        Width = 560;
        Height = 480;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"Ansage wählen ({_allNames.Count})",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var queryBox = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Text = initialSearch?.Trim() ?? string.Empty
        };
        Grid.SetRow(queryBox, 1);
        root.Children.Add(queryBox);

        _status = new TextBlock
        {
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        var list = new ListBox
        {
            ItemsSource = _filtered,
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
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Übernehmen", MinWidth = 110, IsDefault = true };
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
        var name = list.SelectedItem as string ?? (_filtered.Count == 1 ? _filtered[0] : null);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SelectedFileName = name;
        DialogResult = true;
        Close();
    }

    private void ApplyFilter(string query)
    {
        _filtered.Clear();
        IEnumerable<string> source = _allNames;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = _allNames.Where(n => EmbeddedSoundSearch.Matches(
                n,
                query,
                _searchHintsByFileName.GetValueOrDefault(n)));
        }

        foreach (var name in source)
        {
            _filtered.Add(name);
        }

        var count = _filtered.Count;
        _status.Text = string.IsNullOrWhiteSpace(query)
            ? "Dateiname antippen oder doppelklicken."
            : count == 0
                ? $"Keine Treffer für „{query}“"
                : count == 1
                    ? "1 Treffer"
                    : $"{count} Treffer";
    }
}
