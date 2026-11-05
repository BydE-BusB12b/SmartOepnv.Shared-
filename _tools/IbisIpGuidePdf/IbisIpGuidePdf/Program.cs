using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = args.FirstOrDefault()
             ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "SmartOepnv_Anleitung_IBIS-IP_VDV301.pdf");
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
            h.Item().Text("Anleitung: IBIS-IP / VDV 301 in der App").FontSize(16).SemiBold();
            h.Item().Text("Einstellung · Betrieb · Netzwerk · Datenformat · Fehlersuche")
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

            // —— 1 ——
            H(col, "1. Zweck dieses Dokuments");
            P(col, "Diese Anleitung beschreibt, wie IBIS-IP (VDV 301) in der Smart-ÖPNV-App " +
                   "(GPSAnsagen) genutzt und eingestellt wird. Sie richtet sich an Werkstatt, " +
                   "Betrieb und Integration: Welche Rolle die App im Fahrzeugnetz hat, welche " +
                   "Felder Pflicht sind, wie Ziele und Fahrplandaten an Peripherie gehen und " +
                   "worin sich IBIS-IP von klassischem IBIS (VDV 300 / DS021T) unterscheidet.");

            // —— 2 ——
            H(col, "2. Kurzüberblick: Was ist IBIS-IP?");
            P(col, "IBIS-IP ist die IP-basierte Fahrzeugkommunikation nach VDV 301. Statt " +
                   "ASCII-Telegrammen auf dem Wagenbus (1200 7E2) sprechen Geräte HTTP und " +
                   "XML im gleichen LAN. Dienste werden oft per DNS-SD/mDNS gefunden " +
                   "(_ibisip_http._tcp).");

            Bullet(col, "VDV 300 / klassisches IBIS: USB-Wandler, DS001/DS003/DS021T, Infomedia/Gorba über Wagenbus.");
            Bullet(col, "VDV 301 / IBIS-IP: Ethernet oder WLAN, HTTP-XML, CustomerInformationService usw.");
            Bullet(col, "Die App ist Provider (Server) im Fahrzeugnetz – Clients (Anzeigen, CIS, Diagnose) fragen ab oder abonnieren.");

            Callout(col, "Wichtig: Eine klassische Außenanzeige, die nur DS021T versteht, bekommt " +
                         "über IBIS-IP kein DS021T. Dafür braucht sie weiterhin VDV 300 (USB oder " +
                         "Serial-over-IP) oder ein Gerät, das selbst IBIS-IP als Client spricht.");

            // —— 3 ——
            H(col, "3. Rolle der App");
            P(col, "Mit gewähltem Protokoll „IBIS-IP (VDV301 – IP-Netzwerk)“ startet die App einen " +
                   "HTTP-Server (Standardport 8080) und veröffentlicht DNS-SD-Dienste. " +
                   "Betriebliche Ereignisse (Zielwahl, Haltestelle, GNSS, Ansage) aktualisieren " +
                   "den zentralen Zustand und werden Clients per GET/Subscribe bzw. optionalem " +
                   "Push bereitgestellt.");

            SimpleTable(col,
                ["Baustein", "Bedeutung"],
                [
                    ["IbisIpHttpServer", "HTTP-Provider auf konfigurierbarem Listen-Port"],
                    ["IbisIpRuntimeState", "Aktueller Zustand: DisplayContent, Stop, GNSS, Trip …"],
                    ["IbisIpBridge", "Anbindung aus dem Fahrbetrieb (Ziele, Innen, Trip, Ansage)"],
                    ["IbisIpLanNetwork", "Fahrzeug-LAN: WLAN und USB-Ethernet, Socket-Bindung"],
                    ["IbisIpDnsSdAdvertiser", "mDNS: _ibisip_http._tcp je Dienst"]
                ]);

            H2(col, "3.1 Angebotene Dienste");
            SimpleTable(col,
                ["Dienst", "Version", "Kernfunktionen"],
                [
                    ["DeviceManagementService", "2.2", "Geräte-/Dienstinfo, Status, Subscribe"],
                    ["CustomerInformationService", "2.3", "DisplayContent, Stop, Trip, Ansage, Subscribe/Push"],
                    ["GNSSLocationService", "1.0", "Position GetGNSSLocation / Subscribe"],
                    ["JourneyInformationService", "1.0", "Block/Routen/Ziele listen, Set*, Retrieve*"]
                ]);

            // —— 4 ——
            H(col, "4. Voraussetzungen");
            H2(col, "4.1 Hardware & Netzwerk");
            Bullet(col, "Tablet/Handy und IBIS-IP-Client im gleichen IP-Netz (Fahrzeug-LAN).");
            Bullet(col, "Verbindung über WLAN oder USB-Ethernet-Dongle (beides unterstützt).");
            Bullet(col, "USB-Ethernet oft ohne Internet – die App hält dieses Netz trotzdem für IBIS-IP offen.");
            Bullet(col, "Feste oder DHCP-IPs im gleichen Subnetz; Firewall darf Port 8080 (bzw. gewählten Port) nicht blockieren.");
            Bullet(col, "Für mDNS: Multicast im Netz erlaubt; bei WLAN Permission CHANGE_WIFI_MULTICAST_STATE (in der App gesetzt).");

            H2(col, "4.2 Was IBIS-IP nicht ersetzt");
            Bullet(col, "USB-IBIS zu Gorba Infomedia / Bustec / Mobitec RS485 bleibt ein eigener Kanal (VDV 300).");
            Bullet(col, "Protokollwahl „IBIS-IP“ schaltet den DS021T-Zielpfad für Außenanzeigen ab – kein paralleles DS021T an dieselbe Anzeige über denselben Protokollschalter.");
            Bullet(col, "Ticketing, Tür, LSA und weitere VDV-301-Dienste sind (noch) nicht Teil dieses Providers.");

            // —— 5 ——
            H(col, "5. Einstellungen in der App – Schritt für Schritt");
            Numbered(col, 1, "App öffnen → Einstellungen / Menü → Protokollauswahl (Anzeigeprotokoll).");
            Numbered(col, 2, "Schaltfläche „IBIS-IP (VDV301 – IP-Netzwerk)“ tippen (wird grün hervorgehoben).");
            Numbered(col, 3, "Darunter erscheinen die IBIS-IP-Felder (nur bei diesem Protokoll sichtbar).");
            Numbered(col, 4, "Listen-Port setzen (Standard 8080), optional Push konfigurieren – siehe Kapitel 6.");
            Numbered(col, 5, "„Speichern“ tippen. Die App startet den IBIS-IP-Server neu (Broadcast).");
            Numbered(col, 6, "Netzwerk prüfen: Statusseite im Browser öffnen (Kapitel 8).");

            Callout(col, "Nach dem Speichern sollte der Server laufen, ohne die App neu installieren zu müssen. " +
                         "Bei Problemen App vollständig beenden und neu starten. Logcat-Filter: IbisIpHttpServer, IbisIpLan, IbisIpDnsSd.");

            // —— 6 ——
            H(col, "6. Was muss eingestellt werden?");
            H2(col, "6.1 Pflicht");
            SimpleTable(col,
                ["Einstellung", "Pref / Ort", "Empfehlung"],
                [
                    ["Protokoll IBIS-IP", "selected_protocol = IBIS-IP", "Muss aktiv sein, sonst startet kein Provider"],
                    ["Listen-Port", "ibis_ip_listen_port", "8080, sofern frei; mit Clients abstimmen"],
                    ["Fahrzeug-LAN erreichbar", "WLAN oder USB-Ethernet", "Gleiche Subnetz-IP wie Client"]
                ]);

            H2(col, "6.2 Optional, aber oft sinnvoll");
            SimpleTable(col,
                ["Einstellung", "Pref", "Wann nötig"],
                [
                    ["Push HTTP-URL", "ibis_ip_connection_type=HTTP, ibis_ip_server_url", "Zusätzlich aktiver Push an ein festes Gerät (nicht Subscribe-Callback)"],
                    ["Push TCP Host/Port", "ibis_ip_connection_type=TCP, ibis_ip_server_host/port", "Wie oben, Roh-XML per TCP"],
                    ["Fahrzeugname", "registered_vehicle_name", "DNS-SD-Instanzname und DeviceName"],
                    ["Fahrzeugtelefon / Serial", "registered_vehicle_phone", "SerialNumber im DeviceManagement"]
                ]);

            H2(col, "6.3 Felder im Detail");
            P(col, "Listen-Port: Port, auf dem die App HTTP entgegennimmt. Clients rufen z. B. " +
                   "http://192.168.10.20:8080/CustomerInformationService/GetCurrentDisplayContent auf. " +
                   "Leer/ungültig → 8080.");
            P(col, "Optionaler Push: Unabhängig von Subscribe. Wenn gesetzt, schickt die App bei " +
                   "Zieländerungen zusätzlich XML an diese URL bzw. diesen TCP-Host. Für reine " +
                   "VDV-301-Clients mit Subscribe kann Push leer bleiben.");
            P(col, "Push HTTP: vollständige URL, z. B. http://192.168.1.50:9000/CustomerInformationService/…");
            P(col, "Push TCP: Host (IP) und Port; die App sendet den XML-Body als TCP-Payload.");

            H2(col, "6.4 Was nicht in diesem Dialog steht");
            Bullet(col, "IP-Adresse des Tablets: kommt vom System (DHCP/statisch) über WLAN oder USB-Ethernet – nicht manuell im IBIS-IP-Dialog.");
            Bullet(col, "IBIS-Transport USB vs. IP (Serial-over-IP) für VDV-300-Anzeigen: eigener Dialog „IBIS Transport“ – betrifft nicht den 301-Provider.");
            Bullet(col, "Gorba Infomedia / Bustec: eigene Schalter, paralleler USB-Wagenbus möglich, aber anderes Protokoll.");

            // —— 7 ——
            H(col, "7. Nutzung im Betrieb");
            H2(col, "7.1 Außenanzeige / Ziel");
            P(col, "Wenn IBIS-IP gewählt ist und der Fahrer ein Ziel wählt, baut die App kein DS021T. " +
                   "Stattdessen publiziert IbisIpBridge DisplayContent für Front (und ggf. Side) " +
                   "mit Linie, DestinationName, DestinationShortName und optionalen ViaPoints.");
            Bullet(col, "DisplayContentRef Front = Frontscheibe / Hauptziel.");
            Bullet(col, "DisplayContentRef Side = Seitenanzeige, falls Seitenziel vorhanden.");
            Bullet(col, "Clients holen GetCurrentDisplayContent oder erhalten Subscribe-Push / optionalen Outbound-Push.");

            H2(col, "7.2 Innen / Haltestelle / Ansage / GNSS");
            Bullet(col, "Innen- bzw. Haltentexte und Ansagen aktualisieren den Runtime-State (CIS).");
            Bullet(col, "GNSS aus dem Standortlauf speist GNSSLocationService (wenn IBIS-IP aktiv).");
            Bullet(col, "Trip-Metadaten (TripRef, RunNumber, Stops) können Journey/CIS anreichern.");

            H2(col, "7.3 Typischer Ablauf Fahrzeug");
            Numbered(col, 1, "Tablet ins Fahrzeug-LAN (WLAN-AP oder USB-Ethernet-Switch).");
            Numbered(col, 2, "Protokoll IBIS-IP speichern, Port 8080.");
            Numbered(col, 3, "Auf dem Client oder PC Statusseite öffnen und GetDeviceInformation testen.");
            Numbered(col, 4, "Fahrt starten / Ziel wählen → DisplayContent prüfen.");
            Numbered(col, 5, "Optional: Client auf CIS SubscribeCurrentDisplayContent einrichten.");

            // —— 8 ——
            H(col, "8. Statusseite und Endpunkte");
            P(col, "Im Browser (gleiches Netz): http://<Tablet-IP>:<Port>/ oder /ibis-ip");
            Bullet(col, "Zeigt Port, Subscriber-Anzahl, Haltestelle, GNSS, DisplayContent.");
            Bullet(col, "Listet lokale IPv4-URLs für WLAN und Ethernet/USB.");
            Bullet(col, "Kurze Übersicht der HTTP-Pfade der Dienste.");

            H2(col, "8.1 Wichtige URLs (Beispiele mit Port 8080)");
            SimpleTable(col,
                ["Pfad", "Zweck"],
                [
                    ["/DeviceManagementService/GetDeviceInformation", "Gerätename, Hersteller, Serial"],
                    ["/DeviceManagementService/GetServiceInformation", "Liste der angebotenen Dienste"],
                    ["/CustomerInformationService/GetCurrentDisplayContent", "Aktuelle Zieldaten (XML)"],
                    ["/CustomerInformationService/GetAllData", "CIS-Gesamtbild"],
                    ["/CustomerInformationService/GetCurrentStopPoint", "Aktuelle Haltestelle"],
                    ["/GNSSLocationService/GetGNSSLocation", "Position"],
                    ["/JourneyInformationService/GetAllData", "Journey-Überblick"],
                    ["/JourneyInformationService/ListAllDisplayContents", "Alle DisplayContents"]
                ]);
            P(col, "Subscribe*/Unsubscribe* typischerweise per POST mit Client-IP, ReplyPort, ReplyPath. " +
                   "Heartbeat-Angabe in Antworten: PT30S.");

            // —— 9 ——
            H(col, "9. Wie sehen Zieldaten aus?");
            P(col, "VDV 301 liefert XML, kein DS021T. Beispielstruktur (gekürzt):");
            Code(col,
                "CustomerInformationService.GetCurrentDisplayContentResponse\n" +
                "  CurrentDisplayContentData\n" +
                "    TimeStamp\n" +
                "    CurrentDisplayContent\n" +
                "      DisplayContentRef = Front\n" +
                "      LineInformation / LineRef, LineName, LineNumber\n" +
                "      Destination / DestinationRef, DestinationName, DestinationShortName\n" +
                "      ViaPoint… (optional)\n" +
                "      AdditionalInformation (optional)");
            P(col, "Namespace: http://www.vdv.de/ibis/ip. Texte mit Language=\"de\". " +
                   "Referenzen sind NMTOKEN-sicher (Leerzeichen → Unterstrich).");

            // —— 10 ——
            H(col, "10. DNS-SD / mDNS");
            Bullet(col, "Service-Type: _ibisip_http._tcp.");
            Bullet(col, "Instanzname: {ServiceName}-{Fahrzeuglabel} (Label aus Fahrzeugname).");
            Bullet(col, "TXT: ver, path (/ServiceName), svc.");
            Bullet(col, "Bei Wechsel von USB-Ethernet/WLAN wird DNS-SD neu registriert.");
            P(col, "Clients können Dienste so finden, ohne die Tablet-IP fest zu verdrahten – " +
                   "sofern Multicast im Fahrzeugnetz funktioniert. Sonst feste IP + Port nutzen.");

            // —— 11 ——
            H(col, "11. Netzwerk: WLAN und USB-Ethernet");
            P(col, "Der HTTP-Server lauscht auf allen Interfaces (0.0.0.0). Zusätzlich sorgt " +
                   "IbisIpLanNetwork dafür, dass ausgehende Verbindungen (Push, Serial-over-IP-Bridge) " +
                   "bevorzugt über USB-Ethernet, sonst WLAN gehen – nicht über Mobilfunk.");
            Bullet(col, "Dongle einstecken → System holt DHCP-IP → in Statusseite unter „Ethernet/USB“ sichtbar.");
            Bullet(col, "Tablet darf parallel Mobilfunk haben; Fahrzeugverkehr soll über LAN gebunden werden.");
            Bullet(col, "Logcat: IbisIpLan – „Lokale Adressen: …“.");

            // —— 12 ——
            H(col, "12. Abgrenzung zu DS021T, Gorba, Serial-over-IP");
            SimpleTable(col,
                ["Szenario", "Einstellung", "Was ankommt"],
                [
                    ["IBIS-IP-Anzeige / CIS-Client", "Protokoll IBIS-IP", "XML DisplayContent über HTTP"],
                    ["Klassische DS021T-Außenanzeige", "Protokoll DS021T + USB/IP-Transport", "DS021T-Telegramme"],
                    ["Gorba Infomedia 2010", "Infomedia/USB-IBIS (VDV 300)", "DS001a/DS021a/… – nicht 301"],
                    ["Serial-over-IP-Bridge", "IBIS Transport → IP (WLAN/Eth)", "Roh-IBIS VDV 300 zur Bridge"],
                    ["Mobitec", "Mobitec-Protokoll", "nur USB-RS485, nie IBIS-IP-XML"]
                ]);

            // —— 13 ——
            H(col, "13. Checkliste Inbetriebnahme");
            Check(col, "Fahrzeug-LAN steht (Ping Tablet ↔ Client).");
            Check(col, "Protokoll IBIS-IP gespeichert, Listen-Port notiert.");
            Check(col, "Browser: Statusseite zeigt Ethernet/WLAN-IP und Port.");
            Check(col, "GetDeviceInformation liefert SmartOepnv / Fahrzeugname.");
            Check(col, "Ziel wählen → GetCurrentDisplayContent zeigt Linie/Ziel.");
            Check(col, "Optional: Subscribe getestet oder Push-URL gesetzt.");
            Check(col, "Klar: Welche Anzeigen 301 sprechen und welche weiter VDV 300 brauchen.");

            // —— 14 ——
            H(col, "14. Fehlersuche");
            SimpleTable(col,
                ["Symptom", "Prüfen"],
                [
                    ["Statusseite nicht erreichbar", "Protokoll IBIS-IP? Port? Gleiches Subnetz? Firewall? App im Vordergrund/nicht gekillt?"],
                    ["Keine Ethernet-IP", "Dongle/OTG, DHCP, Logcat IbisIpLan, anderes Kabel/Switch"],
                    ["Client findet mDNS nicht", "Multicast/AP-Isolation; feste IP nutzen"],
                    ["Ziel ändert Anzeige nicht", "Spricht Anzeige wirklich 301? Sonst DS021T/USB nötig"],
                    ["Push kommt nicht an", "URL/Host korrekt? LAN-Bindung? Antwortcode in Logcat IbisIpBridge/IbisIpNetworkManager"],
                    ["Falsches Netz (Mobilfunk)", "USB-Ethernet/WLAN aktiv? IbisIpLan „bevorzugt Ethernet“"]
                ]);

            // —— 15 ——
            H(col, "15. Grenzen / Ausblick");
            Bullet(col, "Kein vollständiger VDV-301-Geräte-Update-Workflow.");
            Bullet(col, "Keine automatische Heartbeat-Bereinigung aller Clients.");
            Bullet(col, "Weitere Dienste (Tür, Zählung, Ticket, Video) bei Bedarf später.");
            Bullet(col, "TLS optional noch nicht Pflichtpfad.");
            Bullet(col, "Feineres Fahrplan-/PointSequence-Mapping aus lokalen Routen geplant.");

            H(col, "16. Schnellreferenz Prefs");
            SimpleTable(col,
                ["Schlüssel", "Bedeutung"],
                [
                    ["selected_protocol", "\"IBIS-IP\" aktiviert Provider"],
                    ["ibis_ip_listen_port", "HTTP Listen-Port (Default 8080)"],
                    ["ibis_ip_connection_type", "HTTP oder TCP (optionaler Outbound-Push)"],
                    ["ibis_ip_server_url", "Push-Ziel bei HTTP"],
                    ["ibis_ip_server_host / ibis_ip_server_port", "Push-Ziel bei TCP"]
                ]);

            col.Item().PaddingTop(8).Text("Ende der Anleitung · Smart ÖPNV IBIS-IP / VDV 301")
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
