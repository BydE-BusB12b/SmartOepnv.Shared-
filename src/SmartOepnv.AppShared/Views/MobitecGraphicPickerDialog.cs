using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>Liste der gespeicherten Grafiken aus dem Betrieb-Ordner, mit Bildvorschau.</summary>
public sealed class MobitecGraphicPickerDialog : Window
{
    private readonly string _folder;
    private readonly ListBox _list;
    private readonly Button _useButton;
    private readonly Button _deleteButton;
    private readonly TextBlock _empty;

    public MobitecSavedGraphic? Selected { get; private set; }

    private MobitecGraphicPickerDialog(string folder)
    {
        _folder = folder;
        Title = "Grafik holen";
        Width = 920;
        Height = 680;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResize;
        Background = UiTheme.BackgroundBrush;
        Foreground = UiTheme.ForegroundBrush;

        var root = new DockPanel { Margin = new Thickness(20) };

        var bar = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);

        var openFolder = DialogButton("Ordner öffnen");
        openFolder.MinWidth = 160;
        openFolder.Click += (_, _) => OpenFolder();
        Grid.SetColumn(openFolder, 0);
        bar.Children.Add(openFolder);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);

        _useButton = DialogButton("Einsetzen", filled: true);
        _useButton.MinWidth = 140;
        _useButton.IsDefault = true;
        _useButton.Click += (_, _) => Accept();
        _deleteButton = DialogButton("Löschen");
        _deleteButton.MinWidth = 120;
        _deleteButton.Click += (_, _) => DeleteSelected();
        var cancel = DialogButton("Abbrechen");
        cancel.MinWidth = 130;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(0);
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        actions.Children.Add(_useButton);
        actions.Children.Add(_deleteButton);
        actions.Children.Add(cancel);

        var hint = new TextBlock
        {
            Text = "Gespeicherte Grafiken dieses Betriebs. Einsetzen legt die Grafik oben links auf die aktive Anzeige, danach verschieben die Pfeiltasten. Dieselbe Grafik liegt im Ordner als PNG.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = UiTheme.ForegroundBrush
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var path = new TextBlock
        {
            Text = folder,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Opacity = 0.75,
            Foreground = UiTheme.ForegroundBrush
        };
        DockPanel.SetDock(path, Dock.Top);
        root.Children.Add(path);

        _empty = new TextBlock
        {
            Text = "Noch keine Grafik gespeichert.",
            Margin = new Thickness(8),
            Foreground = Brushes.Black,
            Visibility = Visibility.Collapsed
        };
        _list = new ListBox
        {
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source || FindItem(source) is null)
            {
                return;
            }

            Accept();
        };
        _list.SelectionChanged += (_, _) => UpdateButtons();
        var host = new DockPanel();
        DockPanel.SetDock(_empty, Dock.Top);
        host.Children.Add(_empty);
        host.Children.Add(_list);
        root.Children.Add(host);
        Content = root;
        Reload();
        Loaded += (_, _) => _list.Focus();
    }

    public static MobitecSavedGraphic? Pick(Window owner, string folder)
    {
        var dialog = new MobitecGraphicPickerDialog(folder) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Selected : null;
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var graphic in MobitecGraphicLibrary.List(_folder))
        {
            var imagePath = MobitecGraphicLibrary.EnsureImage(_folder, graphic);
            var row = new Grid { Margin = new Thickness(8, 10, 8, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var preview = new Image
            {
                Source = LoadImage(imagePath),
                Height = 96,
                MaxWidth = 520,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
            var frame = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Child = preview,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(frame, 0);
            row.Children.Add(frame);

            var caption = new StackPanel
            {
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            caption.Children.Add(new TextBlock
            {
                Text = graphic.Name,
                FontSize = 18,
                Foreground = Brushes.Black
            });
            caption.Children.Add(new TextBlock
            {
                Text = $"{graphic.Width}×{graphic.Height}  ·  {Path.GetFileName(imagePath)}",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
            });
            Grid.SetColumn(caption, 1);
            row.Children.Add(caption);

            _list.Items.Add(new ListBoxItem
            {
                Content = row,
                Tag = graphic,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4)
            });
        }

        _empty.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        UpdateButtons();
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void UpdateButtons()
    {
        var hasItem = Current() is not null;
        _useButton.IsEnabled = hasItem;
        _deleteButton.IsEnabled = hasItem;
    }

    private MobitecSavedGraphic? Current() =>
        _list.SelectedItem is ListBoxItem { Tag: MobitecSavedGraphic graphic } ? graphic : null;

    private void Accept()
    {
        if (Current() is not { } graphic)
        {
            return;
        }

        Selected = graphic;
        DialogResult = true;
        Close();
    }

    private void DeleteSelected()
    {
        if (Current() is not { } graphic)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Grafik „{graphic.Name}“ und die Bilddatei aus dem Ordner löschen?",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        MobitecGraphicLibrary.Delete(_folder, graphic.Name);
        Reload();
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = _folder,
            UseShellExecute = true
        });
    }

    private static ListBoxItem? FindItem(DependencyObject source)
    {
        while (source is not ListBoxItem)
        {
            if (source is not FrameworkElement { Parent: DependencyObject parent })
            {
                return null;
            }

            source = parent;
        }

        return (ListBoxItem)source;
    }

    private static Button DialogButton(string text, bool filled = false)
    {
        var button = new Button
        {
            Style = null,
            Content = text,
            MinHeight = 40,
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 14,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(filled ? 0 : 1)
        };
        if (filled)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            button.Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
        }
        else
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            button.Foreground = Brushes.White;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }

        return button;
    }
}
