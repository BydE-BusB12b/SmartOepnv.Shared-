using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Fahrgeraet;

/// <summary>Die drei Hauptmenü-Dialoge wie auf dem Tablet.</summary>
public static class FahrTabletMenue
{
    public static Control Administration(FahrSession session, Action back, Action<Control> open)
    {
        var mode = GeraetEinstellungen.Text("zbl_contact_mode", "call");
        Button call = null!;
        Button funk = null!;
        void Paint()
        {
            var selected = GeraetEinstellungen.Text("zbl_contact_mode", "call");
            call.Background = Brush(selected == "call" ? "#4CAF50" : "#00008B");
            funk.Background = Brush(selected == "sprechwunsch" ? "#4CAF50" : "#00008B");
        }

        call = Item("anrufen", "#00008B", () =>
        {
            SaveText("zbl_contact_mode", "call");
            Paint();
        });
        funk = Item("Funk", "#00008B", () =>
        {
            SaveText("zbl_contact_mode", "sprechwunsch");
            Paint();
        });
        Paint();
        return Sheet("Verwaltung", "Zurück", "#FF0000", back,
            Item("KOM-Tracking", "#2196F3", () => open(KomTracking(session, back, open))),
            Item("Telefon", "#4CAF50", () => open(Phone(session, () => open(Administration(session, back, open)), open))),
            Item("Unternehmername", "#2196F3", () => open(TextPage(
                "Unternehmername", "Unternehmername", "company_name", "", () => open(Administration(session, back, open))))),
            Item("Nachricht", "#FF8C00", () => open(FahrLuecken.Nachricht(session))),
            Item("Mitarbeiterverwaltung", "#FFA500", () => open(FahrLuecken.Mitarbeiter(session))),
            Item("Datentransfer", "#4CAF50", () => open(FahrLuecken.Transfer(session))),
            Item("Protokoll", "#9C27B0", () => open(FahrLuecken.Protokoll())),
            Item("Dropbox-URL ändern", "#FF69B4", () => open(FahrLuecken.DropboxSeite(
                () => open(Administration(session, back, open)), session))),
            Label("ZBL-Button (Tablet)"),
            call,
            funk);
    }

