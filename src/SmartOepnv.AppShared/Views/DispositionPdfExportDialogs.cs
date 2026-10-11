using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Pdf;

namespace SmartOepnv.AppShared.Views;

/// <summary>PDF-Export Personaldisposition: Zeitraum + ein Fahrer oder alle.</summary>
public sealed class FahrerdispoPdfExportDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush InputBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));

    private readonly DatePicker _fromPicker;
    private readonly DatePicker _toPicker;
    private readonly RadioButton _allDriversRadio;
    private readonly RadioButton _oneDriverRadio;
    private readonly ComboBox _driverBox;
    private readonly CheckBox _smartLogoCheck;
    private readonly TextBlock _errorText;

    public DateTime FromDate { get; private set; }

    public DateTime ToDate { get; private set; }

    /// <summary>null = alle Fahrer, sonst Dispo-Schlüssel.</summary>
    public string? SelectedDriverKey { get; private set; }

    public bool ShowSmartOepnvLogo { get; private set; } = true;

    public FahrerdispoPdfExportDialog(
        IReadOnlyList<(string Key, string DisplayName)> drivers,
        DateTime defaultFrom,
        DateTime defaultTo)
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "PDF Personaldisposition";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Zeitraum",
            Foreground = LabelForeground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var dateRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fromPanel = new StackPanel();
        fromPanel.Children.Add(new TextBlock
        {
            Text = "Von",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _fromPicker = CreateDatePicker(defaultFrom.Date);
        fromPanel.Children.Add(_fromPicker);
        Grid.SetColumn(fromPanel, 0);
        dateRow.Children.Add(fromPanel);

        var toPanel = new StackPanel();
        toPanel.Children.Add(new TextBlock
        {
            Text = "Bis",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _toPicker = CreateDatePicker(defaultTo.Date);
        toPanel.Children.Add(_toPicker);
        Grid.SetColumn(toPanel, 2);
        dateRow.Children.Add(toPanel);
        root.Children.Add(dateRow);

        root.Children.Add(new TextBlock
        {
            Text = "Ausgabe",
            Foreground = LabelForeground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _allDriversRadio = new RadioButton
        {
            Content = "Alle Fahrer",
            IsChecked = true,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 6),
            GroupName = "FahrerScope"
        };
        _oneDriverRadio = new RadioButton
        {
            Content = "Nur ein Fahrer",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 6),
            GroupName = "FahrerScope"
        };
        root.Children.Add(_allDriversRadio);
        root.Children.Add(_oneDriverRadio);

        _driverBox = new ComboBox
        {
            IsEnabled = false,
            Margin = new Thickness(0, 0, 0, 12),
            Background = InputBackground,
            Foreground = Brushes.White,
            DisplayMemberPath = "DisplayName",
            SelectedValuePath = "Key"
        };
        foreach (var d in drivers.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _driverBox.Items.Add(new DriverChoice(d.Key, d.DisplayName));
        }

        if (_driverBox.Items.Count > 0)
        {
            _driverBox.SelectedIndex = 0;
        }

        _oneDriverRadio.Checked += (_, _) => _driverBox.IsEnabled = true;
        _allDriversRadio.Checked += (_, _) => _driverBox.IsEnabled = false;
        root.Children.Add(_driverBox);

        _smartLogoCheck = new CheckBox
        {
            Content = "Smart-ÖPNV-Logo anzeigen",
            IsChecked = PlanerPdfBranding.IsSmartOepnvLogoEnabledInPdfs(),
            Foreground = LabelForeground,
            Margin = new Thickness(0, 4, 0, 12)
        };
        root.Children.Add(_smartLogoCheck);

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
        var cancel = new Button
        {
            Content = "Abbrechen",
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var ok = new Button
        {
            Content = "PDF erstellen",
            Width = 130,
            IsDefault = true
        };
        ok.Click += (_, _) => OnOk();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
    }

    private void OnOk()
    {
        if (_fromPicker.SelectedDate is not DateTime from ||
            _toPicker.SelectedDate is not DateTime to)
        {
            ShowError("Bitte Von- und Bis-Datum wählen.");
            return;
        }

        if (to.Date < from.Date)
        {
            ShowError("Bis-Datum darf nicht vor dem Von-Datum liegen.");
            return;
        }

        if (_oneDriverRadio.IsChecked == true)
        {
            if (_driverBox.SelectedItem is not DriverChoice choice)
            {
                ShowError("Bitte einen Fahrer wählen.");
                return;
            }

            SelectedDriverKey = choice.Key;
        }
        else
        {
            SelectedDriverKey = null;
        }

        FromDate = from.Date;
        ToDate = to.Date;
        ShowSmartOepnvLogo = _smartLogoCheck.IsChecked == true;
        PlanerPdfBranding.SetSmartOepnvLogoEnabledInPdfs(ShowSmartOepnvLogo);
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.Visibility = Visibility.Visible;
    }

    private static DatePicker CreateDatePicker(DateTime initial) =>
        new()
        {
            SelectedDate = initial,
            SelectedDateFormat = DatePickerFormat.Short,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("de-DE"),
            Background = InputBackground,
            Foreground = Brushes.White
        };

    private sealed record DriverChoice(string Key, string DisplayName);
}

/// <summary>PDF-Export Fahrzeugdisposition: Zeitraum, immer alle Fahrzeuge.</summary>
public sealed class FahrzeugdispoPdfExportDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush InputBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E));

    private readonly DatePicker _fromPicker;
    private readonly DatePicker _toPicker;
    private readonly CheckBox _smartLogoCheck;
    private readonly TextBlock _errorText;

    public DateTime FromDate { get; private set; }

    public DateTime ToDate { get; private set; }

    public bool ShowSmartOepnvLogo { get; private set; } = true;

    public FahrzeugdispoPdfExportDialog(DateTime defaultFrom, DateTime defaultTo)
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "PDF Fahrzeugdisposition";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Zeitraum (alle Fahrzeuge)",
            Foreground = LabelForeground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var dateRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fromPanel = new StackPanel();
        fromPanel.Children.Add(new TextBlock
        {
            Text = "Von",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _fromPicker = CreateDatePicker(defaultFrom.Date);
        fromPanel.Children.Add(_fromPicker);
        Grid.SetColumn(fromPanel, 0);
        dateRow.Children.Add(fromPanel);

        var toPanel = new StackPanel();
        toPanel.Children.Add(new TextBlock
        {
            Text = "Bis",
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _toPicker = CreateDatePicker(defaultTo.Date);
        toPanel.Children.Add(_toPicker);
        Grid.SetColumn(toPanel, 2);
        dateRow.Children.Add(toPanel);
        root.Children.Add(dateRow);

        _smartLogoCheck = new CheckBox
        {
            Content = "Smart-ÖPNV-Logo anzeigen",
            IsChecked = PlanerPdfBranding.IsSmartOepnvLogoEnabledInPdfs(),
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_smartLogoCheck);

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
        var cancel = new Button
        {
            Content = "Abbrechen",
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var ok = new Button
        {
            Content = "PDF erstellen",
            Width = 130,
            IsDefault = true
        };
        ok.Click += (_, _) => OnOk();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
    }

    private void OnOk()
    {
        if (_fromPicker.SelectedDate is not DateTime from ||
            _toPicker.SelectedDate is not DateTime to)
        {
            ShowError("Bitte Von- und Bis-Datum wählen.");
            return;
        }

        if (to.Date < from.Date)
        {
            ShowError("Bis-Datum darf nicht vor dem Von-Datum liegen.");
            return;
        }

        FromDate = from.Date;
        ToDate = to.Date;
        ShowSmartOepnvLogo = _smartLogoCheck.IsChecked == true;
        PlanerPdfBranding.SetSmartOepnvLogoEnabledInPdfs(ShowSmartOepnvLogo);
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.Visibility = Visibility.Visible;
    }

    private static DatePicker CreateDatePicker(DateTime initial) =>
        new()
        {
            SelectedDate = initial,
            SelectedDateFormat = DatePickerFormat.Short,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("de-DE"),
            Background = InputBackground,
            Foreground = Brushes.White
        };
}
