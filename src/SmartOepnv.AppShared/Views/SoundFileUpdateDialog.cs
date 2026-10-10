using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Zeigt gewählte neue Sounddateien neben den im Planer vorhandenen Dateien gleichen Namens.
/// </summary>
public sealed class SoundFileUpdateDialog : Window
{
    public sealed class Row
    {
        /// <summary>Bestehender Planer-Dateiname, der überschrieben wird.</summary>
        public required string TargetFileName { get; init; }
        public required string ExistingLabel { get; init; }
        public required string NewLabel { get; init; }
        public required string NewPath { get; init; }
        public required bool CanReplace { get; init; }
        public string StatusLabel => CanReplace ? "bereit" : "fehlt im Planer";
    }

    private readonly ObservableCollection<Row> _rows;

    public IReadOnlyList<Row> ReplaceableRows =>
        _rows.Where(r => r.CanReplace).ToList();

    public SoundFileUpdateDialog(IReadOnlyList<Row> rows)
    {
        _rows = new ObservableCollection<Row>(rows);
        Title = "Sounddatei Update";
        Width = 820;
        Height = 480;
        MinWidth = 640;
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

        var replaceCount = _rows.Count(r => r.CanReplace);
        var replace = new Button
        {
            Content = $"Dateien ersetzen ({replaceCount})",
            MinWidth = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = replaceCount > 0,
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        replace.Click += (_, _) =>
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

        buttons.Children.Add(replace);
        buttons.Children.Add(cancel);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock
        {
            Text = "Vorhandene Datei  →  neue Datei",
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Nur Zeilen mit „bereit“ werden ersetzt. Matching: Dateiname, Kartei-Bezeichnung, …_zusammen, 0096_-Präfix, Hbf ↔ Hauptbahnhof (Leerzeichen/Unterstriche egal). Inhalt landet unter dem linken Planer-Namen.",
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var list = new ListView
        {
            ItemsSource = _rows,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
        };
        list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn
                {
                    Header = "Im Planer vorhanden",
                    Width = 300,
                    DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.ExistingLabel))
                },
                new GridViewColumn
                {
                    Header = "Neue Datei",
                    Width = 300,
                    DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.NewLabel))
                },
                new GridViewColumn
                {
                    Header = "Status",
                    Width = 120,
                    DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.StatusLabel))
                }
            }
        };

        root.Children.Add(buttons);
        root.Children.Add(header);
        root.Children.Add(list);
        Content = root;
    }
}
