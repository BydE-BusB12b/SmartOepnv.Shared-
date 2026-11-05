using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.Views;

/// <summary>Mitarbeiter der geladenen Zeitwirtschaft auswählen.</summary>
public sealed class ZeitwirtschaftEmployeePickDialog : Window
{
    private readonly ListBox _list;

    public ZeitwirtschaftMergedEmployee? Selected { get; private set; }

    public ZeitwirtschaftEmployeePickDialog(
        Window? owner,
        IReadOnlyList<ZeitwirtschaftMergedEmployee> employees,
        ZeitwirtschaftMergedEmployee? current)
    {
        Owner = owner;
        Title = "Mitarbeiter auswählen";
        Width = 420;
        Height = 480;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = UiTheme.BackgroundBrush;
        Foreground = UiTheme.ForegroundBrush;

        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var ok = new Button
        {
            Style = null,
            Content = "Übernehmen",
            Width = 110,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        ok.Click += (_, _) => Confirm();
        var cancel = new Button
        {
            Style = null,
            Content = "Abbrechen",
            Width = 110,
            Height = 32,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var title = new TextBlock
        {
            Text = "Mitarbeiter auswählen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(title, Dock.Top);

        _list = new ListBox
        {
            Style = null,
            ItemsSource = employees,
            DisplayMemberPath = nameof(ZeitwirtschaftMergedEmployee.DisplayLine),
            SelectedItem = current,
            Background = UiTheme.SurfaceBrush,
            Foreground = UiTheme.ForegroundBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            FontSize = 15
        };
        _list.MouseDoubleClick += (_, _) => Confirm();

        root.Children.Add(buttons);
        root.Children.Add(title);
        root.Children.Add(_list);
        Content = root;
    }

    private void Confirm()
    {
        if (_list.SelectedItem is not ZeitwirtschaftMergedEmployee employee)
        {
            return;
        }

        Selected = employee;
        DialogResult = true;
        Close();
    }
}