    public static Control Displays(FahrSession session, Action back, Action<Control> open)
    {
        var countdown = GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), "departure_countdown_enabled", false);
        Button countdownButton = null!;
        void PaintCountdown()
        {
            var on = GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), "departure_countdown_enabled", false);
            countdownButton.Content = on ? "Abfahrtscountdown: Ein" : "Abfahrtscountdown: Aus";
            countdownButton.Background = Brush(on ? "#4CAF50" : "#FFA500");
        }

        countdownButton = Item(countdown ? "Abfahrtscountdown: Ein" : "Abfahrtscountdown: Aus", countdown ? "#4CAF50" : "#FFA500", () =>
        {
            var on = GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), "departure_countdown_enabled", false);
            SaveText("departure_countdown_enabled", on ? "false" : "true");
            PaintCountdown();
        });
        return Sheet("Innen-/Außenanzeigen+Entwerter", "Abbrechen", "#FF0000", back,
            Item("Protokoll außen wählen", "#FFA500", () => open(Protocol(
                "Protokoll außen wählen", DisplayCatalog.Outside, session.OutsideProtocol, value =>
                {
                    session.OutsideProtocol = value;
                    SaveText("selected_protocol", value);
                    session.SaveDevice();
                }, () => open(Displays(session, back, open))))),
            Item("Protokoll Innen wählen", "#FFA500", () => open(Protocol(
                "Protokoll Innen wählen", DisplayCatalog.Interior, session.InteriorProtocol, value =>
                {
                    session.InteriorProtocol = value;
                    SaveText("interior_protocol", value);
                    session.SaveDevice();
                }, () => open(Displays(session, back, open))))),
            Item("IBIS Transport (USB / IP)", "#9C27B0", () => open(IbisTransport(session, () => open(Displays(session, back, open))))),
            Item("Anzeigentests", "#FFA500", () => open(FahrLuecken.ManuelleAnzeige(session))),
            Item("Zielliste Außenanzeige", "#4CAF50", () => open(FahrLuecken.Ziele(session))),
            Item("SEV-TFT", "#4CAF50", () => open(Sev(session, () => open(Displays(session, back, open))))),
            Item("PDF aller Ziele", "#2196F3", () => open(FahrLuecken.Ziele(session))),
            countdownButton,
            Item("Entwerter", "#9C27B0", () => open(FahrgeraetMehr.Entwerter(session))));
    }

    public static Control Settings(FahrSession session, Action back, Action<Control> open)
    {
        return Sheet("Einstellungen", "Zurück", "#2196F3", back,
            Toggle("button_sounds_enabled", "Tastentöne aktivieren", true),
            Toggle("is_main_device", "Hauptgerät-Modus", false),
            Toggle("auto_open_next_route", "Automatische Routen-Öffnung", true),
            Toggle("night_mode_enabled", "Nachtmodus", false),
            Toggle("connection_send_without_approach", "Annäherung ausschalten", false),
            Toggle("museum_verkauf_enabled", "Verkauf aktiv", false),
            Toggle("hide_navigation_bar_enabled", "Status- und Navigationsleiste ausblenden", false),
            Toggle("auto_hide_navigation_on_gps", "Automatisch bei GPS-Betrieb", false),
            Direction(),
            Item("Sprache und Sound", "#2196F3", () => open(Language(() => open(Settings(session, back, open))))),
            Item("Passwort ändern", "#FF0000", () => open(TextPage(
                "Passwort ändern", "Neues Passwort", "menu_password", "", () => open(Settings(session, back, open))))),
            Item("Updates", "#4CAF50", () => open(FahrLuecken.Update(session, () => open(Settings(session, back, open))))),
            Item("Fahrzeugdiagnose", "#2196F3", () => open(FahrgeraetMehr.Fahrzeugdiagnose(session))),
            Item("VoIP / Funk", "#2196F3", () => open(VoipStatus(session, () => open(Settings(session, back, open))))),
            Item("Statusseite", "#4CAF50", () => { }),
            Item("VRR Debugmodus", "#FFA500", () => open(FahrFahrtSeiten.Vrr(session))),
            Item("Testfahrt (virtuell)", "#FFA500", () => { }));
    }

    public static Control VoipStatus(FahrSession session, Action back)
    {
        var status = new TextBlock
        {
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        var zeilen = new StackPanel();
        void Zeile(string label, string value) => zeilen.Children.Add(new TextBlock
        {
            Text = label + "\n" + value,
            Foreground = Brushes.White,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        });
        void Paint()
        {
            var blick = session.Funk.Blick;
            var zeichen = status.Inlines!;
            zeichen.Clear();
            zeichen.Add(new Run("●  ") { Foreground = new SolidColorBrush(Color.Parse(blick.Farbe)) });
            zeichen.Add(new Run(blick.Zustand) { Foreground = Brushes.White });
            zeilen.Children.Clear();
            Zeile("KOM-Telefon", blick.Telefon);
            Zeile("Dropbox-Config", blick.ConfigDatei);
            if (blick.Geraetename is { Length: > 0 } name)
            {
                Zeile("Gerätename", name);
            }

            if (blick.Peer is { Length: > 0 } peer)
            {
                Zeile("Peer-ID", peer);
            }

            if (blick.Signaling is { Length: > 0 } signaling)
            {
                Zeile("Signaling (Leitstelle)", signaling);
            }

            if (blick.LetzterAbruf is { Length: > 0 } abruf)
            {
                Zeile("Letzter Config-Abruf", abruf);
            }

            if (blick.ConfigStand is { Length: > 0 } stand)
            {
                Zeile("Config-Stand (Leitstelle)", stand);
            }

            if (blick.Details is { Length: > 0 } details)
            {
                Zeile("Details", details);
            }
        }

        Paint();
        void Geaendert() => Avalonia.Threading.Dispatcher.UIThread.Post(Paint);
        session.Funk.Changed += Geaendert;
        Button laden = null!;
        laden = Item("Config jetzt laden", "#4CAF50", () =>
        {
            laden.IsEnabled = false;
            _ = Nachladen();
            async Task Nachladen()
            {
                try
                {
                    await session.Funk.NeuLadenAsync(session.DevicePhone);
                }
                catch
                {
                    // Der Status steht in der Zeile darüber.
                }
                finally
                {
                    laden.IsEnabled = true;
                    Paint();
                }
            }
        });
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(36, 28, 36, 28) };
        panel.Children.Add(new TextBlock
        {
            Text = "VoIP / Funk",
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 22)
        });
        panel.Children.Add(status);
        panel.Children.Add(zeilen);
        panel.Children.Add(new TextBlock
        {
            Text = "VoIP wird automatisch aus Dropbox geladen. Die Leitstelle muss VoIP konfiguriert haben und das Tablet registriert sein.",
            Foreground = Brushes.LightGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 16)
        });
        panel.Children.Add(laden);
        panel.Children.Add(Item("Schließen", "#2196F3", back));
        var rahmen = new Border
        {
            Background = Brush("#00008B"),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = MenuScroll(panel)
        };
        rahmen.DetachedFromVisualTree += (_, _) => session.Funk.Changed -= Geaendert;
        return rahmen;
    }

    public static Control KomTracking(FahrSession session, Action back, Action<Control> open, string meldung = "")
    {
        var haupt = GeraetEinstellungen.Bool("is_main_device", false);
        var nl = Sprache.Niederlaendisch;
        var hinweis = new TextBlock
        {
            Text = meldung,
            Foreground = Brushes.White,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };
        var nummern = Hauptgeraete();
        var nummer1 = FahrbetriebPages.Field("Hauptgerät 1");
        var nummer2 = FahrbetriebPages.Field("Hauptgerät 2 (optional)");
        nummer1.Text = nummern.Count > 0 ? nummern[0] : "";
        nummer2.Text = nummern.Count > 1 ? nummern[1] : "";
        var felder = new StackPanel { Spacing = 8, IsVisible = !haupt };
        felder.Children.Add(new TextBlock
        {
            Text = "Hauptgerät-Telefonnummern (max. 2):",
            Foreground = Brushes.White,
            FontSize = 16
        });
        felder.Children.Add(nummer1);
        felder.Children.Add(nummer2);
        var speichern = Item("💾 Einstellungen speichern", "#2196F3", () =>
        {
            var liste = new[] { Rein(nummer1.Text), Rein(nummer2.Text) }
                .Where(n => n.Length >= 5)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToList();
            if (liste.Count == 0)
            {
                hinweis.Text = "Bitte mindestens eine gültige Hauptgerätenummer";
                return;
            }

            var arr = new JsonArray();
            foreach (var nummer in liste)
            {
                arr.Add(nummer);
            }

            var values = GeraetEinstellungen.Load();
            values["main_device_phones_json"] = arr.ToJsonString();
            values["main_device_number"] = liste[0];
            values["main_device_phone"] = liste[0];
            GeraetEinstellungen.Save(values);
            StandortSend.Refresh(session);
            hinweis.Text = "Gespeichert – Tracking gestartet (" + liste.Count + " Hauptgerät(e))";
        });
        speichern.IsVisible = !haupt;
        var karte = Item("🗺️ Tracking-Karte öffnen", "#4CAF50", () =>
        {
            if (!GeraetEinstellungen.Bool("is_main_device", false))
            {
                hinweis.Text = "Karte nur im Hauptgerät-Modus verfügbar";
                return;
            }

            open(TrackingKarte(session, () => open(KomTracking(session, back, open))));
        });
        var ziel = Item(nl ? "🎯 Doel op afstand" : "Fernsteuerung Ziel", "#9C27B0", () =>
            open(Auswahl(
                "Ziel auswählen (ITCS-Zielliste)",
                KomOutsideDestinationCatalog.LoadListEnabledNames(session.Package),
                "Keine Ziele in der Außen-Zielliste. Bitte unter Menü → Anzeigen anlegen.",
                name => open(FahrzeugWahl(session, "Fahrzeug auswählen", "Keine KOM-Fahrzeuge hinterlegt.", async telefon =>
                {
                    var text = await Senden(client => KomRemoteDestinationService.UploadAsync(client, telefon, name), "Fernziel wurde an das Fahrzeug gesendet.");
                    open(KomTracking(session, back, open, text));
                }, () => open(KomTracking(session, back, open)))),
                () => open(KomTracking(session, back, open)))));
        var update = Item(nl ? "🔄 Update op afstand starten" : "Fernupdate auslösen", "#FF8C00", () =>
            open(FahrzeugWahl(session, "Fahrzeug für Fernupdate auswählen", "Keine KOM-Fahrzeuge hinterlegt.", async telefon =>
            {
                var text = await Senden(client => RemoteManualUpdateService.UploadAsync(client, telefon), "Fernupdate wurde an das Fahrzeug gesendet.");
                open(KomTracking(session, back, open, text));
            }, () => open(KomTracking(session, back, open)))));
        var route = Item(nl ? "🚌 Route op afstand" : "Fernroute senden", "#4CAF50", () =>
        {
            var namen = session.Package?.RouteNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            open(Auswahl(
                "Route für Fernsteuerung wählen",
                namen,
                "Keine Routen vorhanden – zuerst Routen anlegen oder importieren.",
                name => open(FahrzeugWahl(session, "Fahrzeug für Fernroute wählen", "Keine KOM-Fahrzeuge hinterlegt.", async telefon =>
                {
                    var text = await Senden(client => KomRemoteRouteService.UploadAsync(client, telefon, name), "Fernroute wurde an das Fahrzeug gesendet.");
                    open(KomTracking(session, back, open, text));
                }, () => open(KomTracking(session, back, open)))),
                () => open(KomTracking(session, back, open))));
        });
        var durchsage = Item("Fahrgastraum-Durchsage", "#FF0000", () =>
            hinweis.Text = "Die Fahrgastraum-Durchsage wird mit dem Mikrofon aufgenommen.");
        var verwaltung = Item(nl ? "👥 Voertuigbeheer" : "👥 Fahrzeug-Verwaltung", "#FFA500", () =>
            open(FahrzeugListe(session, () => open(KomTracking(session, back, open)))));
        foreach (var knopf in new[] { ziel, update, route, durchsage, verwaltung })
        {
            knopf.IsVisible = haupt;
        }

        var links = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        links.Children.Add(new TextBlock
        {
            Text = nl ? "📍 Trekking Bus" : "📍 KOM-Tracking",
            Foreground = Brushes.White,
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 4)
        });
        links.Children.Add(felder);
        links.Children.Add(speichern);
        links.Children.Add(karte);
        links.Children.Add(ziel);
        links.Children.Add(update);
        links.Children.Add(route);
        links.Children.Add(durchsage);
        links.Children.Add(verwaltung);
        links.Children.Add(hinweis);
        links.Children.Add(Item("⬅️ Zurück zum Hauptmenü", "#00008B", back));

        var info = new Border
        {
            Background = Brush("#1A237E"),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 16, 16, 16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "ℹ️ Informationen:",
                        Foreground = Brushes.White,
                        FontSize = 16,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = "• Hauptgerät: Empfängt Standort-Updates von allen Fahrzeugen\n• Fahrzeug: Sendet automatisch Standort-Updates an das Hauptgerät\n• Updates alle 30 Sekunden\n• Verschlüsselte SMS-Kommunikation\n• Offline-Erkennung nach 5 Minuten",
                        Foreground = Brushes.White,
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22
                    }
                }
            }
        };
        var raster = new Grid();
        raster.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        raster.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(300)));
        var linksScroll = MenuScroll(links);
        Grid.SetColumn(linksScroll, 0);
        Grid.SetColumn(info, 1);
        raster.Children.Add(linksScroll);
        raster.Children.Add(info);
        return new Border
        {
            Background = Brush("#00008B"),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = raster
        };
    }

    private static Control TrackingKarte(FahrSession session, Action back)
    {
        var status = new TextBlock
        {
            Text = "Tracking aktiv...",
            Foreground = Brushes.White,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };
        var liste = new StackPanel { Spacing = 8 };
        void Malen(IReadOnlyList<VehicleLiveState> staende, string fehler)
        {
            liste.Children.Clear();
            if (fehler.Length > 0)
            {
                status.Text = fehler;
                return;
            }

            status.Text = staende.Count == 0 ? "Tracking aktiv..." : "Tracking aktiv... " + staende.Count;
            if (staende.Count == 0)
            {
                liste.Children.Add(new TextBlock
                {
                    Text = "Noch keine Standort-Daten.",
                    Foreground = Brushes.White,
                    FontSize = 16
                });
                return;
            }

            foreach (var stand in staende)
            {
                liste.Children.Add(new TextBlock
                {
                    Text = stand.DisplayName + "\n" + (stand.LineCourse ?? "") + "  " + (stand.RouteName ?? "") + "\n" + (stand.StopName ?? "") + "  " + stand.Status,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        async Task Laden()
        {
            var roster = (session.Package?.RegisteredVehicles ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.PhoneNumber))
                .Select(v => new RegisteredVehicleInfo
                {
                    Name = string.IsNullOrWhiteSpace(v.Name) ? v.PhoneNumber : v.Name,
                    PhoneNumber = v.PhoneNumber
                })
                .ToList();
            IReadOnlyList<VehicleLiveState> staende = [];
            var fehler = "";
            try
            {
                var client = FahrbetriebPages.OpenDropbox();
                if (client is null)
                {
                    fehler = "Dropbox ist nicht verbunden.";
                }
                else
                {
                    staende = await new VehicleTrackingService(client).SyncAsync(roster);
                }
            }
            catch (Exception ex)
            {
                fehler = ex.Message;
            }

            Dispatcher.UIThread.Post(() => Malen(staende, fehler));
        }

        var links = new StackPanel { Spacing = 8, Margin = new Thickness(12), Width = 220 };
        links.Children.Add(status);
        links.Children.Add(Item("🔄", "#2196F3", () => _ = Laden()));
        links.Children.Add(Item("⬅️ Zurück", "#00008B", back));
        var raster = new Grid();
        raster.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
        raster.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(links, 0);
        var rechts = MenuScroll(liste);
        Grid.SetColumn(rechts, 1);
        raster.Children.Add(links);
        raster.Children.Add(rechts);
        _ = Laden();
        return new Border
        {
            Background = Brush("#00008B"),
            Margin = new Thickness(8),
            Child = raster
        };
    }

    private static Control FahrzeugListe(FahrSession session, Action back)
    {
        var nl = Sprache.Niederlaendisch;
        var zeilen = KomFahrzeuge(session)
            .Select(fahrzeug => Item(fahrzeug.Anzeige, "#2196F3", () => { }))
            .ToArray();
        var leer = zeilen.Length == 0
            ? new Control[] { Label(nl ? "Geen voertuigen" : "Keine KOM-Fahrzeuge hinterlegt.") }
            : zeilen;
        return Sheet(
            nl ? "👥 Voertuigbeheer" : "👥 Fahrzeug-Verwaltung",
            "Zurück",
            "#00008B",
            back,
            new Control[]
            {
                Label(nl ? "📋 Geregistreerde voertuigen:" : "📋 Registrierte Fahrzeuge:"),
                Label(nl ? "Tippen: hernoemen · Long-Click: verwijderen" : "Tippen: umbenennen · Long-Click: löschen")
            }.Concat(leer).ToArray());
    }

    private static Control Auswahl(string titel, IReadOnlyList<string> eintraege, string leer, Action<string> gewaehlt, Action back)
    {
        if (eintraege.Count == 0)
        {
            return Sheet(titel, "Abbrechen", "#FF0000", back, Label(leer));
        }

        return Sheet(titel, "Abbrechen", "#FF0000", back, eintraege.Select(eintrag => Item(eintrag, "#2196F3", () => gewaehlt(eintrag))).ToArray());
    }

    private static Control FahrzeugWahl(FahrSession session, string titel, string leer, Func<string, Task> gewaehlt, Action back)
    {
        var fahrzeuge = KomFahrzeuge(session);
        if (fahrzeuge.Count == 0)
        {
            return Sheet(titel, "Abbrechen", "#FF0000", back, Label(leer));
        }

        return Sheet(
            titel,
            "Abbrechen",
            "#FF0000",
            back,
            fahrzeuge.Select(fahrzeug => Item(fahrzeug.Anzeige, "#2196F3", () => _ = gewaehlt(fahrzeug.Telefon))).ToArray());
    }

    private static async Task<string> Senden(Func<DropboxApiClient, Task<long>> senden, string erfolg)
    {
        try
        {
            var client = FahrbetriebPages.OpenDropbox();
            if (client is null)
            {
                return "Dropbox ist nicht verbunden.";
            }

            var id = await senden(client);
            return id > 0 ? erfolg : "Upload fehlgeschlagen (Dropbox / Netzwerk).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static IReadOnlyList<string> Hauptgeraete()
    {
        var json = GeraetEinstellungen.Text("main_device_phones_json", "");
        if (json.Length > 0)
        {
            try
            {
                var liste = JsonNode.Parse(json)?.AsArray()?
                    .Select(knoten => knoten?.GetValue<string>() ?? "")
                    .Where(text => text.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .ToList();
                if (liste is { Count: > 0 })
                {
                    return liste;
                }
            }
            catch
            {
                // Alte einzelne Nummer bleibt der Ersatz.
            }
        }

        var einzeln = GeraetEinstellungen.Text("main_device_number", "");
        if (einzeln.Length == 0)
        {
            einzeln = GeraetEinstellungen.Text("main_device_phone", "");
        }

        return einzeln.Length == 0 ? [] : [einzeln];
    }

    private static List<(string Anzeige, string Telefon)> KomFahrzeuge(FahrSession session) =>
        (session.Package?.RegisteredVehicles ?? [])
            .Where(fahrzeug => !string.IsNullOrWhiteSpace(fahrzeug.PhoneNumber))
            .Select(fahrzeug => (
                string.IsNullOrWhiteSpace(fahrzeug.Name) ? fahrzeug.PhoneNumber : fahrzeug.Name + "  " + fahrzeug.PhoneNumber,
                fahrzeug.PhoneNumber))
            .ToList();

    private static string Rein(string? roh)
    {
        var text = (roh ?? "").Trim();
        return new string(text.Where(zeichen => char.IsDigit(zeichen) || zeichen == '+').ToArray());
    }

    private static Control Direction()
    {
        var on = GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), "direction_detection_enabled", true);
        Button button = null!;
        button = Item(on ? "Richtungserkennung: Ein" : "Richtungserkennung: Aus", on ? "#4CAF50" : "#00008B", () =>
        {
            var next = !GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), "direction_detection_enabled", true);
            SaveText("direction_detection_enabled", next ? "true" : "false");
            button.Content = next ? "Richtungserkennung: Ein" : "Richtungserkennung: Aus";
            button.Background = Brush(next ? "#4CAF50" : "#00008B");
        });
        return button;
    }

    private static Control Phone(FahrSession session, Action back, Action<Control> open) =>
        Sheet("Telefon", "Zurück", "#2196F3", back,
            Item("Gerät registrieren", "#2196F3", () => open(FahrLuecken.Geraete(session))),
            Item("Handynummer ZBL", "#4CAF50", () => open(TextPage(
                "Handynummer ZBL", "Nummer", "zbl_phone_number", "", () => open(Phone(session, back, open))))),
            Item("Hauptgerät-Nummer", "#FFA500", () => open(TextPage(
                "Hauptgerät-Nummer", "Nummer", "main_device_phone", "", () => open(Phone(session, back, open))))));

    private static Control Language(Action back)
    {
        var current = GeraetEinstellungen.Text("language_selected_index", "0");
        Control Choice(string index, string caption) =>
            Item(caption, current == index ? "#4CAF50" : "#2196F3", () =>
            {
                SaveText("language_selected_index", index);
                back();
            });
        return Sheet("Sprache und Sound", "Zurück", "#2196F3", back,
            Choice("0", "Deutsch"),
            Choice("1", "Niederländisch"),
            Choice("2", "Englisch"));
    }

    private static Control Protocol(string title, IReadOnlyList<string> items, string current, Action<string> save, Action back)
    {
        var rows = items.Select(item => Item(item, item == current ? "#4CAF50" : "#FFA500", () =>
        {
            save(item);
            back();
        })).ToArray();
        return Sheet(title, "Abbrechen", "#FF0000", back, rows);
    }

    private static Control IbisTransport(FahrSession session, Action back)
    {
        var outside = GeraetEinstellungen.Text("ibis_exterior_transport", "usb");
        var inside = GeraetEinstellungen.Text("ibis_interior_transport", "usb");
        return Sheet("IBIS Transport (USB / IP)", "Zurück", "#2196F3", back,
            Label("Außen"),
            Item("USB", outside == "usb" ? "#4CAF50" : "#9C27B0", () =>
            {
                SaveText("ibis_exterior_transport", "usb");
                back();
            }),
            Item("IP", outside == "wlan" ? "#4CAF50" : "#9C27B0", () =>
            {
                SaveText("ibis_exterior_transport", "wlan");
                back();
            }),
            Label("Innen"),
            Item("USB", inside == "usb" ? "#4CAF50" : "#9C27B0", () =>
            {
                SaveText("ibis_interior_transport", "usb");
                back();
            }),
            Item("IP", inside == "wlan" ? "#4CAF50" : "#9C27B0", () =>
            {
                SaveText("ibis_interior_transport", "wlan");
                back();
            }));
    }

    private static Control Sev(FahrSession session, Action back)
    {
        var host = FahrbetriebPages.Field("SEV-TFT Host");
        host.Text = GeraetEinstellungen.Text("sev_tft_host", "");
        var port = FahrbetriebPages.Field("SEV-TFT Port");
        port.Text = GeraetEinstellungen.Text("sev_tft_port", "80");
        var save = Item("Speichern", "#4CAF50", () =>
        {
            SaveText("sev_tft_host", host.Text?.Trim() ?? "");
            SaveText("sev_tft_port", port.Text?.Trim() ?? "80");
            SaveText("sev_tft_enabled", "true");
            back();
        });
        return Sheet("SEV-TFT", "Abbrechen", "#FF0000", back, host, port, save);
    }

    private static Control TextPage(string title, string field, string key, string fallback, Action back)
    {
        var box = FahrbetriebPages.Field(field);
        box.Text = GeraetEinstellungen.Text(key, fallback);
        var save = Item("Speichern", "#4CAF50", () =>
        {
            SaveText(key, box.Text?.Trim() ?? "");
            back();
        });
        return Sheet(title, "Abbrechen", "#FF0000", back, box, save);
    }

    private static CheckBox Toggle(string key, string caption, bool fallback)
    {
        var box = FahrbetriebPages.Check(caption, GeraetEinstellungen.Bool(GeraetEinstellungen.Load(), key, fallback));
        box.IsCheckedChanged += (_, _) => SaveText(key, box.IsChecked == true ? "true" : "false");
        return box;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 16,
        Margin = new Thickness(4, 8, 0, 0)
    };

    private static Button Item(string text, string color, Action click)
    {
        var button = new Button
        {
            Content = text,
            Classes = { "menu" },
            Background = Brush(color),
            MinHeight = 46,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.Click += (_, _) =>
        {
            TastenTon.Play();
            click();
        };
        return button;
    }

    private static Control Sheet(string title, string backText, string backColor, Action back, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        panel.Children.Add(Item(backText, backColor, back));
        return new Border
        {
            Background = Brush("#1A237E"),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = MenuScroll(panel)
        };
    }

    internal static ScrollViewer MenuScroll(Control content)
    {
        var scroll = new ScrollViewer
        {
            Classes = { "menuScroll" },
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        FingerZug.An(scroll);
        FingerZug.In(content);
        return scroll;
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static void SaveText(string key, string value)
    {
        var values = GeraetEinstellungen.Load();
        values[key] = value;
        GeraetEinstellungen.Save(values);
    }
}
