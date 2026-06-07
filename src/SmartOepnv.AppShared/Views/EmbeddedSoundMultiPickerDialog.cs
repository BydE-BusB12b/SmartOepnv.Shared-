using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SmartOepnv.AppShared.Views;

public sealed class EmbeddedSoundMultiPickerDialog : Window
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ObservableCollection<string> _filtered = [];
    private readonly ObservableCollection<string> _selected = [];
    private readonly List<string> _allNames;
    private readonly TextBlock _status;
    private string _pendingQuery = string.Empty;

    public IReadOnlyList<string> SelectedFileNames => _selected.ToList();

    public EmbeddedSoundMultiPickerDialog(IReadOnlyList<string> soundFileNames, string? initialSearch = null)
    {
        _allNames = soundFileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        Title = "Mehrere Ansagen zusammenfügen";
        Width = 760;
        Height = 520;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Schnipsel wählen und Reihenfolge festlegen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var hint = new TextBlock
        {
            Text = "Links antippen oder doppelklicken → rechts. Mindestens zwei Ansagen.",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);

        var queryBox = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Text = initialSearch?.Trim() ?? string.Empty
        };
        Grid.SetRow(queryBox, 2);
        root.Children.Add(queryBox);

        var listsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(listsGrid, 3);
        root.Children.Add(listsGrid);

        var availablePanel = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var availableHeader = new TextBlock
        {
            Text = "Verfügbar",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(availableHeader, Dock.Top);
        availablePanel.Children.Add(availableHeader);

        var availableList = new ListBox
        {
            ItemsSource = _filtered,
            BorderThickness = new Thickness(1)
        };
        availableList.MouseDoubleClick += (_, _) => AddSelectedFromAvailable(availableList);
        DockPanel.SetDock(availableList, Dock.Bottom);
        availablePanel.Children.Add(availableList);
        Grid.SetColumn(availablePanel, 0);
        listsGrid.Children.Add(availablePanel);

        var selectedList = new ListBox
        {
            ItemsSource = _selected,
            BorderThickness = new Thickness(1)
        };
        selectedList.MouseDoubleClick += (_, _) => RemoveFromSelected(selectedList);

        var moveButtons = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        var addButton = new Button { Content = "→", MinWidth = 36, Margin = new Thickness(0, 0, 0, 6) };
        var removeButton = new Button { Content = "←", MinWidth = 36, Margin = new Thickness(0, 0, 0, 6) };
        var upButton = new Button { Content = "↑", MinWidth = 36, Margin = new Thickness(0, 0, 0, 6) };
        var downButton = new Button { Content = "↓", MinWidth = 36 };
        addButton.Click += (_, _) => AddSelectedFromAvailable(availableList);
        removeButton.Click += (_, _) => RemoveFromSelected(selectedList);
        upButton.Click += (_, _) => MoveSelected(selectedList, -1);
        downButton.Click += (_, _) => MoveSelected(selectedList, 1);
        moveButtons.Children.Add(addButton);
        moveButtons.Children.Add(removeButton);
        moveButtons.Children.Add(upButton);
        moveButtons.Children.Add(downButton);
        Grid.SetColumn(moveButtons, 1);
        listsGrid.Children.Add(moveButtons);

        var selectedPanel = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        var selectedHeader = new TextBlock
        {
            Text = "Reihenfolge",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(selectedHeader, Dock.Top);
        selectedPanel.Children.Add(selectedHeader);
        DockPanel.SetDock(selectedList, Dock.Bottom);
        selectedPanel.Children.Add(selectedList);
        Grid.SetColumn(selectedPanel, 2);
        listsGrid.Children.Add(selectedPanel);

        _status = new TextBlock
        {
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        };

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(footer, 4);
        footer.Children.Add(_status);
        DockPanel.SetDock(_status, Dock.Left);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Übernehmen", MinWidth = 120, IsDefault = true };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        ok.Click += (_, _) => ConfirmSelection();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        footer.Children.Add(buttons);
        DockPanel.SetDock(buttons, Dock.Right);
        root.Children.Add(footer);

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

            UpdateStatus();
        };

        _selected.CollectionChanged += (_, _) => UpdateStatus();
    }

    private void AddSelectedFromAvailable(ListBox availableList)
    {
        if (availableList.SelectedItem is not string name || _selected.Contains(name))
        {
            return;
        }

        _selected.Add(name);
    }

    private void RemoveFromSelected(ListBox selectedList)
    {
        if (selectedList.SelectedItem is not string name)
        {
            return;
        }

        _selected.Remove(name);
    }

    private void MoveSelected(ListBox selectedList, int delta)
    {
        var index = selectedList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= _selected.Count)
        {
            return;
        }

        _selected.Move(index, newIndex);
        selectedList.SelectedIndex = newIndex;
    }

    private void ConfirmSelection()
    {
        if (_selected.Count < 2)
        {
            MessageBox.Show(
                this,
                "Bitte mindestens zwei Ansagen in der Reihenfolge-Liste wählen.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ApplyFilter(string query)
    {
        _filtered.Clear();
        IEnumerable<string> source = _allNames;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = _allNames.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var name in source)
        {
            _filtered.Add(name);
        }

        UpdateStatus(query, _filtered.Count);
    }

    private void UpdateStatus(string? query = null, int? filteredCount = null)
    {
        var suffix = _selected.Count >= 2 ? string.Empty : " (mind. 2)";
        if (string.IsNullOrWhiteSpace(query))
        {
            _status.Text = $"{_selected.Count} gewählt{suffix} · {_allNames.Count} verfügbar";
            return;
        }

        var count = filteredCount ?? _filtered.Count;
        _status.Text = count == 0
            ? $"Keine Treffer für „{query}“"
            : $"{_selected.Count} gewählt{suffix} · {count} von {_allNames.Count} Treffern";
    }
}
