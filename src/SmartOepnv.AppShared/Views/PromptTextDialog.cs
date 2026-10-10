using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartOepnv.AppShared.Views;

/// <summary>Einfacher Texteingabe-Dialog (z. B. Linie für Spezialbausteine).</summary>
public sealed class PromptTextDialog : Window
{
    private readonly TextBox _textBox;

    public string ResultText => _textBox.Text?.Trim() ?? string.Empty;

    public PromptTextDialog(string title, string prompt, string initialValue = "")
    {
        Title = title;
        Width = 420;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        Foreground = Brushes.White;

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
            Content = "OK",
            Width = 88,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        ok.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Style = null,
            Content = "Abbrechen",
            Width = 88,
            Height = 32,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        // Style=null: MaterialDesign-Underline-TextBox wäre auf dunklem Dialog kaum sichtbar.
        _textBox = new TextBox
        {
            Style = null,
            Text = initialValue,
            Margin = new Thickness(0, 10, 0, 0),
            MinHeight = 36,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        };

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Opacity = 0.95
        });
        body.Children.Add(_textBox);

        root.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;
        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public static string? Ask(Window? owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new PromptTextDialog(title, prompt, initialValue);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }
}
