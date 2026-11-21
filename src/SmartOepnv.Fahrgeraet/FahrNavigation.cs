using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Navigationsfeld der gewählten Route. Der Fahrweg kommt aus dem gespeicherten Routenpaket.
/// Ohne Standort bleibt die Ansage auf „Warte auf Standort“, die Linie ist trotzdem sichtbar.
/// </summary>
public static class FahrNavigation
{
    public static Control Create(FahrSession session, Action close)
    {
        var draft = Laden(session, out var hinweis);
        var shape = draft?.SnappedShape
            .Select(point => (point.Lat, point.Lon))
            .ToList() ?? [];
        var karten = new Canvas { Background = new SolidColorBrush(Color.Parse("#0A1628")) };
        var ansage = Banner("Warte auf Standort", 18, Brushes.White, VerticalAlignment.Top);
        var strecke = Banner("", 16, new SolidColorBrush(Color.Parse("#FFEB3B")), VerticalAlignment.Bottom);
        var symbole = new ScrollViewer
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#CC0A1628")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = SymbolListe(draft)
        };
        FingerZug.An(symbole);
        var karte = new Grid
        {
            Children = { karten, ansage, strecke, symbole }
        };
        karten.LayoutUpdated += (_, _) => Zeichnen(karten, shape, session);

