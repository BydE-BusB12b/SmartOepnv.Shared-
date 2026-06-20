using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class GpsMapPickerDialog : Window
{
    private static readonly SolidColorBrush WindowBackground = CreateBrush(0x0A, 0x10, 0x20);
    private static readonly SolidColorBrush PanelBackground = CreateBrush(0x12, 0x1A, 0x2E);
    private static readonly SolidColorBrush TextBrush = Brushes.White;
    private static readonly SolidColorBrush MutedTextBrush = CreateBrush(0xCC, 0xD6, 0xE8);
    private static readonly SolidColorBrush AccentBrush = CreateBrush(0x1E, 0x5A, 0x9E);

    private const double DefaultLat = 51.2277;
    private const double DefaultLon = 6.7735;

    private readonly WebView2 _mapView;
    private readonly TextBlock _hint;
    private readonly Button _okButton;
    private readonly double? _otherLat;
    private readonly double? _otherLon;
    private readonly string? _otherLabel;
    private bool _mapReady;
    private double? _pickedLat;
    private double? _pickedLon;

    public bool HasSelection => _pickedLat is not null && _pickedLon is not null;

    public string SelectedCoordinates =>
        HasSelection
            ? CoordinateFormatting.Format(_pickedLat!.Value, _pickedLon!.Value)
            : string.Empty;

    public GpsMapPickerDialog(
        string fieldTitle,
        string? initialCoordinates = null,
        string? otherCoordinates = null,
        string? otherLabel = null)
    {
        Title = fieldTitle;
        Width = 720;
        Height = 560;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackground;
        Foreground = TextBrush;

        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        if (TryParseCoordinates(otherCoordinates, out var otherLat, out var otherLon))
        {
            _otherLat = otherLat;
            _otherLon = otherLon;
            _otherLabel = string.IsNullOrWhiteSpace(otherLabel) ? "Referenz" : otherLabel.Trim();
        }

        var panel = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = PanelBackground
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = fieldTitle,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        _hint = new TextBlock
        {
            Text = BuildHintText(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_hint, 1);
        root.Children.Add(_hint);

        _mapView = new WebView2
        {
            MinHeight = 280,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 18, 26, 46)
        };
        Grid.SetRow(_mapView, 2);
        root.Children.Add(_mapView);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = CreateButton("Abbrechen", isPrimary: false);
        _okButton = CreateButton("Übernehmen", isPrimary: true);
        _okButton.IsDefault = true;
        _okButton.IsEnabled = false;
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        _okButton.Click += (_, _) =>
        {
            if (!HasSelection)
            {
                return;
            }

            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_okButton);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        panel.Child = root;
        Content = panel;

        if (TryParseCoordinates(initialCoordinates, out var lat, out var lon))
        {
            _pickedLat = lat;
            _pickedLon = lon;
            _okButton.IsEnabled = true;
        }

        Loaded += async (_, _) =>
        {
            try
            {
                await InitializeMapAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _hint.Text = $"Karte konnte nicht geladen werden: {ex.Message}";
            }
        };
    }

    private string BuildHintText()
    {
        if (_otherLat is not null && _otherLon is not null)
        {
            return $"Auf der Karte klicken, um den Standort zu setzen. Orange Pin: {_otherLabel}.";
        }

        return "Auf der Karte klicken, um den Standort zu setzen.";
    }

    private static Button CreateButton(string text, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            Background = isPrimary ? AccentBrush : CreateBrush(0x24, 0x32, 0x52)
        };
        if (isPrimary)
        {
            button.FontWeight = FontWeights.SemiBold;
        }

        return button;
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private async Task InitializeMapAsync()
    {
        if (_mapView.CoreWebView2 is not null)
        {
            return;
        }

        var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(userDataFolder, "GpsPickMap"))
            .ConfigureAwait(true);
        await _mapView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = _mapView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;

        var mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "gps_pick_map.html");
        _mapView.Source = new Uri(mapPath);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _hint.Text = "Karte konnte nicht angezeigt werden.";
            return;
        }

        _mapReady = true;
        SendInitMessage();
    }

    private void SendInitMessage()
    {
        if (!_mapReady || _mapView.CoreWebView2 is null)
        {
            return;
        }

        var hasSelection = HasSelection;
        var lat = hasSelection ? _pickedLat!.Value : DefaultLat;
        var lon = hasSelection ? _pickedLon!.Value : DefaultLon;
        var zoom = hasSelection ? 16 : 13;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "init",
            ["lat"] = lat,
            ["lon"] = lon,
            ["zoom"] = zoom,
            ["hasSelection"] = hasSelection
        };

        if (_otherLat is double otherLat && _otherLon is double otherLon)
        {
            payload["otherLat"] = otherLat;
            payload["otherLon"] = otherLon;
            payload["otherLabel"] = _otherLabel ?? "Referenz";
        }

        _mapView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnWebMessageReceived(sender, e));
            return;
        }

        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
            {
                json = e.WebMessageAsJson;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var type = typeProp.GetString();
            if (type == "ready")
            {
                SendInitMessage();
                return;
            }

            if (type != "picked" ||
                !root.TryGetProperty("lat", out var latEl) ||
                !root.TryGetProperty("lon", out var lonEl))
            {
                return;
            }

            var lat = latEl.GetDouble();
            var lon = lonEl.GetDouble();
            if (!double.IsFinite(lat) || !double.IsFinite(lon))
            {
                return;
            }

            _pickedLat = lat;
            _pickedLon = lon;
            _okButton.IsEnabled = true;
            UpdateHint();
        }
        catch
        {
            // ignore malformed messages from the map page
        }
    }

    private void UpdateHint()
    {
        if (!HasSelection)
        {
            _hint.Text = BuildHintText();
            return;
        }

        _hint.Text = $"Gewählt: {CoordinateFormatting.Format(_pickedLat!.Value, _pickedLon!.Value)}";
    }

    private static bool TryParseCoordinates(string? raw, out double lat, out double lon)
    {
        lat = lon = double.NaN;
        if (!CoordinateFormatting.TryParsePair(raw, out var latStr, out var lonStr))
        {
            return false;
        }

        return double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out lon) &&
               double.IsFinite(lat) && double.IsFinite(lon);
    }
}
