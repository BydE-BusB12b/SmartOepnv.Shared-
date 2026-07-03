using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

internal static class VehicleKomUi
{
    private static readonly Brush WindowBackground = FrozenRgb(0x0A, 0x10, 0x20);
    private static readonly Brush PanelBackground = FrozenRgb(0x00, 0x21, 0x71);
    private static readonly Brush AccentBackground = FrozenRgb(0x0D, 0x47, 0xA1);
    private static readonly Brush InputBackground = FrozenRgb(0x1E, 0x5A, 0x9E);
    private static readonly Brush AccentBorder = FrozenRgb(0x42, 0xA5, 0xF5);
    private static readonly Brush ForegroundBrush = Brushes.White;
    private static readonly Brush MutedForeground = FrozenRgb(0xBB, 0xDE, 0xFB);

    public static void PrepareWindow(Window window, UIElement content)
    {
        window.Background = WindowBackground;
        window.Foreground = ForegroundBrush;
        WindowTitleBarHelper.ApplyDarkWindowBackground(window);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(window);

        window.Content = new Border
        {
            Background = PanelBackground,
            Padding = new Thickness(24),
            Child = content
        };
    }

    public static bool EnsureDropboxConnected(Window owner)
    {
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            return true;
        }

        SmartConfirmDialog.ShowInfo(owner,
            "Fernsteuerung",
            "Dropbox nicht verbunden – bitte unter Einstellungen verbinden.");
        return false;
    }

    public static string? ResolvePhoneOrWarn(Window owner, VehicleListItemViewModel vehicle)
    {
        var phone = vehicle.ResolvePhoneNumber();
        if (!string.IsNullOrWhiteSpace(phone))
        {
            return phone;
        }

        SmartConfirmDialog.ShowInfo(owner,
            "Fernsteuerung",
            $"Für „{vehicle.DisplayName}“ ist keine Telefonnummer bekannt – Fernsteuerung nicht möglich.");
        return null;
    }

    public static TextBlock MakeText(
        string text,
        double fontSize = 14,
        FontWeight? weight = null,
        Thickness? margin = null,
        bool muted = false) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0, 0, 0, 8),
            Foreground = muted ? MutedForeground : ForegroundBrush
        };

    public static Button MakeButton(
        string content,
        bool primary = false,
        bool isCancel = false,
        bool isDefault = false,
        double minWidth = 0,
        Thickness? margin = null,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
    {
        var button = new Button
        {
            Content = content,
            Background = primary ? AccentBackground : InputBackground,
            Foreground = ForegroundBrush,
            BorderBrush = AccentBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            MinHeight = 36,
            HorizontalAlignment = horizontalAlignment,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = isCancel,
            IsDefault = isDefault
        };

        if (minWidth > 0)
        {
            button.MinWidth = minWidth;
        }

        if (margin is not null)
        {
            button.Margin = margin.Value;
        }

        return button;
    }

    public static Button MakeActionButton(string label, Action onClick)
    {
        var button = MakeButton(
            label,
            horizontalAlignment: HorizontalAlignment.Stretch);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static void StyleListBox(ListBox list)
    {
        list.Background = InputBackground;
        list.Foreground = ForegroundBrush;
        list.BorderBrush = AccentBorder;
        list.BorderThickness = new Thickness(1);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ForegroundBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, AccentBackground));
        style.Triggers.Add(selected);

        var hover = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x55, 0x0D, 0x47, 0xA1))));
        style.Triggers.Add(hover);

        list.ItemContainerStyle = style;
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = InputBackground;
        comboBox.Foreground = ForegroundBrush;
        comboBox.BorderBrush = AccentBorder;
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.Background = InputBackground;
        textBox.Foreground = ForegroundBrush;
        textBox.BorderBrush = AccentBorder;
        textBox.CaretBrush = ForegroundBrush;
    }

    public static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.Foreground = ForegroundBrush;
    }

    public static StackPanel MakeButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    private static SolidColorBrush FrozenRgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
