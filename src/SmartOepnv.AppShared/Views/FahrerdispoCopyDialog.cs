using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

/// <summary>Tage/Wochen in der Fahrerdisposition kopieren (Quelle → Ziel).</summary>
public sealed class FahrerdispoCopyDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush InputBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));

    private readonly DatePicker _sourceFromPicker;
    private readonly DatePicker _sourceToPicker;
    private readonly DatePicker _targetFromPicker;
    private readonly RadioButton _allDriversRadio;
    private readonly RadioButton _oneDriverRadio;
    private readonly ComboBox _driverBox;
    private readonly TextBlock _errorText;
    private readonly DateTime _weekStart;

    public DateTime SourceFrom { get; private set; }

    public DateTime SourceTo { get; private set; }

    public DateTime TargetFrom { get; private set; }

    /// <summary>null = alle Fahrer, sonst Dispo-Schlüssel.</summary>
    public string? SelectedDriverKey { get; private set; }

    public FahrerdispoCopyDialog(
        IReadOnlyList<(string Key, string DisplayName)> drivers,
        DateTime visibleWeekStart)
    {
        _weekStart = visibleWeekStart.Date;

        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "Tage / Woche kopieren";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var lastWeekStart = _weekStart.AddDays(-7);
        var lastWeekFri = lastWeekStart.AddDays(4);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Tage / Woche kopieren",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Dienste aus dem Quellzeitraum auf denselben Wochentag im Zielzeitraum legen.",
            Foreground = LabelForeground,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        presets.Children.Add(MakePresetButton(
            "Mo–Fr wie letzte Woche",
            () => ApplyRange(lastWeekStart, lastWeekFri, _weekStart)));
        presets.Children.Add(MakePresetButton(
            "Ganze KW → aktuelle KW",
            () => ApplyRange(lastWeekStart, lastWeekStart.AddDays(6), _weekStart)));
        presets.Children.Add(MakePresetButton(
            "Sichtbare Woche → nächste",
            () => ApplyRange(_weekStart, _weekStart.AddDays(6), _weekStart.AddDays(7))));
        root.Children.Add(presets);

        root.Children.Add(MakeSectionLabel("Quelle"));
        var sourceRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sourceFromPanel = new StackPanel();
        sourceFromPanel.Children.Add(MakeFieldLabel("Von"));
        _sourceFromPicker = CreateDatePicker(lastWeekStart);
        sourceFromPanel.Children.Add(_sourceFromPicker);
        Grid.SetColumn(sourceFromPanel, 0);
        sourceRow.Children.Add(sourceFromPanel);

        var sourceToPanel = new StackPanel();
        sourceToPanel.Children.Add(MakeFieldLabel("Bis"));
        _sourceToPicker = CreateDatePicker(lastWeekFri);
        sourceToPanel.Children.Add(_sourceToPicker);
        Grid.SetColumn(sourceToPanel, 2);
        sourceRow.Children.Add(sourceToPanel);
        root.Children.Add(sourceRow);

        root.Children.Add(MakeSectionLabel("Ziel"));
        var targetPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        targetPanel.Children.Add(MakeFieldLabel("Erster Tag (gleiche Länge wie Quelle)"));
        _targetFromPicker = CreateDatePicker(_weekStart);
        targetPanel.Children.Add(_targetFromPicker);
        root.Children.Add(targetPanel);

        root.Children.Add(MakeSectionLabel("Fahrer"));
        _allDriversRadio = new RadioButton
        {
            Content = "Alle Fahrer",
            IsChecked = true,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _oneDriverRadio = new RadioButton
        {
            Content = "Ein Fahrer",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(_allDriversRadio);
        root.Children.Add(_oneDriverRadio);

        _driverBox = new ComboBox
        {
            ItemsSource = drivers
                .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new DriverOption(d.Key, d.DisplayName))
                .ToList(),
            DisplayMemberPath = nameof(DriverOption.Label),
            SelectedValuePath = nameof(DriverOption.Key),
            MinHeight = 34,
            IsEnabled = false,
            Background = InputBackground,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        if (_driverBox.Items.Count > 0)
        {
            _driverBox.SelectedIndex = 0;
        }

        _allDriversRadio.Checked += (_, _) => _driverBox.IsEnabled = false;
        _oneDriverRadio.Checked += (_, _) => _driverBox.IsEnabled = true;
        root.Children.Add(_driverBox);

        _errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Abbrechen", Width = 110, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = new Button { Content = "Kopieren", Width = 110, IsDefault = true };
        ok.Click += (_, _) => OnOk();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
    }

    private void ApplyRange(DateTime sourceFrom, DateTime sourceTo, DateTime targetFrom)
    {
        _sourceFromPicker.SelectedDate = sourceFrom.Date;
        _sourceToPicker.SelectedDate = sourceTo.Date;
        _targetFromPicker.SelectedDate = targetFrom.Date;
        _errorText.Visibility = Visibility.Collapsed;
    }

    private void OnOk()
    {
        if (_sourceFromPicker.SelectedDate is not DateTime sourceFrom ||
            _sourceToPicker.SelectedDate is not DateTime sourceTo ||
            _targetFromPicker.SelectedDate is not DateTime targetFrom)
        {
            ShowError("Bitte alle Daten wählen.");
            return;
        }

        if (sourceTo.Date < sourceFrom.Date)
        {
            ShowError("Quell-Bis darf nicht vor Quell-Von liegen.");
            return;
        }

        if (_oneDriverRadio.IsChecked == true)
        {
            if (_driverBox.SelectedValue is not string key || string.IsNullOrWhiteSpace(key))
            {
                ShowError("Bitte einen Fahrer wählen.");
                return;
            }

            SelectedDriverKey = key;
        }
        else
        {
            SelectedDriverKey = null;
        }

        SourceFrom = sourceFrom.Date;
        SourceTo = sourceTo.Date;
        TargetFrom = targetFrom.Date;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.Visibility = Visibility.Visible;
    }

    private Button MakePresetButton(string label, Action apply)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(10, 4, 10, 4)
        };
        button.Click += (_, _) => apply();
        return button;
    }

    private static TextBlock MakeSectionLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };

    private static TextBlock MakeFieldLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static DatePicker CreateDatePicker(DateTime initial) =>
        new()
        {
            SelectedDate = initial,
            SelectedDateFormat = DatePickerFormat.Short,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("de-DE"),
            Background = InputBackground,
            Foreground = Brushes.White
        };

    private sealed record DriverOption(string Key, string Label);
}
