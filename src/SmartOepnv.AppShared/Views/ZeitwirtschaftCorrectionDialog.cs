using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.Views;

public sealed class ZeitwirtschaftCorrectionDialog : Window
{
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;

    private readonly TextBox _correctedStartBox;
    private readonly TextBox _correctedEndBox;
    private readonly TextBlock _errorText;

    public long CorrectedStartMs { get; private set; }
    public long? CorrectedEndMs { get; private set; }

    public ZeitwirtschaftCorrectionDialog(
        ZeitwirtschaftTimeTableRow row,
        ZeitwirtschaftMergedEntry entry)
    {
        Title = "Zeitkorrektur";
        Width = 480;
        MinWidth = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Zeitkorrektur",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Die ursprüngliche Stempelzeit bleibt erhalten. Die Korrektur wird zusätzlich gespeichert.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Opacity = 0.88,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Original Kommen"));
        root.Children.Add(MakeReadOnly(row.Kommen.Split('\n')[0]));
        root.Children.Add(MakeLabel("Original Gehen"));
        root.Children.Add(MakeReadOnly(row.Gehen.Split('\n')[0]));

        var effectiveStart = ZeitwirtschaftMergeService.EffectiveStartMs(entry);
        var effectiveEnd = ZeitwirtschaftMergeService.EffectiveEndMs(entry);

        root.Children.Add(MakeLabel("Korrigiert Kommen"));
        _correctedStartBox = MakeInput(
            "dd.MM.yyyy HH:mm",
            ZeitwirtschaftMergeService.FormatStamp(effectiveStart, entry.CorrectedStartIso ?? entry.StartIso));
        root.Children.Add(_correctedStartBox);

        root.Children.Add(MakeLabel("Korrigiert Gehen (optional)"));
        _correctedEndBox = MakeInput(
            "leer lassen = unverändert/offen",
            effectiveEnd is > 0
                ? ZeitwirtschaftMergeService.FormatStamp(effectiveEnd.Value, entry.CorrectedEndIso ?? entry.EndIso)
                : string.Empty);
        root.Children.Add(_correctedEndBox);

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
            MinWidth = 100,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var save = new Button
        {
            Content = "Speichern",
            MinWidth = 100,
            MinHeight = 36,
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (!TryParseInput(out var error))
            {
                _errorText.Text = error;
                _errorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
    }

    private bool TryParseInput(out string error)
    {
        error = string.Empty;
        if (!TryParseGermanDateTime(_correctedStartBox.Text, out var start))
        {
            error = "Korrigiertes Kommen ist ungültig (Format: dd.MM.yyyy HH:mm).";
            return false;
        }

        CorrectedStartMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        CorrectedEndMs = null;

        var endText = _correctedEndBox.Text.Trim();
        if (endText.Length > 0)
        {
            if (!TryParseGermanDateTime(endText, out var end))
            {
                error = "Korrigiertes Gehen ist ungültig (Format: dd.MM.yyyy HH:mm).";
                return false;
            }

            var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
            if (endMs <= CorrectedStartMs)
            {
                error = "Gehen muss nach Kommen liegen.";
                return false;
            }

            CorrectedEndMs = endMs;
        }

        return true;
    }

    private static bool TryParseGermanDateTime(string text, out DateTime result)
    {
        result = default;
        var trimmed = text.Trim();
        return DateTime.TryParseExact(
            trimmed,
            "dd.MM.yyyy HH:mm",
            CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.AssumeLocal,
            out result);
    }

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 8, 0, 4)
        };

    private static TextBlock MakeReadOnly(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Opacity = 0.92,
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
        };

    private static TextBox MakeInput(string placeholder, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x66))
        };
        if (string.IsNullOrEmpty(initial) && !string.IsNullOrEmpty(placeholder))
        {
            box.Tag = placeholder;
        }

        return box;
    }
}
