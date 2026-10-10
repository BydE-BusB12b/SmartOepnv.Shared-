using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Zeigt doppelte Ansagen gruppiert; pro Gruppe Hauptdatei (Radio) und zu löschende Einträge (Checkbox).
/// </summary>
public sealed class DuplicateAnnouncementsDialog : Window
{
    public sealed class EntryRow : INotifyPropertyChanged
    {
        private bool _isPrimary;
        private bool _markForDeletion;

        public required ManagedAnnouncementTemplateItem Announcement { get; init; }
        public required string Label { get; init; }
        public required string Detail { get; init; }
        public bool IsMergedFile { get; init; }
        public required DuplicateGroup Group { get; init; }

        public bool IsPrimary
        {
            get => _isPrimary;
            set
            {
                if (_isPrimary == value)
                {
                    return;
                }

                _isPrimary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDelete));
                if (value)
                {
                    MarkForDeletion = false;
                    Group.SetPrimary(this);
                }
            }
        }

        public bool MarkForDeletion
        {
            get => _markForDeletion;
            set
            {
                if (_isPrimary)
                {
                    value = false;
                }

                if (_markForDeletion == value)
                {
                    return;
                }

                _markForDeletion = value;
                OnPropertyChanged();
                Group.NotifySelectionChanged();
            }
        }

        public bool CanDelete => !IsPrimary;

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void ForcePrimary(bool value)
        {
            _isPrimary = value;
            if (value)
            {
                _markForDeletion = false;
                OnPropertyChanged(nameof(MarkForDeletion));
            }

            OnPropertyChanged(nameof(IsPrimary));
            OnPropertyChanged(nameof(CanDelete));
        }

        internal void NotifyCanDeleteChanged() => OnPropertyChanged(nameof(CanDelete));

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class DuplicateGroup : INotifyPropertyChanged
    {
        public required string Title { get; init; }
        public ObservableCollection<EntryRow> Entries { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? SelectionChanged;

        public void SetPrimary(EntryRow primary)
        {
            foreach (var entry in Entries)
            {
                if (ReferenceEquals(entry, primary))
                {
                    continue;
                }

                if (entry.IsPrimary)
                {
                    entry.ForcePrimary(false);
                }

                entry.NotifyCanDeleteChanged();
            }

            primary.NotifyCanDeleteChanged();
            NotifySelectionChanged();
        }

        public void NotifySelectionChanged()
        {
            SelectionChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Entries)));
        }
    }

    private readonly ObservableCollection<DuplicateGroup> _groups;
    private readonly Button _applyButton;

    public IReadOnlyList<(ManagedAnnouncementTemplateItem Primary, IReadOnlyList<ManagedAnnouncementTemplateItem> ToDelete)>
        Resolutions =>
        _groups
            .Select(g =>
            {
                var primary = g.Entries.FirstOrDefault(e => e.IsPrimary)?.Announcement
                              ?? g.Entries[0].Announcement;
                var toDelete = g.Entries
                    .Where(e => e.MarkForDeletion && !ReferenceEquals(e.Announcement, primary))
                    .Select(e => e.Announcement)
                    .ToList();
                return (primary, (IReadOnlyList<ManagedAnnouncementTemplateItem>)toDelete);
            })
            .Where(r => r.Item2.Count > 0)
            .ToList();

    public DuplicateAnnouncementsDialog(IReadOnlyList<DuplicateGroup> groups)
    {
        _groups = new ObservableCollection<DuplicateGroup>(groups);
        foreach (var group in _groups)
        {
            group.SelectionChanged += RefreshApplyButton;
        }

        Title = "Doppelte Ansagen prüfen";
        Width = 900;
        Height = 560;
        MinWidth = 720;
        MinHeight = 420;
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

        _applyButton = new Button
        {
            Content = "Bereinigen",
            MinWidth = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        _applyButton.Click += (_, _) =>
        {
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

        buttons.Children.Add(_applyButton);
        buttons.Children.Add(cancel);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock
        {
            Text = $"{_groups.Count} Duplikat-Gruppe(n)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4)
        });
        header.Children.Add(new TextBlock
        {
            Text =
                "Pro Gruppe eine Hauptdatei wählen (bleibt). Markierte Duplikate werden gelöscht; " +
                "Haltestellen mit deren Ton erhalten automatisch die Hauptdatei. " +
                "Gelb / „_zusammen“ = Zusammenfüge-Datei – wird nicht als Hauptdatei vorgeschlagen.",
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var listPanel = new StackPanel();
        foreach (var group in _groups)
        {
            listPanel.Children.Add(BuildGroupCard(group));
        }

        scroll.Content = listPanel;

        root.Children.Add(buttons);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Content = root;

        RefreshApplyButton();
    }

    private void RefreshApplyButton()
    {
        var deleteCount = _groups.Sum(g => g.Entries.Count(e => e.MarkForDeletion));
        _applyButton.IsEnabled = deleteCount > 0;
        _applyButton.Content = deleteCount > 0
            ? $"Bereinigen ({deleteCount} löschen)"
            : "Bereinigen";
    }

    private UIElement BuildGroupCard(DuplicateGroup group)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var groupName = "dup_" + Guid.NewGuid().ToString("N");
        foreach (var entry in group.Entries)
        {
            stack.Children.Add(BuildEntryRow(entry, groupName));
        }

        border.Child = stack;
        return border;
    }

    private static UIElement BuildEntryRow(EntryRow entry, string radioGroupName)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (entry.IsMergedFile)
        {
            grid.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xEB, 0x3B));
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var labelBlock = new TextBlock
        {
            Text = entry.Label,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = entry.IsMergedFile ? FontWeights.SemiBold : FontWeights.Normal
        };
        if (entry.IsMergedFile)
        {
            labelBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A));
        }

        text.Children.Add(labelBlock);
        text.Children.Add(new TextBlock
        {
            Text = entry.Detail,
            FontSize = 11,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            Foreground = entry.IsMergedFile
                ? new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A))
                : Brushes.White
        });
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var primary = new RadioButton
        {
            Content = "Hauptdatei",
            GroupName = radioGroupName,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = entry.IsPrimary
        };
        primary.Checked += (_, _) =>
        {
            if (primary.IsChecked == true)
            {
                entry.IsPrimary = true;
            }
        };
        Grid.SetColumn(primary, 1);
        grid.Children.Add(primary);

        var delete = new CheckBox
        {
            Content = "Löschen",
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = entry.MarkForDeletion,
            IsEnabled = entry.CanDelete
        };
        delete.Checked += (_, _) => entry.MarkForDeletion = true;
        delete.Unchecked += (_, _) => entry.MarkForDeletion = false;
        entry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(EntryRow.CanDelete) or nameof(EntryRow.IsPrimary))
            {
                delete.IsEnabled = entry.CanDelete;
                if (!entry.CanDelete)
                {
                    delete.IsChecked = false;
                }
            }

            if (args.PropertyName == nameof(EntryRow.MarkForDeletion) &&
                delete.IsChecked != entry.MarkForDeletion)
            {
                delete.IsChecked = entry.MarkForDeletion;
            }

            if (args.PropertyName == nameof(EntryRow.IsPrimary) &&
                primary.IsChecked != entry.IsPrimary)
            {
                primary.IsChecked = entry.IsPrimary;
            }
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);

        return grid;
    }
}
