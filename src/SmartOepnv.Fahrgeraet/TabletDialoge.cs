using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Die vier Tablet-Dialoge: Personalnummer, Linie/Kurs, Anzeigenliste.
/// </summary>
internal static class TabletDialoge
{
    public static Control Anmeldung(FahrSession session, Action onLoggedIn)
    {
        return Personalnummer(session, onLoggedIn);
    }

    public static Control LinieKurs(FahrSession session, Action back)
    {
        var entered = "";
        var anzeige = Anzeige("_ _ _ / _ _");
        var hinweis = FahrbetriebPages.Info("");
        var seite = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4) };
        seite.Children.Add(Titel("Linie/Kurs eingeben"));
        seite.Children.Add(anzeige);
        seite.Children.Add(Ziffern(ziffer =>
        {
            if (entered.Length < 5)
            {
                entered += ziffer;
                anzeige.Text = FormatiereLinie(entered);
            }
        }));
        var leiste = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 8, 0, 0) };
        var loeschen = Farbknopf("Löschen", "#FF0000");
        loeschen.Margin = new Thickness(0, 0, 4, 0);
        loeschen.Click += (_, _) =>
        {
            entered = "";
            anzeige.Text = FormatiereLinie("");
            hinweis.Text = "";
        };
        var abbrechen = Farbknopf("Abbrechen", "#2196F3");
        abbrechen.Margin = new Thickness(2, 0);
        Grid.SetColumn(abbrechen, 1);
        abbrechen.Click += (_, _) => back();
        var ok = Farbknopf("OK", "#4CAF50");
        ok.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ok, 2);
        ok.Click += (_, _) =>
        {
            if (entered.Length is not (3 or 4 or 5))
            {
                hinweis.Text = "Ungültige Eingabe. Bitte 3, 4 oder 5 Ziffern eingeben.";
                return;
            }

            var linie = NormalisiereLinie(entered);
            if (linie == "000/00")
            {
                VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l000"), 9600);
                VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("lE00"), 9600);
                hinweis.Text = "Linie/Kurs 000/00: Anzeigen gelöscht.";
                return;
            }

            var treffer = PassendeRouten(session, linie);
            if (treffer.Count == 0)
            {
                var andereTage = session.Package?.RouteNames.Any(name => RoutenListe.GleicheLinie(name, linie)) == true;
                hinweis.Text = andereTage
                    ? "Keine Route verfügbar"
                    : "Keine Route für " + linie + ".";
                return;
            }

            if (Finde(seite) is ContentControl host)
            {
                host.Content = Routen(session, linie, treffer, back);
            }
        };
        leiste.Children.Add(loeschen);
        leiste.Children.Add(abbrechen);
        leiste.Children.Add(ok);
        seite.Children.Add(leiste);
        seite.Children.Add(hinweis);
        return Fest(seite);
    }

    public static Control Anzeigenliste(FahrSession session, Action back)
    {
        var eintraege = (session.Package?.OutsideDisplays ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        var seite = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        seite.Children.Add(Titel("Anzeigenliste"));
        var zielnummer = Farbknopf("Zielnummer", "#2196F3");
        var suche = FahrbetriebPages.Field("Suchen…");
        var liste = new StackPanel { Spacing = 6 };
        var leer = FahrbetriebPages.Info(eintraege.Count == 0
            ? "Keine Außenanzeigen gespeichert.\n\nSpeichern Sie Außenanzeigen über:\nMenü → Anzeigen → Außenanzeige"
            : "");
        leer.IsVisible = eintraege.Count == 0;
        suche.IsVisible = eintraege.Count > 0;

        void Fuellen()
        {
            liste.Children.Clear();
            var suchtext = suche.Text?.Trim() ?? "";
            foreach (var eintrag in eintraege)
            {
                var name = eintrag.Split('|')[0].Trim();
                if (suchtext.Length > 0 && name.Contains(suchtext, StringComparison.OrdinalIgnoreCase) == false
                    && eintrag.Contains(suchtext, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                var nummer = ZielnummerVon(eintrag);
                var text = string.IsNullOrWhiteSpace(nummer) ? name : name + " (" + nummer + ")";
                var knopf = Farbknopf(text, string.Equals(name, "Startziel", StringComparison.OrdinalIgnoreCase) ? "#FF0000" : "#1A237E");
                var ziel = name;
                knopf.Click += async (_, _) =>
                {
                    try
                    {
                        await DisplayTelegrams.SendAsync(session, "", ziel, "");
                    }
                    catch
                    {
                        // Die Liste bleibt offen, wenn die Anzeige nicht erreichbar ist.
                        return;
                    }

                    back();
                };
                liste.Children.Add(knopf);
            }
        }

        suche.TextChanged += (_, _) => Fuellen();
        Fuellen();
        zielnummer.Click += (_, _) =>
        {
            if (Finde(seite) is not ContentControl host)
            {
                return;
            }

            var liste = host.Content;
            host.Content = Zielnummer(session, eintraege, () => host.Content = liste, back);
        };

        var unten = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 8, 0, 0) };
        var loeschen = Farbknopf("Außen Löschen", "#2196F3");
        loeschen.Margin = new Thickness(0, 0, 4, 0);
        loeschen.Click += (_, _) =>
        {
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l000"), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("lE00"), 9600);
            back();
        };
        var schliessen = Farbknopf("Schließen", "#FF0000");
        schliessen.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(schliessen, 1);
        schliessen.Click += (_, _) => back();
        unten.Children.Add(loeschen);
        unten.Children.Add(schliessen);
        seite.Children.Add(zielnummer);
        seite.Children.Add(suche);
        seite.Children.Add(leer);
        seite.Children.Add(liste);
        seite.Children.Add(unten);
        return Blatt(seite);
    }

    private static Control Personalnummer(FahrSession session, Action onLoggedIn)
    {
        var entered = "";
        var anzeige = Anzeige("_ _ _ _");
        var hinweis = FahrbetriebPages.Info("");
        var seite = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4) };
        seite.Children.Add(Titel("Personalnummer"));
        seite.Children.Add(new TextBlock
        {
            Text = "Geben Sie Ihre 4-stellige Personalnummer ein",
            Foreground = Brushes.White,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        seite.Children.Add(anzeige);
        seite.Children.Add(Ziffern(ziffer =>
        {
            if (entered.Length < 4)
            {
                entered += ziffer;
                anzeige.Text = Plaetze(entered, 4, false);
            }
        }));
        var leiste = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var loeschen = Farbknopf("Löschen", "#FF0000");
        loeschen.Margin = new Thickness(0, 0, 4, 0);
        loeschen.Click += (_, _) =>
        {
            entered = "";
            anzeige.Text = Plaetze("", 4, false);
            hinweis.Text = "";
        };
        var ok = Farbknopf("OK", "#4CAF50");
        ok.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ok, 1);
        ok.Click += (_, _) =>
        {
            if (entered.Length != 4)
            {
                hinweis.Text = "Bitte 4 Ziffern eingeben";
                return;
            }

            var person = session.Package?.Employees.FirstOrDefault(e =>
                FahrbetriebPages.Digits(e.PersonnelNumber) == entered
                || FahrbetriebPages.Digits(e.PersonnelNumber).EndsWith(entered, StringComparison.Ordinal));
            if (person is null)
            {
                hinweis.Text = "Unbekannte Personalnummer";
                entered = "";
                anzeige.Text = Plaetze("", 4, false);
                return;
            }

            if (Finde(seite) is not ContentControl host)
            {
                return;
            }

            var formular = host.Content;
            host.Content = Passwort(session, person, onLoggedIn, () => host.Content = formular);
        };
        leiste.Children.Add(loeschen);
        leiste.Children.Add(ok);
        seite.Children.Add(leiste);
        seite.Children.Add(hinweis);
        return Fest(seite);
    }

    private static Control Passwort(FahrSession session, EmployeeRosterItem person, Action onLoggedIn, Action anderer)
    {
        var erwartet = person.Password;
        var ziffern = erwartet.Length > 0 && erwartet.All(char.IsDigit);
        var entered = "";
        var anzeige = Anzeige(ziffern ? Plaetze("", Math.Clamp(erwartet.Length, 1, 12), true) : "");
        var frei = ziffern
            ? null
            : new TextBox
            {
                PasswordChar = '•',
                FontSize = 22,
                Watermark = "Passwort",
                MinHeight = 48,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled
            };
        var hinweis = FahrbetriebPages.Info("Geben Sie Ihr Passwort ein" + (ziffern ? " (nur Ziffern)" : ""));
        var seite = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4) };
        seite.Children.Add(Titel("Anmeldung"));
        seite.Children.Add(new TextBlock
        {
            Text = person.Name,
            Foreground = Brushes.White,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        if (frei is null)
        {
            seite.Children.Add(anzeige);
            seite.Children.Add(Ziffern(ziffer =>
            {
                if (entered.Length < erwartet.Length)
                {
                    entered += ziffer;
                    anzeige.Text = Plaetze(entered, erwartet.Length, true);
                }
            }));
        }
        else
        {
            seite.Children.Add(frei);
        }

        var leiste = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var loeschen = Farbknopf("Löschen", "#FF0000");
        loeschen.Click += (_, _) =>
        {
            entered = "";
            if (frei is not null)
            {
                frei.Text = "";
            }
            else
            {
                anzeige.Text = Plaetze("", erwartet.Length, true);
            }

            hinweis.Text = "";
        };
        var andere = Farbknopf("Anderer Nutzer", "#2196F3");
        Grid.SetColumn(andere, 1);
        andere.Margin = new Thickness(4, 0);
        andere.Click += (_, _) => anderer();
        var ok = Farbknopf("OK", "#4CAF50");
        Grid.SetColumn(ok, 2);
        ok.Margin = new Thickness(4, 0, 0, 0);
        ok.Click += (_, _) =>
        {
            var wert = frei is null ? entered : frei.Text ?? "";
            if (wert != erwartet)
            {
                hinweis.Text = "Falsches Passwort";
                entered = "";
                if (frei is not null)
                {
                    frei.Text = "";
                }
                else
                {
                    anzeige.Text = Plaetze("", erwartet.Length, true);
                }

                return;
            }

            session.Employee = person;
            session.SaveDevice();
            _ = session.Funk.ConnectAsync(session.DevicePhone);
            StandortSend.Start(session);
            onLoggedIn();
        };
        leiste.Children.Add(loeschen);
        leiste.Children.Add(andere);
        leiste.Children.Add(ok);
        seite.Children.Add(leiste);
        seite.Children.Add(hinweis);
        return Fest(seite);
    }

    private static Control Zielnummer(FahrSession session, List<string> eintraege, Action zurueck, Action fertig)
    {
        var entered = "";
        var anzeige = Anzeige("_ _ _ _");
        var hinweis = FahrbetriebPages.Info("");
        var seite = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4) };
        seite.Children.Add(Titel("Zielnummer"));
        seite.Children.Add(anzeige);
        seite.Children.Add(Ziffern(ziffer =>
        {
            if (entered.Length < 4)
            {
                entered += ziffer;
                anzeige.Text = Plaetze(entered, 4, false);
            }
        }));
        var leiste = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var abbrechen = Farbknopf("Abbrechen", "#FF0000");
        abbrechen.Margin = new Thickness(0, 0, 4, 0);
        abbrechen.Click += (_, _) => zurueck();
        var ok = Farbknopf("Übernehmen", "#4CAF50");
        ok.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ok, 1);
        ok.Click += async (_, _) =>
        {
            if (entered.Length != 4)
            {
                hinweis.Text = "Bitte 4 Ziffern eingeben";
                return;
            }

            var treffer = eintraege.FirstOrDefault(e =>
                string.Equals(ZielnummerVon(e), entered, StringComparison.Ordinal)
                || e.Split('|')[0].Contains(entered, StringComparison.Ordinal));
            if (treffer is null)
            {
                hinweis.Text = "Zielnummer nicht gefunden";
                return;
            }

            try
            {
                await DisplayTelegrams.SendAsync(session, "", treffer.Split('|')[0].Trim(), "");
            }
            catch (Exception ex)
            {
                hinweis.Text = ex.Message;
                return;
            }

            fertig();
        };
        leiste.Children.Add(abbrechen);
        leiste.Children.Add(ok);
        seite.Children.Add(leiste);
        seite.Children.Add(hinweis);
        return Fest(seite);
    }

    private static Control Routen(FahrSession session, string linie, List<string> treffer, Action back)
    {
        var liste = new StackPanel { Spacing = 6 };
        var naechste = RoutenListe.Naechste(session, treffer);
        Control? markiert = null;
        for (var i = 0; i < treffer.Count; i++)
        {
            var route = treffer[i];
            var knopf = Farbknopf(RoutenListe.Beschriftung(route) + "  " + RoutenListe.Uhr(session, route), "#00008B");
            if (i == naechste)
            {
                knopf.BorderBrush = new SolidColorBrush(Color.Parse("#FFA500"));
                knopf.BorderThickness = new Thickness(3);
                knopf.CornerRadius = new CornerRadius(10);
                markiert = knopf;
            }

            knopf.Click += (_, _) =>
            {
                session.RouteName = route;
                session.Trip.Reset();
                back();
            };
            liste.Children.Add(knopf);
        }

        var scroll = FahrTabletMenue.MenuScroll(liste);
        if (markiert is not null)
        {
            var ziel = markiert;
            EventHandler? einmal = null;
            einmal = (_, _) =>
            {
                scroll.LayoutUpdated -= einmal;
                InDieDritteZeile(scroll, ziel);
            };
            scroll.AttachedToVisualTree += (_, _) => scroll.LayoutUpdated += einmal;
        }

        var zurueck = Farbknopf("Zurück", "#FF0000");
        zurueck.Margin = new Thickness(0, 8, 0, 0);
        zurueck.Click += (_, _) => back();
        var seite = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 4),
            Children = { Titel(linie), scroll, zurueck }
        };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(zurueck, 2);
        return Rand(seite);
    }

    /// <summary>Die markierte Fahrt steht in der dritten sichtbaren Zeile, wie auf dem Tablet.</summary>
    private static void InDieDritteZeile(ScrollViewer scroll, Control ziel)
    {
        if (scroll.Content is not Visual inhalt)
        {
            return;
        }

        var punkt = ziel.TranslatePoint(new Point(0, 0), inhalt);
        if (punkt is null)
        {
            return;
        }

        var zeile = ziel.Bounds.Height > 1 ? ziel.Bounds.Height + 6 : 54;
        scroll.Offset = new Vector(0, Math.Max(0, punkt.Value.Y - 2 * zeile));
    }

    private static ContentControl? Finde(Control von)
    {
        StyledElement? knoten = von;
        while (knoten is not null)
        {
            if (knoten is ContentControl host && knoten is not ScrollViewer)
            {
                return host;
            }

            knoten = knoten.Parent as StyledElement;
        }

        return null;
    }

    private static string ZielnummerVon(string eintrag)
    {
        var teile = eintrag.Split('|');
        if (teile.Length <= 13 || string.IsNullOrWhiteSpace(teile[13]))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(teile[13])).Trim();
        }
        catch
        {
            return teile[13].Trim();
        }
    }

    private static List<string> PassendeRouten(FahrSession session, string linie)
    {
        if (session.Package is null)
        {
            return [];
        }

        return session.Package.RouteNames
            .Where(name => RoutenListe.GleicheLinie(name, linie) && RoutenListe.Heute(session, name))
            .OrderBy(name => RoutenListe.Abfahrt(session, name))
            .ThenBy(name => RoutenListe.Fahrt(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatiereLinie(string digits)
    {
        return digits.Length switch
        {
            0 => "_ _ _ / _ _",
            1 => "_ _ _ / _ " + digits[0],
            2 => "_ _ _ / " + digits[0] + " " + digits[1],
            3 => "_ _ " + digits[0] + " / " + digits[1] + " " + digits[2],
            4 => "_ " + digits[0] + " " + digits[1] + " / " + digits[2] + " " + digits[3],
            _ => digits[0] + " " + digits[1] + " " + digits[2] + " / " + digits[3] + " " + digits[4]
        };
    }

    private static string NormalisiereLinie(string digits)
    {
        // Wie auf dem Tablet: die Ziffern stehen rechtsbündig in _ _ _ / _ _.
        // 201 wird _ _ 2 / 0 1 und damit 002/01. 2013 wird _ 2 0 / 1 3 und damit 020/13.
        var teile = FormatiereLinie(digits).Split('/');
        var links = NurZiffern(teile[0]).PadLeft(3, '0');
        var rechts = NurZiffern(teile.Length > 1 ? teile[1] : "").PadLeft(2, '0');
        return links[^3..] + "/" + rechts[^2..];
    }

    private static string NurZiffern(string text) => new(text.Where(char.IsDigit).ToArray());

    private static string Plaetze(string entered, int count, bool maskiert)
    {
        var laenge = Math.Clamp(count, 1, 12);
        return string.Join(" ", Enumerable.Range(0, laenge).Select(i =>
            i < entered.Length ? (maskiert ? "•" : entered[i].ToString()) : "_"));
    }

    private static TextBlock Anzeige(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 28,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        TextAlignment = TextAlignment.Center,
        Background = new SolidColorBrush(Color.Parse("#0D47A1")),
        Padding = new Thickness(8, 6)
    };

    public static Control AppBeenden(Action zurueck, Action schliessen)
    {
        const string erwartetesPasswort = "21102026";
        var entered = "";
        var anzeige = Anzeige(Plaetze("", 8, true));
        var hinweis = FahrbetriebPages.Info("Passwort eingeben");
        var seite = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4) };
        seite.Children.Add(Titel("App beenden"));
        seite.Children.Add(hinweis);
        seite.Children.Add(anzeige);
        seite.Children.Add(Ziffern(ziffer =>
        {
            if (entered.Length >= 8)
            {
                return;
            }

            entered += ziffer;
            anzeige.Text = Plaetze(entered, 8, true);
            hinweis.Text = "";
        }));
        var leiste = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 8, 0, 0) };
        var loeschen = Farbknopf("Löschen", "#FF0000");
        loeschen.Margin = new Thickness(0, 0, 4, 0);
        loeschen.Click += (_, _) =>
        {
            entered = "";
            anzeige.Text = Plaetze("", 8, true);
            hinweis.Text = "";
        };
        var abbrechen = Farbknopf("Abbrechen", "#2196F3");
        abbrechen.Margin = new Thickness(2, 0);
        Grid.SetColumn(abbrechen, 1);
        abbrechen.Click += (_, _) => zurueck();
        var ok = Farbknopf("Beenden", "#4CAF50");
        ok.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ok, 2);
        ok.Click += (_, _) =>
        {
            if (!string.Equals(entered, erwartetesPasswort, StringComparison.Ordinal))
            {
                entered = "";
                anzeige.Text = Plaetze("", 8, true);
                hinweis.Text = "Falsches Passwort";
                IvuTon.Error();
                return;
            }

            schliessen();
        };
        leiste.Children.Add(loeschen);
        leiste.Children.Add(abbrechen);
        leiste.Children.Add(ok);
        seite.Children.Add(leiste);
        return seite;
    }

    private static TextBlock Titel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 22,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };

    private static Control Ziffern(Action<string> beiZiffer)
    {
        var block = new StackPanel { Spacing = 6 };
        foreach (var reihe in new[] { "123", "456", "789", " 0 " })
        {
            var zeile = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            for (var i = 0; i < 3; i++)
            {
                var zeichen = reihe[i];
                if (zeichen == ' ')
                {
                    continue;
                }

                var knopf = Farbknopf(zeichen.ToString(), "#2196F3");
                knopf.MinHeight = 40;
                knopf.MinWidth = 0;
                knopf.FontSize = 22;
                Grid.SetColumn(knopf, i);
                knopf.Margin = new Thickness(3);
                var ziffer = zeichen.ToString();
                knopf.Click += (_, _) => beiZiffer(ziffer);
                zeile.Children.Add(knopf);
            }

            block.Children.Add(zeile);
        }

        return block;
    }

    private static Button Farbknopf(string text, string farbe)
    {
        var knopf = FahrbetriebPages.Touch(text);
        knopf.Background = new SolidColorBrush(Color.Parse(farbe));
        knopf.Focusable = false;
        return knopf;
    }

    private static Control Blatt(Control inhalt) => Rand(FahrTabletMenue.MenuScroll(inhalt));

    private static Control Fest(Control inhalt) => Rand(new Viewbox
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.DownOnly,
        Child = new Border
        {
            MaxWidth = 720,
            Child = inhalt
        }
    });

    private static Border Rand(Control inhalt) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#1A237E")),
        CornerRadius = new CornerRadius(8),
        Margin = new Thickness(8),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Child = inhalt
    };
}
