using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SmartOepnv.Fahrgeraet;

/// <summary>Ladekreis mit Prozent, wie der Fortschrittsdialog auf dem Tablet.</summary>
internal static class LadeAnzeige
{
    private static DispatcherTimer? _drehung;
    private static TextBlock? _prozent;
    private static TextBlock? _meldung;
    private static CancellationTokenSource? _abbruch;
    private static Control? _kopfRing;
    private static TextBlock? _kopfProzent;

    public static bool Offen => _abbruch is not null || _kopfRing?.IsVisible == true;

    public static void Verbinden(Control ring, TextBlock prozent)
    {
        _kopfRing = ring;
        _kopfProzent = prozent;
    }

    public static void KopfAus()
    {
        if (_kopfRing is not null)
        {
            _kopfRing.IsVisible = false;
        }

        if (_kopfProzent is not null)
        {
            _kopfProzent.IsVisible = false;
        }
    }

    public static CancellationToken Zeigen(Grid root, string titel, string meldung)
    {
        Schliessen(root);
        _abbruch = new CancellationTokenSource();
        var token = _abbruch.Token;
        var winkel = new RotateTransform();
        var ring = new Ellipse
        {
            Width = 56,
            Height = 56,
            Stroke = Brushes.White,
            StrokeThickness = 6,
            StrokeDashArray = new AvaloniaList<double> { 8, 4 },
            StrokeLineCap = PenLineCap.Round,
            RenderTransform = winkel,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        _prozent = Text("0%", 22);
        _prozent.MinWidth = 64;
        _prozent.VerticalAlignment = VerticalAlignment.Center;
        _meldung = Text(meldung, 16);
        var abbrechen = FahrbetriebPages.Touch("Update abbrechen");
        abbrechen.Background = new SolidColorBrush(Color.Parse("#FF0000"));
        abbrechen.Focusable = false;
        abbrechen.Click += (_, _) => _abbruch?.Cancel();
        var zeile = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { ring, _prozent }
        };
        var karte = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A237E")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            MinWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    Text(titel, 18),
                    zeile,
                    _meldung,
                    abbrechen
                }
            }
        };
        var flaeche = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Tag = "lade-anzeige",
            ZIndex = 50,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = karte
        };
        root.Children.Add(flaeche);
        _drehung = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _drehung.Tick += (_, _) => winkel.Angle = (winkel.Angle + 8) % 360;
        _drehung.Start();
        return token;
    }

    public static void Setzen(int prozent, string? meldung = null)
    {
        var wert = Math.Clamp(prozent, 0, 100);
        Dispatcher.UIThread.Post(() =>
        {
            if (_kopfRing is not null)
            {
                _kopfRing.IsVisible = true;
            }

            if (_kopfProzent is not null)
            {
                _kopfProzent.Text = wert + "%";
                _kopfProzent.IsVisible = true;
            }

            if (_prozent is not null)
            {
                _prozent.Text = wert + "%";
            }

            if (!string.IsNullOrWhiteSpace(meldung) && _meldung is not null)
            {
                _meldung.Text = meldung;
            }
        });
    }

    public static void Schliessen(Grid root)
    {
        _drehung?.Stop();
        _drehung = null;
        _prozent = null;
        _meldung = null;
        _abbruch?.Dispose();
        _abbruch = null;
        KopfAus();
        for (var i = root.Children.Count - 1; i >= 0; i--)
        {
            if (root.Children[i] is Control kind && Equals(kind.Tag, "lade-anzeige"))
            {
                root.Children.RemoveAt(i);
            }
        }
    }

    private static TextBlock Text(string text, double groesse) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = groesse,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
}