        var zurueck = Knopf("Zurück", "#FF0000", Brushes.White);
        zurueck.Click += (_, _) => close();
        var symbolKnopf = Knopf("Symbole", "#FFEB3B", new SolidColorBrush(Color.Parse("#00008B")));
        symbolKnopf.Click += (_, _) => symbole.IsVisible = !symbole.IsVisible;
        var leiste = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(0, 6, 0, 0),
            Children = { zurueck, symbolKnopf }
        };
        Grid.SetColumn(symbolKnopf, 1);
        symbolKnopf.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetRow(leiste, 1);

        Fuellen(session, draft, shape, hinweis, ansage, strecke);
        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { karte, leiste }
        };
    }

    private static void Fuellen(
        FahrSession session,
        RoutePathDraft? draft,
        List<(double Lat, double Lon)> shape,
        string? hinweis,
        TextBlock ansage,
        TextBlock strecke)
    {
        if (hinweis is not null)
        {
            ansage.Text = hinweis;
            strecke.IsVisible = false;
            return;
        }

        if (!session.Trip.HasFix || shape.Count < 2)
        {
            ansage.Text = "Warte auf Standort";
            strecke.IsVisible = false;
            return;
        }

        var along = Projektion(shape, session.Trip.LastLat, session.Trip.LastLon);
        var abstand = AbstandZurLinie(shape, session.Trip.LastLat, session.Trip.LastLon);
        if (abstand > 50)
        {
            ansage.Text = "Linienweg verlassen";
            strecke.Text = $"{abstand:0} m vom Fahrweg";
            strecke.IsVisible = true;
            return;
        }

        var naechstes = draft!.SnappedManeuvers
            .Where(item => item.DistanceM > along - 25)
            .OrderBy(item => item.DistanceM)
            .FirstOrDefault();
        ansage.Text = naechstes is null
            ? "Weiter dem Fahrweg folgen"
            : Aktion(naechstes, along);
        var halt = NaechsterHalt(session, shape, along);
        strecke.Text = halt ?? "";
        strecke.IsVisible = halt is not null;
    }

    private static string Aktion(RoutePathSnapManeuver manover, double along)
    {
        var text = string.IsNullOrWhiteSpace(manover.Instruction) ? "Geradeaus" : manover.Instruction.Trim();
        var strasse = manover.NextStreet?.Trim();
        if (!string.IsNullOrWhiteSpace(strasse) &&
            !string.Equals(strasse, manover.CurrentStreet?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} auf {strasse}";
        }

        var rest = manover.DistanceM - along;
        return $"{Distanz(rest)} · {text}";
    }

    private static string Distanz(double meter)
    {
        var wert = Math.Max(0, meter);
        if (wert <= 0)
        {
            return "jetzt";
        }

        if (wert > 900)
        {
            return $"in {Math.Max(1, (int)Math.Ceiling(wert / 1000))} km";
        }

        var stufe = wert > 200 ? 100 : wert > 100 ? 50 : 25;
        var gerundet = Math.Max(0, (int)Math.Round(wert / stufe) * stufe);
        return gerundet <= 0 ? "jetzt" : $"in {gerundet} m";
    }

    private static string? NaechsterHalt(FahrSession session, List<(double Lat, double Lon)> shape, double along)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            return null;
        }

        foreach (var stop in session.Package.GetStops(session.RouteName))
        {
            if (stop.IsWaypoint || stop.Name.StartsWith("Hinweis:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!StopMatch.TryCoord(stop, out var lat, out var lon) &&
                !StopMatch.TryParse(stop.GpsCoordinates, out lat, out lon))
            {
                continue;
            }

            var bei = Projektion(shape, lat, lon);
            if (bei + 20 < along)
            {
                continue;
            }

            var text = Distanz(bei - along);
            return text == "jetzt" ? "Haltestelle jetzt" : $"Haltestelle {text}";
        }

        return null;
    }

    private static Control SymbolListe(RoutePathDraft? draft)
    {
        var liste = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        var zeilen = draft?.SnappedManeuvers
            .Where(item => !string.IsNullOrWhiteSpace(item.Instruction) || !string.IsNullOrWhiteSpace(item.NextStreet))
            .ToList() ?? [];
        if (zeilen.Count == 0)
        {
            liste.Children.Add(new TextBlock
            {
                Text = "Keine Manöver im Fahrweg.",
                Foreground = Brushes.White,
                FontSize = 16
            });
            return liste;
        }

        foreach (var zeile in zeilen)
        {
            var text = string.IsNullOrWhiteSpace(zeile.Instruction) ? "Geradeaus" : zeile.Instruction.Trim();
            if (!string.IsNullOrWhiteSpace(zeile.NextStreet))
            {
                text = $"{text} auf {zeile.NextStreet.Trim()}";
            }

            liste.Children.Add(new TextBlock
            {
                Text = $"{zeile.DistanceM:0} m · {text}",
                Foreground = Brushes.White,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return liste;
    }

    private static RoutePathDraft? Laden(FahrSession session, out string? hinweis)
    {
        hinweis = null;
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            hinweis = "Bitte zuerst eine Route wählen.";
            return null;
        }

        RoutePathDraft? draft = null;
        var json = RoutePathDraftRepository.TryGetDraftJson(session.Package.PackageRoot, session.RouteName);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                draft = RoutePathDraftSerializer.FromJson(json);
            }
            catch
            {
                hinweis = "Der gespeicherte Fahrweg lässt sich nicht lesen.";
                return null;
            }
        }

        if (draft is null || draft.SnappedShape.Count < 2)
        {
            var overview = LeitstelleRoutePathOverview.TryGetOverviewJson(session.Package.PackageRoot, session.RouteName);
            if (!string.IsNullOrWhiteSpace(overview))
            {
                try
                {
                    var ausUebersicht = RoutePathDraftSerializer.FromJson(overview);
                    if (ausUebersicht.SnappedShape.Count >= 2)
                    {
                        draft = ausUebersicht;
                    }
                }
                catch
                {
                    // Die Übersicht ist nur der Ersatz, wenn der Entwurf keine Linie hat.
                }
            }
        }

        if (draft is null || draft.SnappedShape.Count < 2)
        {
            hinweis = "Kein Fahrweg vorhanden. Bitte zuerst Fahrweg erstellen.";
            return null;
        }

        return draft;
    }

    private static void Zeichnen(Canvas flaeche, List<(double Lat, double Lon)> shape, FahrSession session)
    {
        var breite = flaeche.Bounds.Width;
        var hoehe = flaeche.Bounds.Height;
        if (breite < 20 || hoehe < 20 || shape.Count < 2)
        {
            return;
        }

        if (flaeche.Tag is (double altBreite, double altHoehe) &&
            Math.Abs(altBreite - breite) < 2 &&
            Math.Abs(altHoehe - hoehe) < 2 &&
            flaeche.Children.Count > 0)
        {
            return;
        }

        flaeche.Tag = (breite, hoehe);
        flaeche.Children.Clear();
        var minLat = shape.Min(point => point.Lat);
        var maxLat = shape.Max(point => point.Lat);
        var minLon = shape.Min(point => point.Lon);
        var maxLon = shape.Max(point => point.Lon);
        var latSpan = Math.Max(0.0001, maxLat - minLat);
        var lonSpan = Math.Max(0.0001, maxLon - minLon);
        Point Ort(double lat, double lon) => new(
            12 + (lon - minLon) / lonSpan * (breite - 24),
            hoehe - 12 - (lat - minLat) / latSpan * (hoehe - 24));

        var punkte = new Points();
        var schritt = Math.Max(1, shape.Count / 800);
        for (var i = 0; i < shape.Count; i += schritt)
        {
            punkte.Add(Ort(shape[i].Lat, shape[i].Lon));
        }

        var letzter = shape[^1];
        punkte.Add(Ort(letzter.Lat, letzter.Lon));
        flaeche.Children.Add(new Polyline
        {
            Stroke = Brushes.White,
            StrokeThickness = 3,
            Points = punkte
        });
        if (!session.Trip.HasFix)
        {
            return;
        }

        var hier = Ort(session.Trip.LastLat, session.Trip.LastLon);
        var punkt = new Ellipse { Width = 14, Height = 14, Fill = Brushes.OrangeRed };
        Canvas.SetLeft(punkt, hier.X - 7);
        Canvas.SetTop(punkt, hier.Y - 7);
        flaeche.Children.Add(punkt);
    }

    private static double Projektion(IReadOnlyList<(double Lat, double Lon)> points, double lat, double lon)
    {
        var best = 0.0;
        var bestDistance = double.MaxValue;
        var walked = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var span = StopMatch.Meters(a.Lat, a.Lon, b.Lat, b.Lon);
            var vx = b.Lon - a.Lon;
            var vy = b.Lat - a.Lat;
            var length = vx * vx + vy * vy;
            var t = length <= 0 ? 0 : Math.Clamp(((lon - a.Lon) * vx + (lat - a.Lat) * vy) / length, 0, 1);
            var distance = StopMatch.Meters(lat, lon, a.Lat + vy * t, a.Lon + vx * t);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = walked + span * t;
            }

            walked += span;
        }

        return best;
    }

    private static double AbstandZurLinie(IReadOnlyList<(double Lat, double Lon)> points, double lat, double lon)
    {
        var best = double.MaxValue;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var vx = b.Lon - a.Lon;
            var vy = b.Lat - a.Lat;
            var length = vx * vx + vy * vy;
            var t = length <= 0 ? 0 : Math.Clamp(((lon - a.Lon) * vx + (lat - a.Lat) * vy) / length, 0, 1);
            best = Math.Min(best, StopMatch.Meters(lat, lon, a.Lat + vy * t, a.Lon + vx * t));
        }

        return best;
    }

    private static TextBlock Banner(string text, double size, IBrush farbe, VerticalAlignment kante) => new()
    {
        Text = text,
        Foreground = farbe,
        FontSize = size,
        FontWeight = FontWeight.Bold,
        Background = new SolidColorBrush(Color.Parse("#CC0A1628")),
        Padding = new Thickness(8, 6),
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 64,
        VerticalAlignment = kante,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Button Knopf(string text, string hintergrund, IBrush schrift) => new()
    {
        Content = text,
        Height = 42,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = new SolidColorBrush(Color.Parse(hintergrund)),
        Foreground = schrift,
        FontSize = 16,
        FontWeight = FontWeight.Bold,
        CornerRadius = new CornerRadius(8)
    };
}
