using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.Views;

public sealed class ZeitwirtschaftVoidDialog : Window
{
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;

    private readonly TextBox _reasonBox;
    private readonly TextBlock _errorText;

    public string VoidReason { get; private set; } = string.Empty;

    public ZeitwirtschaftVoidDialog(ZeitwirtschaftTimeTableRow row)
    {
        Title = "Eintrag stornieren";
        Width = 460;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Storno (Soft-Delete)",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Der Eintrag bleibt in der JSON erhalten (Originalzeiten, Grund, Zeitpunkt). "
                   + "Er wird nicht mehr zur Arbeitszeit gezählt und in PDF/CSV als storniert markiert.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Opacity = 0.88,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Kommen"));
        root.Children.Add(MakeReadOnly(row.Kommen.Split('\n')[0]));
        root.Children.Add(MakeLabel("Gehen"));
        root.Children.Add(MakeReadOnly(row.Gehen.Split('\n')[0]));

        root.Children.Add(MakeLabel("Storno-Grund (Pflicht)"));
        _reasonBox = new TextBox
        {
            Background = InputBackground,
            Foreground = InputForeground,
            CaretBrush = InputForeground,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0),
            Text = "Test"
        };
        root.Children.Add(_reasonBox);

        _errorText = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        var ok = new Button
        {
            Content = "Stornieren",
            Padding = new Thickness(16, 6, 16, 6),
            IsDefault = true
        };
        ok.Click += (_, _) =>
        {
            var reason = _reasonBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                _errorText.Text = "Bitte einen Grund angeben (z. B. Test, Doppelbuchung).";
                _errorText.Visibility = Visibility.Visible;
                return;
            }

            VoidReason = reason;
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Opacity = 0.9,
            Margin = new Thickness(0, 10, 0, 0)
        };

    private static TextBlock MakeReadOnly(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0)
        };
}
