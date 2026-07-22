using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.Geo;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public sealed class GpsMapPickerDialog : Window
{
    private static readonly SolidColorBrush WindowBackground = CreateBrush(0x0A, 0x10, 0x20);
    private static readonly SolidColorBrush PanelBackground = CreateBrush(0x12, 0x1A, 0x2E);
    private static readonly SolidColorBrush InputBackground = CreateBrush(0x1A, 0x24, 0x3A);
    private static readonly SolidColorBrush TextBrush = Brushes.White;
    private static readonly SolidColorBrush MutedTextBrush = CreateBrush(0xCC, 0xD6, 0xE8);
    private static readonly SolidColorBrush AccentBrush = CreateBrush(0x1E, 0x5A, 0x9E);

    private const double DefaultLat = 51.2277;
    private const double DefaultLon = 6.7735;

    private readonly WebView2 _mapView;
    private readonly TextBlock _hint;
    private readonly TextBox _addressBox;
    private readonly Button _searchButton;
    private readonly Button _okButton;
    private readonly double? _otherLat;
    private readonly double? _otherLon;
    private readonly string? _otherLabel;
    private readonly int _radiusMeters;
    private bool _mapReady;
    private double? _pickedLat;
    private double? _pickedLon;
    private double? _viewLat;
    private double? _viewLon;
    private double _viewZoom = 13;
    private CancellationTokenSource? _searchCts;

    public bool HasSelection => _pickedLat is not null && _pickedLon is not null;

    public string SelectedCoordinates =>
        HasSelection
            ? CoordinateFormatting.Format(_pickedLat!.Value, _pickedLon!.Value)
            : string.Empty;

    public GpsMapPickerDialog(
        string fieldTitle,
        string? initialCoordinates = null,
        string? otherCoordinates = null,
        string? otherLabel = null,
        int radiusMeters = 0)
    {
        Title = fieldTitle;
        Width = 720;
        Height = 600;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackground;
        Foreground = TextBrush;
        _radiusMeters = radiusMeters > 0 ? radiusMeters : 0;

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

        var addressRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        addressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _addressBox = new TextBox
        {
            Background = InputBackground,
            Foreground = TextBrush,
            BorderBrush = CreateBrush(0x33, 0x44, 0x66),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = TextBrush
        };
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_addressBox, "Adresse suchen (Straße, Ort …)");
        _addressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SearchAddressAsync();
            }
        };
        Grid.SetColumn(_addressBox, 0);
        addressRow.Children.Add(_addressBox);

        _searchButton = CreateButton("Suchen", isPrimary: true);
        _searchButton.Margin = new Thickness(8, 0, 0, 0);
        _searchButton.MinWidth = 96;
        _searchButton.Click += async (_, _) => await SearchAddressAsync().ConfigureAwait(true);
        Grid.SetColumn(_searchButton, 1);
        addressRow.Children.Add(_searchButton);

        Grid.SetRow(addressRow, 2);
        root.Children.Add(addressRow);

        _mapView = new WebView2
        {
            MinHeight = 280,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 18, 26, 46)
        };
        Grid.SetRow(_mapView, 3);
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
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        panel.Child = root;
        Content = panel;

        if (TryParseCoordinates(initialCoordinates, out var lat, out var lon))
        {
            _pickedLat = lat;
            _pickedLon = lon;
            _viewLat = lat;
            _viewLon = lon;
            _viewZoom = 16;
            _okButton.IsEnabled = true;
        }
        else
        {
            var remembered = TryLoadLastView();
            if (remembered is not null)
            {
                _viewLat = remembered.Lat;
                _viewLon = remembered.Lon;
                _viewZoom = remembered.Zoom;
            }
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

        Closed += (_, _) =>
        {
            PersistLastView();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        };
    }

    private string BuildHintText()
    {
        var radiusHint = _radiusMeters > 0
            ? $" Blauer Kreis = GPS-Radius {_radiusMeters} m (Auslösung in der App)."
            : string.Empty;
        if (_otherLat is not null && _otherLon is not null)
        {
            return $"Adresse suchen oder auf der Karte klicken. Orange Pin: {_otherLabel}.{radiusHint}";
        }

        return $"Adresse suchen oder auf der Karte klicken, um den Standort zu setzen.{radiusHint}";
    }

    private async Task SearchAddressAsync()
    {
        var query = _addressBox.Text.Trim();
        if (query.Length < 3)
        {
            _hint.Text = "Bitte mindestens 3 Zeichen für die Adresssuche eingeben.";
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        _searchButton.IsEnabled = false;
        _hint.Text = "Adresse wird gesucht …";
        try
        {
            var result = await NominatimForwardGeocoder.TrySearchAsync(query, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (result is null)
            {
                _hint.Text = "Keine Treffer für diese Adresse.";
                return;
            }

            ApplyPickedLocation(result.Latitude, result.Longitude, result.DisplayName);
            SendSetPinMessage(result.Latitude, result.Longitude);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _hint.Text = $"Adresssuche fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }
    }

    private void ApplyPickedLocation(double lat, double lon, string? addressLabel = null)
    {
        _pickedLat = lat;
        _pickedLon = lon;
        _viewLat = lat;
        _viewLon = lon;
        if (_viewZoom < 15)
        {
            _viewZoom = 16;
        }

        _okButton.IsEnabled = true;
        if (!string.IsNullOrWhiteSpace(addressLabel))
        {
            _hint.Text =
                $"{addressLabel.Trim()}\n{CoordinateFormatting.Format(lat, lon)}";
        }
        else
        {
            UpdateHint();
        }
    }

    private void SendSetPinMessage(double lat, double lon)
    {
        if (!_mapReady || _mapView.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "setPin",
            lat,
            lon,
            zoom = 17,
            radiusMeters = _radiusMeters > 0 ? _radiusMeters : (int?)null
        });
        _mapView.CoreWebView2.PostWebMessageAsJson(payload);
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
        var lat = hasSelection
            ? _pickedLat!.Value
            : _viewLat ?? DefaultLat;
        var lon = hasSelection
            ? _pickedLon!.Value
            : _viewLon ?? DefaultLon;
        var zoom = hasSelection ? 16 : _viewZoom;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "init",
            ["lat"] = lat,
            ["lon"] = lon,
            ["zoom"] = zoom,
            ["hasSelection"] = hasSelection
        };

        if (_radiusMeters > 0)
        {
            payload["radiusMeters"] = _radiusMeters;
        }

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

            if (type == "viewChanged" &&
                root.TryGetProperty("lat", out var viewLatEl) &&
                root.TryGetProperty("lon", out var viewLonEl))
            {
                var viewLat = viewLatEl.GetDouble();
                var viewLon = viewLonEl.GetDouble();
                if (!double.IsFinite(viewLat) || !double.IsFinite(viewLon))
                {
                    return;
                }

                _viewLat = viewLat;
                _viewLon = viewLon;
                if (root.TryGetProperty("zoom", out var zoomEl) &&
                    zoomEl.TryGetDouble(out var zoom) &&
                    zoom is > 0 and <= 22)
                {
                    _viewZoom = zoom;
                }

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

            ApplyPickedLocation(lat, lon);
        }
        catch
        {
            // ignore malformed messages from the map page
        }
    }

    private static GpsMapPickerView? TryLoadLastView()
    {
        try
        {
            return new GpsMapPickerViewStore(AppServices.SettingsSubfolder).Load();
        }
        catch
        {
            return null;
        }
    }

    private void PersistLastView()
    {
        if (_viewLat is not double lat || _viewLon is not double lon)
        {
            return;
        }

        try
        {
            new GpsMapPickerViewStore(AppServices.SettingsSubfolder).Save(new GpsMapPickerView
            {
                Lat = lat,
                Lon = lon,
                Zoom = _viewZoom
            });
        }
        catch
        {
            // ignore persistence errors
        }
    }

    private void UpdateHint()
    {
        if (!HasSelection)
        {
            _hint.Text = BuildHintText();
            return;
        }

        _hint.Text = $"Gewählt: {CoordinateFormatting.Format(_pickedLat!.Value, _pickedLon!.Value)}" +
                     (_radiusMeters > 0 ? $" · Radius {_radiusMeters} m" : string.Empty);
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
