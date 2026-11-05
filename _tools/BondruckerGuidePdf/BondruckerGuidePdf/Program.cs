using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = args.FirstOrDefault()
             ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "SmartOepnv_Bondrucker_Anforderungen.pdf");
var created = DateTime.Now;

Document.Create(document =>
{
    document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.25f));

        page.Header().Column(h =>
        {
            h.Item().Text("Smart ÖPNV · Museums-Ticketing (VERKAUF)").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken2);
            h.Item().Text("Bondrucker – was wird benötigt?").FontSize(16).SemiBold();
            h.Item().Text("Hardware · Papier · App · Planer · Druckvorlage · Checkliste")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
            h.Item().PaddingTop(4).LineHorizontal(1.2f).LineColor(Colors.Blue.Darken2);
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span($"Stand {created:dd.MM.yyyy} · ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" / ").FontSize(8);
            t.TotalPages().FontSize(8);
        });

        page.Content().PaddingTop(10).Column(col =>
        {
            col.Spacing(7);

            H(col, "1. Zweck");
            P(col, "Dieses Dokument listet alles auf, was für den Ticketdruck im Museums-Verkauf " +
                   "(VERKAUF in der App GPSAnsagen) benötigt wird: Bondrucker, Papier, Anschluss, " +
                   "App-Einstellungen, Planer-Daten und Druckvorlage. Es richtet sich an Werkstatt, " +
                   "Betrieb und Planer-Nutzer.");

            Callout(col, "Hinweis: Der Druck erfolgt über ESC/POS (Thermodrucker). Die Druckvorlage " +
                         "wird im Planer in Millimetern gestaltet; die App rendert daraus ein Bitmap " +
                         "für den Bondrucker.");

            H(col, "2. Kurzüberblick – Bausteine");
            SimpleTable(col,
                ["Baustein", "Rolle"],
                [
                    ["Bondrucker (ESC/POS)", "Thermodruck 58 mm, USB und/oder Bluetooth"],
                    ["Thermopapier 58 mm", "Rollenware; Ticketmaß ca. 58 × 50 mm (Vorlage)"],
                    ["Android-Tablet (App)", "VERKAUF, Produkte, Druckbefehl"],
                    ["Planer", "Ticketprodukte, Preise, Druckvorlage speichern"],
                    ["Fahrzeug-Sync", "routes_export / routes_update mit ticketProducts + ticketTemplate"],
                    ["Logo (optional)", "ticket_logo.png im Paket / Vorlage-Feld „logo“"]
                ]);

            H(col, "3. Hardware – Bondrucker");
            H2(col, "3.1 Empfohlene Eigenschaften");
            Bullet(col, "Thermodrucker mit ESC/POS-Kompatibilität (übliche Bondrucker-Klasse).");
            Bullet(col, "Papierbreite 58 mm (Standard-Bons / Museums-Ticketbreite).");
            Bullet(col, "Schnittstelle: USB-OTG zum Tablet und/oder Bluetooth Classic (SPP).");
            Bullet(col, "Zuverlässige Stromversorgung im Fahrzeug (12/24 V → Drucker-Netzteil bzw. DC-Adapter laut Hersteller).");
            Bullet(col, "Ausreichend Druckdichte für Stempelstreifen und kleine Schrift (7–9 pt Hinweise).");

            H2(col, "3.2 Was der Drucker können muss");
            Bullet(col, "Bitmap-/Rasterdruck (App sendet gerenderte Ticket-Grafik, nicht nur Klartextzeilen).");
            Bullet(col, "Schneller Schnitt oder manuelles Abreißen nach Ausdruck (je nach Gerät).");
            Bullet(col, "Stabiler Treiber/Modus unter Android (kein herstellergebundenes Cloud-only-Modell).");

            Callout(col, "Kein klassischer A4-BürodDrucker und kein reiner Label-Drucker ohne ESC/POS. " +
                         "Kabeladapter (USB-C-OTG) am Tablet nicht vergessen.");

            H(col, "4. Papier & physisches Ticket");
            SimpleTable(col,
                ["Thema", "Vorgabe"],
                [
                    ["Breite", "58 mm Thermorolle"],
                    ["Vorlagenhöhe (Planer)", "Standard 50 mm (anpassbar)"],
                    ["Hintergrund", "Weiß (#FFFFFF) – drucknah"],
                    ["Stempelbereich", "Diagonale Stempelstreifen rechts (Feldtyp stampStripes)"],
                    ["Entwerter", "Hinweistext „Nur gültig mit Entwerteraufdruck“ in der Vorlage"]
                ]);
            P(col, "Die Stempelstreifen sind Teil der Druckvorlage (wie Verbund-Thermotickets). " +
                   "Sie ersetzen keinen physischen Entwerteraufdruck – der erfolgt weiter am Entwerter.");

            H(col, "5. App (GPSAnsagen) – Voraussetzungen");
            Numbered(col, 1, "Feature „Museumsverkauf / VERKAUF“ in den Einstellungen aktivieren.");
            Numbered(col, 2, "Fahrer angemeldet; bei 5-Tage-Sperre ist VERKAUF bis Planer-Freischaltung gesperrt.");
            Numbered(col, 3, "Gültige Route / Linie-Kurs gewählt (nicht 000/00, nicht „Route auswählen…“).");
            Numbered(col, 4, "Bondrucker verbunden (USB oder Bluetooth) und bereit.");
            Numbered(col, 5, "ticketProducts und ticketTemplate aus dem letzten Sync übernommen.");

            H2(col, "5.1 Bedienung kurz");
            Bullet(col, "ITCS → VERKAUF: Ticket wählen, optional Warenkorb, dann „Drucken“.");
            Bullet(col, "„Warenkorb hinzufügen“ / „Warenkorb löschen“: nur Zwischenspeicher, kein Verkauf.");
            Bullet(col, "„Drucken“: bucht Verkauf und erzeugt den Bond (Historie / 5-Tage-Logik).");
            Bullet(col, "Wechsel VERKAUF → ITCS: Warenkorb wird geleert (kein Storno, keine Historie).");

            H(col, "6. Planer – was vorbereitet werden muss");
            H2(col, "6.1 Ticketprodukte");
            Bullet(col, "Menü Ticketing → Produkte: Name, Preis (€), Aktiv, Standard, Sortierung.");
            Bullet(col, "Speichern – Daten landen als ticketProducts im Route-Paket.");
            Bullet(col, "Nur aktive Produkte erscheinen in der App unter VERKAUF.");

            H2(col, "6.2 Druckvorlage");
            Bullet(col, "Tab Druckvorlage: Felder in mm (Logo, Ticketname, Preis, Preisstufe, Hinweis, Zahlenkette, Stempelstreifen).");
            Bullet(col, "Vorschau entspricht dem Druckmaßstab (weißes Papier, Stempelstreifen rechts).");
            Bullet(col, "Optional Logo-Datei ticket_logo.png referenzieren.");
            Bullet(col, "„Standard“ lädt 58×50 mm inkl. Stempelstreifen neu.");

            H2(col, "6.3 Sync zum Fahrzeug");
            Bullet(col, "Nach Speichern: routes_update.json (ohne Audio) oder routes_export.json senden.");
            Bullet(col, "Beide Profile müssen ticketProducts und ticketTemplate enthalten.");
            Bullet(col, "App muss das Update importieren (Lite-Update übernimmt Ticketing mit).");

            H(col, "7. Datenfelder im Paket (technisch)");
            Code(col,
                "ticketProducts: [ { id, name, priceCents, isDefault, active, sortOrder }, … ]\n" +
                "ticketTemplate: { pageWidthMm, pageHeightMm, backgroundColor, fields: [ … ] }\n" +
                "Feldtypen: logo | ticketName | price | priceLevel | numberChain | staticText | stampStripes");

            H(col, "8. Werkstatt-Checkliste");
            Check(col, "Bondrucker ESC/POS 58 mm montiert und mit Strom versorgt");
            Check(col, "USB-OTG oder Bluetooth-Kopplung zum Tablet getestet");
            Check(col, "Thermorolle 58 mm eingelegt, Papierlauf korrekt");
            Check(col, "Testausdruck: Logo/Text lesbar, Stempelstreifen rechts sichtbar");
            Check(col, "Planer: Produkte + Vorlage gespeichert");
            Check(col, "Fahrzeug-Sync (routes_update oder Export) durchgeführt");
            Check(col, "App: VERKAUF aktiv, Route gewählt, Preise wie im Planer");
            Check(col, "Probeverkauf + Storno-Fenster (≤ 3 Min.) verstanden");

            H(col, "9. Typische Probleme");
            SimpleTable(col,
                ["Symptom", "Prüfen"],
                [
                    ["Alte Preise / Seed-Produkte", "Sync fehlt oder Lite-Import vor Fix; Update erneut senden"],
                    ["Kein Ausdruck", "Verbindung USB/BT, Papier, ESC/POS, Berechtigungen"],
                    ["Layout verschoben", "Vorlage mm vs. 58 mm Rolle; Stempelstreifen-Feld"],
                    ["VERKAUF grau / gesperrt", "Route 000/00 oder 5-Tage-Sperre (Planer freischalten)"],
                    ["Warenkorb im Storno", "Gewollt nicht – nur gedruckte Verkäufe stornierbar"]
                ]);

            H(col, "10. Abgrenzung");
            Bullet(col, "Bondrucker ≠ Entwerter: Entwerteraufdruck bleibt separate Fahrzeugtechnik.");
            Bullet(col, "Bondrucker ≠ IBIS/Anzeigen: Ticketing hängt nicht an IBIS-IP.");
            Bullet(col, "Verkaufshistorie (verkauf_*.json) ist Fahrer-/Gerätehistorie, nicht die Druckvorlage.");

            col.Item().PaddingTop(8).Text("Ende · Smart ÖPNV Bondrucker / Museums-Ticketing")
                .FontSize(9).FontColor(Colors.Grey.Medium);
        });
    });
}).GeneratePdf(outPath);

