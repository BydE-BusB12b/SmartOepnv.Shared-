using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = args.FirstOrDefault()
             ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "SmartOepnv_Anleitung_WLAN_IBIS_Serial-over-IP.pdf");
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
            h.Item().Text("Smart ÖPNV · GPSAnsagen").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken2);
            h.Item().Text("Anleitung: IBIS über WLAN (Serial-over-IP)").FontSize(16).SemiBold();
            h.Item().Text("Teltonika / RS232-Bridge · App-Einstellungen · Inbetriebnahme · Fehlersuche")
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
            P(col, "Diese Anleitung beschreibt, wie klassisches IBIS (VDV 300, ASCII-Telegramme) " +
                   "vom Tablet per WLAN an den Wagenbus kommt – über eine Serial-over-IP-Bridge " +
                   "(z. B. Teltonika RUT). Das ist nicht IBIS-IP / VDV 301, sondern der bekannte " +
                   "Wagenbus (1200 7E2), nur ohne USB-Kabel zum Tablet.");

            Callout(col, "Kurz: App → TCP/UDP über WLAN → Router (Serial Utilities) → RS232 → IBIS-Geräte " +
                         "(Außenanzeige, Innenanzeige, …).");

            H(col, "2. Architektur");
            SimpleTable(col,
                ["Baustein", "Aufgabe"],
                [
                    ["GPSAnsagen-App", "Erzeugt IBIS-Telegramme (DS001, DS003, DS021T, …) und sendet Bytes per TCP/UDP"],
                    ["Fahrzeug-WLAN / LAN", "Gleiches Subnetz: Tablet und Bridge"],
                    ["Teltonika (o. ä.)", "Services → Serial Utilities → Over IP: RS232 ↔ IP"],
                    ["RS232 / IBIS-Wandler", "Physikalischer Anschluss an den Wagenbus (je nach Fahrzeug)"]
                ]);

            H(col, "3. Teltonika – Serial Utilities (Beispiel IBIS_Wagen_9001)");
            P(col, "Weboberfläche typisch: https://192.168.1.1 → Services → Serial Utilities → " +
                   "Over IP configuration. Gerät z. B. „IBIS_Wagen_9001“.");

            H2(col, "3.1 Serielle Parameter (müssen zum Wagenbus passen)");
            SimpleTable(col,
                ["Parameter", "Sollwert", "Hinweis"],
                [
                    ["Enable", "on", "Profil aktiv"],
                    ["Device", "rs232", "Physikalischer Port der Bridge"],
                    ["Baud rate", "1200", "Standard-IBIS"],
                    ["Data bits", "7", "7E2"],
                    ["Stop bits", "2", "7E2"],
                    ["Parity", "Even", "7E2 (gerade Parität)"],
                    ["Flow control", "None", "kein Hardware-Handshake"],
                    ["Echo", "off", "kein Echo zurück ins Netz"]
                ]);

            H2(col, "3.2 Advanced");
            SimpleTable(col,
                ["Parameter", "Sollwert", "Hinweis"],
                [
                    ["Raw mode", "on", "Bytes unverändert durchreichen (empfohlen)"],
                    ["Fill zeros", "off", "keine Auffüllung"],
                    ["Timeout", "0", "kein künstliches Timeout (bzw. Hersteller-Doku beachten)"]
                ]);

            Callout(col, "Diese Werte entsprechen dem Standard-IBIS der App am USB-Wandler (1200 7E2). " +
                         "Abweichungen nur, wenn die Fahrzeugverkabelung ausdrücklich andere Parameter verlangt.");

            H(col, "4. App – WLAN-Transport einstellen");
            P(col, "In GPSAnsagen den Dialog für IBIS-Transport / TCP-IP öffnen " +
                   "(Einstellungen / Verbindung Außen-Innen).");

            Numbered(col, 1, "Außenanzeige und/oder Innenanzeige auf „WLAN“ stellen (nicht USB).");
            Numbered(col, 2, "Bridge-Host: IP der Teltonika (Beispiel-Hinweis in der App: 192.168.1.1).");
            Numbered(col, 3, "Port: den Port aus Serial Utilities / Over IP (TCP-Server oder Client laut Router-Doku).");
            Numbered(col, 4, "Protokoll: TCP (üblich) oder UDP – muss zur Teltonika-Seite passen.");
            Numbered(col, 5, "Speichern. Statuszeile sollte gültige Bridge-Konfiguration melden.");

            H2(col, "4.1 Was die App intern macht");
            Bullet(col, "TcpSocketDisplayManager sendet Rohbytes an Host:Port.");
            Bullet(col, "Nur wenn Außen bzw. Innen auf WLAN steht und Host/Port gesetzt sind.");
            Bullet(col, "USB-Wandler und WLAN-Bridge können je Kanal getrennt gewählt werden " +
                        "(Außen WLAN, Innen USB – oder umgekehrt).");

            H(col, "5. Netzwerk-Checkliste");
            Check(col, "Tablet im gleichen Subnetz wie die Teltonika (kein Gast-WLAN mit Client-Isolation).");
            Check(col, "Teltonika-Web UI erreichbar (z. B. https://192.168.1.1).");
            Check(col, "Serial-Profil Enabled, Raw mode on, 1200 7E2.");
            Check(col, "In der App Außen/Innen = WLAN, Host = Router-IP, Port = Serial-Utilities-Port.");
            Check(col, "Test: Ziel senden → Anzeige am Fahrzeug ändert sich.");
            Check(col, "Logcat-Filter bei Problemen: TcpSocketDisplay, IbisIpTransport, DisplayProtocol.");

            H(col, "6. Abgrenzung zu IBIS-IP (VDV 301)");
            SimpleTable(col,
                ["Thema", "WLAN Serial-over-IP (diese Anleitung)", "IBIS-IP / VDV 301"],
                [
                    ["Inhalt", "Klassische IBIS-Telegramme (DS…)", "HTTP/XML-Dienste"],
                    ["Physik", "RS232 hinter der Bridge", "Ethernet/WLAN-Clients"],
                    ["App-Rolle", "TCP/UDP-Client zur Bridge", "HTTP-Provider im LAN"],
                    ["Anzeigen", "Geräte, die VDV 300 erwarten", "Geräte, die VDV 301 sprechen"]
                ]);

            Callout(col, "Eine klassische Außenanzeige braucht weiterhin VDV 300 (USB oder diese WLAN-Bridge). " +
                         "IBIS-IP ersetzt das nicht automatisch.");

            H(col, "7. Fehlersuche");
            SimpleTable(col,
                ["Symptom", "Prüfen"],
                [
                    ["Keine Reaktion der Anzeige", "WLAN-Transport aktiv? Host/Port? Raw mode? RS232-Kabel/Polarität?"],
                    ["App sendet, Bus tot", "Baud/7E2 an Teltonika vs. Fahrzeug; Echo aus; richtiger rs232-Port"],
                    ["Verbindung timeout", "Firewall auf Router, Port der Over-IP-Config, TCP vs. UDP"],
                    ["Nur USB funktioniert", "In der App Kanal noch auf USB? Bridge-IP erreichbar (Ping)?"],
                    ["Falsches Netz", "Tablet am Fahrzeug-WLAN, nicht am öffentlichen Hotspot"]
                ]);

            H(col, "8. Beispielwerte (Werkstatt)");
            Code(col,
                "Teltonika Web:  https://192.168.1.1\n" +
                "Profil-Name:    IBIS_Wagen_9001 (Beispiel)\n" +
                "Seriell:        rs232 · 1200 · 7 · 2 · Even · Flow none · Echo off\n" +
                "Advanced:       Raw mode on\n" +
                "App:            Außen = WLAN, Host = 192.168.1.1, Port = <Serial-Utilities-Port>, TCP");

            col.Item().PaddingTop(8).Text("Ende · Smart ÖPNV WLAN-IBIS (Serial-over-IP)")
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
