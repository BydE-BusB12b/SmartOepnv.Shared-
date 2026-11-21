using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public sealed class ZeitwirtschaftManualEntryDialog : Window
{
    public readonly record struct VehicleChoice(string Phone, string Label);

    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static Brush LabelForeground => UiTheme.ForegroundBrush;

    private readonly ComboBox _vehicleBox;
    private readonly TextBox _startBox;
    private readonly TextBox _endBox;
    private readonly TextBox _pauseStartBox;
    private readonly TextBox _pauseEndBox;
    private readonly TextBox _reasonBox;
    private readonly TextBlock _errorText;

    public string VehiclePhone { get; private set; } = string.Empty;
    public string VehicleLabel { get; private set; } = string.Empty;
    public long StartMs { get; private set; }
    public long EndMs { get; private set; }
    public long? PauseStartMs { get; private set; }
    public long? PauseEndMs { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    public ZeitwirtschaftManualEntryDialog(string employeeLabel, IReadOnlyList<VehicleChoice> vehicles)
    {
        Title = "Zeit nachtragen";
        Width = 520;
        MinWidth = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = UiTheme.BackgroundBrush;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Zeit nachtragen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Für eine vergessene Stempelung. Der Eintrag bleibt als Nachtrag mit Grund erkennbar und ersetzt keine vorhandene Stempelzeit.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Opacity = 0.88,
            Margin = new Thickness(0, 0, 0, 12)
        });
        root.Children.Add(MakeLabel("Fahrer"));
        root.Children.Add(MakeReadOnly(employeeLabel));

        root.Children.Add(MakeLabel("Fahrzeug"));
        _vehicleBox = new ComboBox
        {
            ItemsSource = vehicles,
            DisplayMemberPath = nameof(VehicleChoice.Label),
            MinHeight = 32,
            Background = InputBackground,
            Foreground = InputForeground
        };
        if (vehicles.Count > 0)
        {
            _vehicleBox.SelectedIndex = 0;
        }

        root.Children.Add(_vehicleBox);

        root.Children.Add(MakeLabel("Kommen"));
        _startBox = MakeInput();
        root.Children.Add(_startBox);
        root.Children.Add(MakeLabel("Gehen"));
        _endBox = MakeInput();
        root.Children.Add(_endBox);
        root.Children.Add(MakeLabel("Pause Beginn (optional)"));
        _pauseStartBox = MakeInput();
        root.Children.Add(_pauseStartBox);
        root.Children.Add(MakeLabel("Pause Ende (optional)"));
        _pauseEndBox = MakeInput();
        root.Children.Add(_pauseEndBox);
        root.Children.Add(MakeLabel("Grund"));
        _reasonBox = MakeInput();
        _reasonBox.Text = "Stempelung vergessen";
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
            if (!TryRead(out var error))
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

    private bool TryRead(out string error)
    {
        error = string.Empty;
        if (_vehicleBox.SelectedItem is not VehicleChoice vehicle || vehicle.Phone.Length == 0)
        {
            error = "Bitte ein Fahrzeug wählen.";
            return false;
        }

        if (!TryParse(_startBox.Text, out var start))
        {
            error = "Kommen ist ungültig (Format: dd.MM.yyyy HH:mm).";
            return false;
        }

        if (!TryParse(_endBox.Text, out var end))
        {
            error = "Gehen ist ungültig (Format: dd.MM.yyyy HH:mm).";
            return false;
        }

        var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        if (endMs <= startMs)
        {
            error = "Gehen muss nach Kommen liegen.";
            return false;
        }

        long? pauseStart = null;
        long? pauseEnd = null;
        var pauseStartText = _pauseStartBox.Text.Trim();
        var pauseEndText = _pauseEndBox.Text.Trim();
        if (pauseStartText.Length > 0 || pauseEndText.Length > 0)
        {
            if (!TryParse(pauseStartText, out var pauseFrom) || !TryParse(pauseEndText, out var pauseTo))
            {
                error = "Pause ist ungültig (Format: dd.MM.yyyy HH:mm).";
                return false;
            }

            pauseStart = new DateTimeOffset(pauseFrom).ToUnixTimeMilliseconds();
            pauseEnd = new DateTimeOffset(pauseTo).ToUnixTimeMilliseconds();
            if (pauseEnd <= pauseStart || pauseStart < startMs || pauseEnd > endMs)
            {
                error = "Die Pause muss zwischen Kommen und Gehen liegen.";
                return false;
            }
        }

        var reason = _reasonBox.Text.Trim();
        if (reason.Length < 3)
        {
            error = "Bitte den Grund angeben, zum Beispiel Stempelung vergessen.";
            return false;
        }

        VehiclePhone = vehicle.Phone;
        VehicleLabel = vehicle.Label;
        StartMs = startMs;
        EndMs = endMs;
        PauseStartMs = pauseStart;
        PauseEndMs = pauseEnd;
        Reason = reason;
        return true;
    }

    private static bool TryParse(string text, out DateTime result) =>
        DateTime.TryParseExact(
            text.Trim(),
            "dd.MM.yyyy HH:mm",
            CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.AssumeLocal,
            out result);

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

    private static TextBox MakeInput() =>
        new()
        {
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x66))
        };
}