Console.WriteLine(outPath);

static void H(ColumnDescriptor col, string title) =>
    col.Item().PaddingTop(6).Text(title).FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);

static void H2(ColumnDescriptor col, string title) =>
    col.Item().PaddingTop(2).Text(title).FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);

static void P(ColumnDescriptor col, string text) =>
    col.Item().Text(text).FontSize(10);

static void Bullet(ColumnDescriptor col, string text) =>
    col.Item().PaddingLeft(6).Text("•  " + text).FontSize(9.5f);

static void Numbered(ColumnDescriptor col, int n, string text) =>
    col.Item().PaddingLeft(6).Text($"{n}.  {text}").FontSize(9.5f);

static void Check(ColumnDescriptor col, string text) =>
    col.Item().PaddingLeft(6).Text("☐  " + text).FontSize(9.5f);

static void Callout(ColumnDescriptor col, string text) =>
    col.Item().Background(Colors.Blue.Lighten5).Border(0.5f).BorderColor(Colors.Blue.Lighten2)
        .Padding(8).Text(text).FontSize(9).FontColor(Colors.Blue.Darken4);

static void Code(ColumnDescriptor col, string text) =>
    col.Item().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1)
        .Padding(8).Text(text).FontSize(8).FontFamily(Fonts.CourierNew).FontColor(Colors.Grey.Darken4);

static void SimpleTable(ColumnDescriptor col, string[] headers, string[][] rows, float fontSize = 8.5f)
{
    col.Item().Table(t =>
    {
        t.ColumnsDefinition(c =>
        {
            foreach (var _ in headers)
                c.RelativeColumn();
        });
        t.Header(h =>
        {
            foreach (var header in headers)
                h.Cell().Background(Colors.Blue.Darken3).Padding(4)
                    .Text(header).FontSize(fontSize).SemiBold().FontColor(Colors.White);
        });
        var alt = false;
        foreach (var row in rows)
        {
            var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
            alt = !alt;
            foreach (var cell in row)
                t.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Padding(4).Text(cell).FontSize(fontSize);
        }
    });
}
